using System;
using System.Collections.Generic;
using System.Linq;

namespace KikiMeter.controllers;

internal sealed class DamageTracker
{
    private sealed class DpsState
    {
        public long LastPool;
        public IntPtr Address;
        public long Frame;
        public int LastHealth;
    }

    private readonly Dictionary<uint, DpsState> states = new();
    private readonly Queue<(double Time, long Damage, uint Id, IntPtr Address)> samples = new();
    private readonly Dictionary<(uint Id, IntPtr Address), long> frameTargets = new();
    private readonly Dictionary<(uint Id, IntPtr Address), long> rollingTargets = new();
    private string skillSignature = "", pendingSkills = "";
    private double pendingSkillsSince;
    private readonly List<CombatSummary> history = new();
    private long frame, frameDamage, rollingDamage;
    private double previousTime, frameTime, combatStart, lastDamageTime;
    private bool inCombat;

    private List<KikiMeterSettings.DamageMeterMap> maps = new();
    private double? mapTime;
    private double mapFrameSeconds;
    private readonly Dictionary<KikiMeterSettings.DamageMeterMapStats, Dictionary<(string Area, uint Id, long Address), int>> mapEnemyIndices = new();
    private KikiMeterSettings.DamageMeterMap? currentSubArea;
    private string currentAreaHash = "";
    private bool inHub = true;
    public KikiMeterSettings.DamageMeterMap? CurrentMap { get; private set; }
    public string CurrentAreaHash => currentAreaHash;
    public bool MapActive => mapTime.HasValue && CurrentMap is { Finished: false };
    public bool MapDirty { get; private set; }

    public void BindMaps(List<KikiMeterSettings.DamageMeterMap> savedMaps)
    {
        maps = savedMaps;
        CurrentMap = null;
        currentSubArea = null;
        currentAreaHash = "";
        inHub = true;
        mapTime = null;
        MapDirty = false;
        // Only the newest report can remain paused between map visits.
        foreach (var map in maps.Take(Math.Max(0, maps.Count - 1)))
            if (!map.Finished) { map.Finished = true; MapDirty = true; }
        mapEnemyIndices.Clear();
    }

    public void VisitMap(string hash, string name, int level, uint processId, double now, string label)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(name) || !double.IsFinite(now))
        {
            PauseMap();
            return;
        }
        if (currentAreaHash != hash || CurrentMap?.ProcessId != processId)
        {
            bool resumedAfterRestart = CurrentMap == null;
            PauseMap();
            EndArea();
            mapEnemyIndices.Clear();
            var existing = maps.FindLast(m => m.ProcessId == processId &&
                (m.Hash == hash || m.SubAreas.Any(a => a.Hash == hash)));
            if (existing != null) CurrentMap = existing;
            else if (inHub || CurrentMap == null || CurrentMap.Finished || CurrentMap.ProcessId != processId)
            {
                foreach (var map in maps) map.Finished = true;
                CurrentMap = new KikiMeterSettings.DamageMeterMap
                    { Hash = hash, Name = name, AreaLevel = level, ProcessId = processId, Label = label, EnemySampleVersion = 1 };
                maps.Add(CurrentMap);
            }
            currentSubArea = null;
            if (CurrentMap!.Hash != hash)
            {
                currentSubArea = CurrentMap.SubAreas.Find(a => a.Hash == hash);
                if (currentSubArea == null)
                {
                    currentSubArea = new KikiMeterSettings.DamageMeterMap
                        { Hash = hash, Name = name, AreaLevel = level, ProcessId = processId, Label = CurrentMap.Label, EnemySampleVersion = 1 };
                    CurrentMap.SubAreas.Add(currentSubArea);
                }
            }
            currentAreaHash = hash;
            if (resumedAfterRestart && CurrentMap is { Finished: false } && CurrentMap.Overall.Seconds > 0)
                CurrentMap.ReadingInterrupted = true;
            MapDirty = true;
        }
        inHub = false;
        mapFrameSeconds = 0;
        if (CurrentMap.Finished) { mapTime = null; return; }
        if (mapTime is { } previous && now >= previous)
        {
            double elapsed = now - previous;
            CurrentMap.Overall.Seconds += elapsed;
            if (currentSubArea != null) currentSubArea.Overall.Seconds += elapsed;
            if (elapsed <= 0.5) mapFrameSeconds = elapsed;
            else CurrentMap.ReadingInterrupted = true;
            if (elapsed > 0) MapDirty = true;
        }
        mapTime = now;
    }

    public void PauseMap()
    {
        if (mapTime.HasValue) MapDirty = true;
        mapTime = null;
        mapFrameSeconds = 0;
    }

    public void VisitHub() { inHub = true; PauseMap(); }

    public void FinishMap()
    {
        if (CurrentMap == null || CurrentMap.Finished) return;
        CurrentMap.Finished = true;
        PauseMap();
        MapDirty = true;
    }

    public void MarkMapSaved() => MapDirty = false;

    public void ResumeMap()
    {
        if (CurrentMap == null || !CurrentMap.Finished) return;
        CurrentMap.Finished = false;
        EndArea();
        PauseMap();
        MapDirty = true;
    }

    public void MarkMapSkillChange()
    {
        if (MapActive) { CurrentMap!.SkillsChanged = true; MapDirty = true; }
    }

    private void RecordMapFrame()
    {
        if (!MapActive) return;
        RecordMapFrame(CurrentMap!);
        if (currentSubArea != null) RecordMapFrame(currentSubArea);
        if (frameDamage > 0) MapDirty = true;
        mapFrameSeconds = 0;
    }

    private void RecordMapFrame(KikiMeterSettings.DamageMeterMap map)
    {
        if (map.EnemySampleVersion == 0)
        {
            // Existing reports keep their totals. Their enemy cohorts start here,
            // rather than inventing historical hits or enemy counts.
            map.EnemySamplesPartial = map.Overall.Damage > 0;
            map.EnemySampleVersion = 1;
            MapDirty = true;
        }
        map.Overall.Damage += frameDamage;
        map.Overall.Peak = Math.Max(map.Overall.Peak, CurrentDps);
        map.Overall.LargestSample = Math.Max(map.Overall.LargestSample, frameDamage);
        RecordEnemySamples(map.Overall);
        var mode = CurrentTargets == 1 ? map.Single : CurrentTargets > 1 ? map.Multi : null;
        if (mode != null)
        {
            mode.Seconds += mapFrameSeconds;
            mode.Damage += frameDamage;
            mode.Peak = Math.Max(mode.Peak, CurrentDps);
            mode.LargestSample = Math.Max(mode.LargestSample, frameDamage);
            RecordEnemySamples(mode);
        }
    }

    private void RecordEnemySamples(KikiMeterSettings.DamageMeterMapStats stats)
    {
        if (frameTargets.Count == 0) return;
        stats.Enemies ??= new();
        if (!mapEnemyIndices.TryGetValue(stats, out var indices))
        {
            indices = new();
            for (int i = 0; i < stats.Enemies.Count; i++)
            {
                if (stats.Enemies[i].AreaHash.Length == 0) stats.Enemies[i].AreaHash = CurrentMap?.Hash ?? currentAreaHash;
                indices.TryAdd((stats.Enemies[i].AreaHash, stats.Enemies[i].Id, stats.Enemies[i].Address), i);
            }
            mapEnemyIndices[stats] = indices;
        }
        if (stats.EnemySampleDamage == null || stats.EnemySampleDamage.Count != KikiMeterSettings.DamageMeterEnemySampleSizes.Length)
        {
            stats.EnemySampleDamage = Enumerable.Repeat(0L, KikiMeterSettings.DamageMeterEnemySampleSizes.Length).ToList();
            for (int i = 0; i < stats.Enemies.Count; i++)
                for (int sample = 0; sample < stats.EnemySampleDamage.Count; sample++)
                    if (i < KikiMeterSettings.DamageMeterEnemySampleSizes[sample])
                        stats.EnemySampleDamage[sample] += stats.Enemies[i].Damage;
        }
        foreach (var target in frameTargets)
        {
            var key = (Area: currentAreaHash, target.Key.Id, Address: target.Key.Address.ToInt64());
            if (!indices.TryGetValue(key, out int index))
            {
                index = stats.Enemies.Count;
                indices.Add(key, index);
                stats.Enemies.Add(new KikiMeterSettings.DamageMeterEnemy { AreaHash = key.Area, Id = key.Id, Address = key.Address });
            }
            stats.Enemies[index].Damage += target.Value;
            stats.LargestImpact = Math.Max(stats.LargestImpact, target.Value);
            for (int sample = 0; sample < stats.EnemySampleDamage.Count; sample++)
                if (index < KikiMeterSettings.DamageMeterEnemySampleSizes[sample])
                    stats.EnemySampleDamage[sample] += target.Value;
        }
    }

    internal readonly record struct CombatSummary(double Seconds, long Damage, double Peak, int Targets = 1, bool Controlled = false)
    {
        public double Average => Damage / Math.Max(Seconds, 0.001);
    }

    private const double TrialDuration = 10;
    private readonly HashSet<(uint Id, IntPtr Address)> trialTargets = new();
    private double? trialStart;
    private long trialDamage;
    private double trialPeak;
    public bool TrialArmed { get; private set; }
    public bool TrialInterrupted { get; private set; }
    public bool TrialRunning => trialStart.HasValue;
    public double TrialSeconds => trialStart.HasValue ? Math.Clamp(frameTime - trialStart.Value, 0, TrialDuration) : 0;
    public CombatSummary? TrialResult { get; private set; }
    public CombatSummary? ComparisonSnapshot => TrialResult ?? Snapshot;

    public void ArmTrial()
    {
        RestartMeasurement();
        TrialResult = null;
        TrialInterrupted = false;
        trialTargets.Clear();
        trialDamage = 0;
        trialPeak = 0;
        TrialArmed = true;
    }

    private void InterruptTrial()
    {
        if (!TrialArmed && !TrialRunning) return;
        TrialInterrupted = true;
        TrialArmed = false;
        trialStart = null;
        TrialResult = null;
    }

    public int CurrentTargets { get; private set; }
    public int MaxTargets { get; private set; }
    public double MainTargetDps { get; private set; }
    public double MultiTargetDps => CurrentTargets > 1 ? CurrentDps : 0;
    public bool SkillChangeDetected { get; private set; }
    public CombatSummary? Snapshot => TotalDamage > 0
        ? new CombatSummary(CombatSeconds, TotalDamage, PeakDps, MaxTargets) : null;

    public bool ObserveSkills(IEnumerable<string> names, double now)
    {
        string signature = string.Join("\n", names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal));
        if (signature.Length == 0) { pendingSkills = ""; return false; }
        if (skillSignature.Length == 0) { skillSignature = signature; return false; }
        if (signature == skillSignature) { pendingSkills = ""; return false; }
        if (signature != pendingSkills) { pendingSkills = signature; pendingSkillsSince = now; return false; }
        if (now - pendingSkillsSince < 1) return false;
        skillSignature = signature;
        pendingSkills = "";
        SkillChangeDetected = true;
        return true;
    }

    public void DismissSkillChange() => SkillChangeDetected = false;

    public void RestartMeasurement()
    {
        EndArea();
        TrialResult = null;
        TrialInterrupted = false;
        TotalDamage = 0;
        PeakDps = 0;
        MaxTargets = 0;
        combatStart = lastDamageTime = 0;
    }

    public double CurrentDps { get; private set; }
    public double PeakDps { get; private set; }
    public long TotalDamage { get; private set; }
    public double CombatSeconds => TotalDamage == 0 ? 0 : Math.Max(0.001, lastDamageTime - combatStart);
    public double AverageDps => TotalDamage / Math.Max(CombatSeconds, 0.001);
    public bool InCombat => inCombat;
    public IReadOnlyList<CombatSummary> History => history;

    public void Reset()
    {
        TrialArmed = TrialInterrupted = false;
        trialStart = null;
        TrialResult = null;
        trialTargets.Clear();
        trialDamage = 0;
        trialPeak = 0;
        states.Clear();
        samples.Clear();
        history.Clear();
        frameTargets.Clear();
        rollingTargets.Clear();
        CurrentTargets = MaxTargets = 0;
        MainTargetDps = 0;
        skillSignature = pendingSkills = "";
        SkillChangeDetected = false;
        frame = frameDamage = rollingDamage = TotalDamage = 0;
        previousTime = frameTime = combatStart = lastDamageTime = CurrentDps = PeakDps = 0;
        inCombat = false;
    }

    public void EndArea()
    {
        InterruptTrial();
        FinishCombat();
        states.Clear();
        samples.Clear();
        rollingDamage = 0;
        frameTargets.Clear();
        rollingTargets.Clear();
        CurrentTargets = 0;
        MainTargetDps = 0;
        skillSignature = pendingSkills = "";
        CurrentDps = 0;
        previousTime = frameTime = 0;
    }

    public void BeginFrame(double now)
    {
        // A sampling interruption must not become an artificial burst of damage.
        if (frameTime > 0 && (now - frameTime > 0.5 || now < frameTime)) EndArea();
        previousTime = frameTime;
        frameTime = now;
        frame++;
        frameDamage = 0;
        frameTargets.Clear();
    }

    public void Observe(uint entityId, IntPtr address, int health, int shield)
    {
        if (address == IntPtr.Zero || health < 0 || shield < 0) return;
        long pool = (long)health + shield;
        if (!states.TryGetValue(entityId, out var state))
        {
            states[entityId] = new DpsState { Address = address, Frame = frame, LastPool = pool, LastHealth = health };
            return;
        }
        if (state.Frame == frame) return;
        if (state.Frame == frame - 1 && state.Address == address)
        {
            long damage = Math.Max(0L, state.LastPool - pool);
            if (damage > 0)
            {
                if (health == 0) InterruptTrial();
                frameDamage += damage;
                frameTargets[(entityId, address)] = damage;
            }
        }
        state.Address = address;
        state.Frame = frame;
        state.LastPool = pool;
        state.LastHealth = health;
    }

    public bool NeedsFinalSample(uint entityId, IntPtr address) => states.TryGetValue(entityId, out var state) &&
        state.Address == address && state.Frame == frame - 1 && state.LastHealth > 0;

    public void EndFrame()
    {
        if (inCombat && frameTime - lastDamageTime >= 3) FinishCombat();
        if (frameDamage > 0)
        {
            if (!inCombat)
            {
                TotalDamage = 0;
                PeakDps = 0;
                MaxTargets = 0;
                combatStart = previousTime > 0 ? previousTime : frameTime - 0.1;
                inCombat = true;
            }
            TotalDamage += frameDamage;
            lastDamageTime = frameTime;
            foreach (var target in frameTargets)
            {
                samples.Enqueue((frameTime, target.Value, target.Key.Id, target.Key.Address));
                rollingTargets.TryGetValue(target.Key, out long damage);
                rollingTargets[target.Key] = damage + target.Value;
            }
            rollingDamage += frameDamage;
        }
        while (samples.TryPeek(out var sample) && frameTime - sample.Time >= 1)
        {
            samples.Dequeue();
            rollingDamage -= sample.Damage;
            var key = (sample.Id, sample.Address);
            long remaining = rollingTargets[key] - sample.Damage;
            if (remaining == 0) rollingTargets.Remove(key);
            else rollingTargets[key] = remaining;
        }
        CurrentTargets = rollingTargets.Count;
        MainTargetDps = rollingTargets.Count == 0 ? 0 : rollingTargets.Values.Max();
        if (inCombat) MaxTargets = Math.Max(MaxTargets, CurrentTargets);
        CurrentDps = rollingDamage; // Damage observed over a fixed one-second window.
        PeakDps = Math.Max(PeakDps, CurrentDps);
        RecordMapFrame();
        // A fixed wall-clock window includes pauses between attacks. It is separate
        // from automatic combats, which finish after three idle seconds.
        if (TrialRunning && trialTargets.Any(target => !states.TryGetValue(target.Id, out var tracked) ||
            tracked.Address != target.Address || tracked.Frame != frame)) InterruptTrial();
        if (TrialRunning && frameTime - trialStart!.Value >= TrialDuration)
        {
            TrialResult = new CombatSummary(TrialDuration, trialDamage, trialPeak, trialTargets.Count, true);
            trialStart = null;
        }
        if (TrialArmed && frameDamage > 0)
        {
            TrialArmed = false;
            trialStart = frameTime;
        }
        if (TrialRunning)
        {
            trialDamage += frameDamage;
            foreach (var target in frameTargets.Keys) trialTargets.Add(target);
            trialPeak = Math.Max(trialPeak, CurrentDps);
        }
        // Keep baselines only for entities observed in this sample.
        foreach (var id in new List<uint>(states.Keys))
            if (states[id].Frame != frame) states.Remove(id);
    }

    private void FinishCombat()
    {
        if (!inCombat) return;
        history.Insert(0, new CombatSummary(CombatSeconds, TotalDamage, PeakDps, MaxTargets));
        if (history.Count > 10) history.RemoveAt(history.Count - 1);
        inCombat = false;
    }

}
