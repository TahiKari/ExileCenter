namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using ImGuiNET;
    using Newtonsoft.Json;

    // ── Persisted session-history model ──────────────────────────────────
    // One priced line of a map's net gains, snapshotted at session-save time (so a historical record
    // stays stable regardless of later price moves).
    public sealed class SessionLootLine
    {
        public string Label = string.Empty; // English display name (or art id fallback)
        public long Count;
        public double Ex;                    // total Exalted value of this line at save time (0 if unpriced)
        public bool Priced;
    }

    // A completed map within a saved session.
    public sealed class SessionMap
    {
        public string Hash = string.Empty;
        public DateTime StartedUtc;
        public string League = string.Empty;
        public string ParentHash = string.Empty;
        public string Name = string.Empty;
        public double ActiveSeconds;         // wall time spent inside the map
        public double ProfitEx;              // Σ line.Ex
        public List<SessionLootLine> Loot = new();
        [JsonIgnore] public MapRun? LiveRun;
        [JsonIgnore] public double? ArchivedDivineRate;
    }

    // A finished tracking session (everything between two "New session" presses).
    public sealed class SessionRecord
    {
        public string League = string.Empty;
        public bool HistoryIncomplete;
        public DateTime StartUtc;
        public DateTime EndUtc;
        public double DivineRate;            // Divine→Exalted rate at save time (0 = unknown)
        public List<SessionMap> Maps = new();

        // On-disk path, filled on load so the row can be deleted. Not serialized.
        [JsonIgnore]
        public string FilePath = string.Empty;

        public double TotalEx()
        {
            double s = 0;
            foreach (var m in this.Maps.Where(m => m.ParentHash.Length == 0)) s += m.ProfitEx;
            return s;
        }

        public double TotalActiveSeconds()
        {
            double s = 0;
            foreach (var m in this.Maps.Where(m => m.ParentHash.Length == 0)) s += m.ActiveSeconds;
            return s;
        }

        public double PerHourEx()
        {
            double h = this.TotalActiveSeconds() / 3600.0;
            return h > 0 ? this.TotalEx() / h : 0;
        }

        public double ToDivine(double ex) => this.DivineRate > 0 ? ex / this.DivineRate : 0;
    }

    // The live (in-progress) session, autosaved separately from the archived history so a GH close or a
    // game crash doesn't lose it. Holds the raw MapRun list (unpriced deltas) so pricing stays live on
    // restore — unlike SessionRecord, which snapshots prices at archive time.
    public sealed class ActiveSessionState
    {
        public bool HistoryIncomplete;
        public DateTime StartUtc;
        public List<MapRun> Completed = new();
    }

    public sealed partial class LootTrackerCore
    {
        private string SessionsDir => Path.Join(this.DllDirectory, "config", "sessions");
        private string ActiveSessionPathname => Path.Join(this.DllDirectory, "config", "active.json");
        private DateTime nextAutoSaveUtc = DateTime.MinValue;
        private bool trackingHistoryIncomplete, periodHistoryUnreadable;
        private int periodExcludedMaps;
        private DateTime? periodFirstKnownUtc;

        // Autosave the live session to active.json. Called on zone transitions, on a ~20s timer (to catch
        // mid-map loot), and on disable. The currently-active run gets its un-folded live leg + running
        // time baked into the saved copy, so a crash keeps progress up to the last autosave; the in-memory
        // state is untouched (active.json is a mirror, never read back during normal play). Best-effort.
        private void SaveActiveState()
        {
            try
            {
                if (this.completed.Count == 0)
                {
                    this.DeleteActiveState();
                    return;
                }

                var state = new ActiveSessionState { StartUtc = this.sessionStartUtc, HistoryIncomplete = this.trackingHistoryIncomplete };
                foreach (var r in this.completed)
                {
                    bool isActive = ReferenceEquals(r, this.current) && this.onMapArea;
                    state.Completed.Add(new MapRun
                    {
                        Name = r.Name,
                        StartedUtc = r.StartedUtc,
                        League = r.League,
                        Hash = r.Hash,
                        ParentHash = r.ParentHash,
                        ProcessId = r.ProcessId,
                        SekhemaFloor = r.SekhemaFloor,
                        SekhemaAreaHashes = new List<string>(r.SekhemaAreaHashes),
                        AreaLevel = r.AreaLevel,
                        IsFinished = r.IsFinished,
                        ActiveTime = isActive ? this.CurrentLiveTime() : r.ActiveTime,
                        Gained = new Dictionary<string, long>(isActive ? this.CurrentGainedLive() : r.Gained, StringComparer.Ordinal),
                        Kills = (int[])r.Kills.Clone(),
                    });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(this.ActiveSessionPathname)!);
                File.WriteAllText(this.ActiveSessionPathname, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch
            {
                // best-effort; a failed autosave just means this tick isn't crash-protected.
            }
        }

        // Restore a live session from active.json (called on enable). Brings back the completed map list
        // (loot/time/kills) with the timer paused; re-entering a map resumes its run by hash and keeps
        // accumulating. No-op if there's nothing to restore.
        private void LoadActiveState()
        {
            try
            {
                if (!File.Exists(this.ActiveSessionPathname))
                {
                    return;
                }

                var state = JsonConvert.DeserializeObject<ActiveSessionState>(File.ReadAllText(this.ActiveSessionPathname));
                if (state?.Completed == null || state.Completed.Count == 0)
                {
                    return;
                }

                this.completed.Clear();
                this.completed.AddRange(state.Completed);
                this.trackingHistoryIncomplete = state.HistoryIncomplete;
                this.sessionStartUtc = state.StartUtc == default ? DateTime.UtcNow : state.StartUtc;
                this.current = null;
                this.runStartUtc = null;
                this.baseline = null;
                this.baselinePending = false;
                this.prevSnapshot = null;
                this.lastProcessedZoneHash = string.Empty;
            }
            catch
            {
                // a corrupt autosave just means we start fresh.
            }
        }

        private void DeleteActiveState()
        {
            try
            {
                if (File.Exists(this.ActiveSessionPathname))
                {
                    File.Delete(this.ActiveSessionPathname);
                }
            }
            catch
            {
                // ignore
            }
        }

        // History-window state (transient, not persisted).
        private bool showSessionHistory;
        private List<SessionRecord>? sessionCache;
        private SessionRecord? detailSession;
        private SessionMap? lootMap;        // history loot view (priced snapshot at archive time)
        private MapRun? lootRun;            // active-session loot view (re-priced live each frame)

        // ── Persistence ──────────────────────────────────────────────────
        // Snapshot the session being ended (the completed runs, plus the in-progress one banked first)
        // and write it to its own json under config/sessions. No-op when nothing was run.
        private void SaveCurrentSession()
        {
            this.BankActiveTime(DateTime.UtcNow); // include the active run's still-running time
            if (this.completed.Count == 0)
            {
                return;
            }

            var rec = this.BuildSessionRecord(this.sessionStartUtc, DateTime.UtcNow);
            this.WriteSession(rec);
            this.TrimSessions();
        }

        // Price every completed run's net gains at the current rates and assemble the record.
        private SessionRecord BuildSessionRecord(DateTime startUtc, DateTime endUtc)
        {
            var rec = new SessionRecord
            {
                StartUtc = startUtc,
                EndUtc = endUtc,
                DivineRate = this.priceCache.DivineToExaltedRate,
                HistoryIncomplete = this.trackingHistoryIncomplete,
                League = this.completed.Select(r => r.League).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? this.completed[0].League : string.Empty,
            };

            foreach (var r in this.completed)
            {
                rec.Maps.Add(this.BuildSessionMap(r));
            }

            return rec;
        }

        // Price one run's net gains at the CURRENT rates into a SessionMap (loot lines sorted by value).
        // Shared by session archiving and the live "Active session" loot view in settings.
        private SessionMap BuildSessionMap(MapRun r, bool includeLoot = true)
        {
            var m = new SessionMap { Name = r.Name, Hash = r.Hash, StartedUtc = r.StartedUtc, League = r.League,
                ParentHash = r.ParentHash, ActiveSeconds = this.RunDisplayTime(r).TotalSeconds };
            if (!includeLoot)
            {
                m.LiveRun = r;
                m.ProfitEx = this.ValueOf(this.RunGained(r), out _, out _);
                return m;
            }
            double profit = 0;
            foreach (var kv in this.RunGained(r))
            {
                if (kv.Value == 0) continue;
                bool priced = this.TryPriceItem(kv.Key, out var unit, out var label);
                double ex = priced ? unit * kv.Value : 0;
                m.Loot.Add(new SessionLootLine { Label = label, Count = kv.Value, Ex = ex, Priced = priced });
                profit += ex;
            }

            m.Loot.Sort((a, b) => Math.Abs(b.Ex).CompareTo(Math.Abs(a.Ex)));
            m.ProfitEx = profit;
            return m;
        }

        private void WriteSession(SessionRecord rec)
        {
            try
            {
                Directory.CreateDirectory(this.SessionsDir);
                // Timestamp filename → lexical order == chronological order (used by the trim).
                var name = $"session_{rec.StartUtc:yyyyMMdd_HHmmss_fff}.json";
                File.WriteAllText(Path.Join(this.SessionsDir, name), JsonConvert.SerializeObject(rec, Formatting.Indented));
                this.sessionCache = null;
            }
            catch
            {
                // history is best-effort; a failed write just means this session isn't archived.
            }
        }

        // Drop the oldest session files past the keep-limit.
        private void TrimSessions()
        {
            try
            {
                if (!Directory.Exists(this.SessionsDir)) return;
                var files = Directory.GetFiles(this.SessionsDir, "session_*.json");
                int max = Math.Max(1, this.Settings.MaxSessions);
                if (files.Length <= max) return;
                Array.Sort(files, StringComparer.Ordinal); // oldest first
                for (int i = 0; i < files.Length - max; i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch
            {
                // ignore
            }
        }

        // (Re)load all saved sessions from disk into the cache, newest first.
        private void LoadSessions()
        {
            var list = new List<SessionRecord>();
            this.periodHistoryUnreadable = false;
            try
            {
                if (Directory.Exists(this.SessionsDir))
                {
                    foreach (var path in Directory.GetFiles(this.SessionsDir, "session_*.json"))
                    {
                        try
                        {
                            var rec = JsonConvert.DeserializeObject<SessionRecord>(File.ReadAllText(path));
                            if (rec != null)
                            {
                                rec.FilePath = path;
                                list.Add(rec);
                            }
                        }
                        catch
                        {
                            this.periodHistoryUnreadable = true;
                            // skip a corrupt file
                        }
                    }
                }
            }
            catch
            {
                this.periodHistoryUnreadable = true;
                // ignore
            }

            list.Sort((a, b) => b.StartUtc.CompareTo(a.StartUtc));
            this.sessionCache = list;
        }

        private void DeleteSession(SessionRecord rec)
        {
            try
            {
                if (!string.IsNullOrEmpty(rec.FilePath) && File.Exists(rec.FilePath))
                {
                    File.Delete(rec.FilePath);
                }
            }
            catch
            {
                // ignore
            }

            this.sessionCache?.Remove(rec);
            // Only an explicit, complete clear is an accounting reset; a new session is not.
            try
            {
                if (this.completed.Count == 0 && this.sessionCache?.Count == 0 &&
                    (!Directory.Exists(this.SessionsDir) || !Directory.EnumerateFiles(this.SessionsDir, "session_*.json").Any()))
                {
                    this.Settings.TrackingResetUtc = DateTime.UtcNow;
                    this.SaveSettings();
                }
            }
            catch { /* Inaccessible history must not be treated as a complete reset. */ }
            if (ReferenceEquals(this.detailSession, rec))
            {
                this.detailSession = null;
                this.lootMap = null;
            }
        }

        // ── History windows ──────────────────────────────────────────────
        // Top-level list of saved sessions: date · length · maps · total Divine · Divine/h, with
        // per-row Details / Delete. Opened from the plugin settings button.
        private void DrawSessionHistoryWindow()
        {
            if (!this.showSessionHistory)
            {
                return;
            }

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(760, 380), ImGuiCond.FirstUseEver);
            bool open = true;
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow(this.Loc.Title("lt.session_history", "Session history", "lt_session_history"), ref open))
            {
                if (ImGui.Button(this.L("lt.refresh", "Refresh")))
                {
                    this.LoadSessions();
                }

                ImGui.SameLine();
                ImGui.TextDisabled(this.LF("lt.sessions_summary", "{0} saved · keeping last {1}", this.sessionCache?.Count ?? 0, this.Settings.MaxSessions));

                if (ImGui.BeginTable("sessions_tbl", 6,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(this.L("lt.col_date", "Date"));
                    ImGui.TableSetupColumn(this.L("lt.col_length", "Length"), ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn(this.L("lt.col_maps", "Maps"), ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn(this.L("lt.col_total_div", "Total div"), ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn(this.L("lt.col_div_h", "Div/h"), ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableHeadersRow();

                    SessionRecord? toDelete = null;
                    var cache = this.sessionCache;
                    if (cache != null)
                    {
                        for (int i = 0; i < cache.Count; i++)
                        {
                            var rec = cache[i];
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted($"{rec.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(FormatDuration(rec.EndUtc - rec.StartUtc));
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(rec.Maps.Count(m => m.ParentHash.Length == 0).ToString());
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(rec.DivineRate > 0 ? $"{rec.ToDivine(rec.TotalEx()):0.0}" : "—");
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(rec.DivineRate > 0 ? $"{rec.ToDivine(rec.PerHourEx()):0.0}" : "—");
                            ImGui.TableNextColumn();
                            if (ImGui.SmallButton($"{this.L("lt.details", "Details")}##s{i}"))
                            {
                                this.detailSession = rec;
                                this.lootMap = null;
                            }

                            ImGui.SameLine();
                            if (ImGui.SmallButton($"{this.L("lt.delete", "Delete")}##s{i}"))
                            {
                                toDelete = rec;
                            }
                        }
                    }

                    ImGui.EndTable();

                    if (toDelete != null)
                    {
                        this.DeleteSession(toDelete);
                    }
                }
            }

            ImGui.End();
            if (!open)
            {
                this.showSessionHistory = false;
            }
        }

        // One session in full: the six aggregates + the per-map table, each map openable for its loot.
        private void DrawSessionDetailWindow()
        {
            var rec = this.detailSession;
            if (rec == null)
            {
                return;
            }

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(620, 420), ImGuiCond.FirstUseEver);
            bool open = true;
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow($"{this.L("lt.session", "Session")} {rec.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm}###session_detail", ref open))
            {
                int maps = rec.Maps.Count(m => m.ParentHash.Length == 0);
                double totalEx = rec.TotalEx();
                double activeS = rec.TotalActiveSeconds();
                var avgTime = maps > 0 ? TimeSpan.FromSeconds(activeS / maps) : TimeSpan.Zero;
                double avgEx = maps > 0 ? totalEx / maps : 0;
                string totalDiv = rec.DivineRate > 0 ? $"{rec.ToDivine(totalEx):0.0}" : "—";
                string hourDiv = rec.DivineRate > 0 ? $"{rec.ToDivine(rec.PerHourEx()):0.0}" : "—";

                // Divine-only display converts by the session's OWN saved rate (rec.DivineRate), never the
                // live rate — a two-week-old session keeps the value it had then.
                bool divHist = this.Settings.ShowPricesInDivineOnly && rec.DivineRate > 0;

                ImGui.Text(this.LF("lt.duration", "Duration: {0}", FormatDuration(rec.EndUtc - rec.StartUtc)));
                ImGui.Text(this.LF("lt.maps_completed", "Maps completed: {0}", maps));
                ImGui.Text(this.LF("lt.avg_time_map", "AVG time in map: {0}", FormatDuration(avgTime)));
                ImGui.Text(divHist ? this.LF("lt.avg_profit_div", "AVG profit: {0:0.##} Div", rec.ToDivine(avgEx)) : this.LF("lt.avg_profit_ex", "AVG profit: {0:0} Ex", avgEx));
                ImGui.Text(this.LF("lt.total_divine", "Total: {0} Divine", totalDiv));
                ImGui.Text(this.LF("lt.per_hour", "Per hour: {0} Divine / hour", hourDiv));
                ImGui.Separator();

                if (ImGui.BeginTable("detail_maps_tbl", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(this.L("lt.col_map", "Map"));
                    ImGui.TableSetupColumn(this.L("lt.col_time", "Time"), ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn(this.L("lt.col_profit", "Profit"), ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableHeadersRow();

                    for (int i = 0; i < rec.Maps.Count; i++)
                    {
                        var m = rec.Maps[i];
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted((m.ParentHash.Length == 0 ? "" : "    > ") + m.Name);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(FormatDuration(TimeSpan.FromSeconds(m.ActiveSeconds)));
                        ImGui.TableNextColumn();
                        ImGui.TextColored(m.ProfitEx >= 0 ? GreenCol : RedCol, divHist
                            ? $"{rec.ToDivine(m.ProfitEx):+0.##;-0.##;0} div"
                            : $"{m.ProfitEx:+0.0;-0.0;0} ex");
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"{this.L("lt.loot", "Loot")}##m{i}"))
                        {
                            this.lootMap = m;
                            this.lootRun = null; // history view takes over from any active-session view
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.End();
            if (!open)
            {
                this.detailSession = null;
                this.lootMap = null;
            }
        }

        // What a single map dropped. Two sources share this window: a history map (priced snapshot,
        // converted by the saved session rate) and a live active-session run (re-priced every frame at
        // the current rate). lootRun takes priority when both happen to be set.
        private void DrawMapLootWindow()
        {
            SessionMap? m;
            double rate;
            if (this.lootRun != null)
            {
                m = this.BuildSessionMap(this.lootRun);
                rate = this.priceCache.DivineToExaltedRate; // live run → current rate
            }
            else
            {
                m = this.lootMap;
                rate = m?.ArchivedDivineRate ?? this.detailSession?.DivineRate ?? 0; // history → that session's saved rate
            }

            if (m == null)
            {
                return;
            }

            bool divHist = this.Settings.ShowPricesInDivineOnly && rate > 0;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 360), ImGuiCond.FirstUseEver);
            bool open = true;
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow($"{this.L("lt.loot", "Loot")} — {m.Name}###map_loot", ref open))
            {
                ImGui.TextColored(m.ProfitEx >= 0 ? GreenCol : RedCol, divHist
                    ? $"{m.ProfitEx / rate:+0.##;-0.##;0} div"
                    : $"{m.ProfitEx:+0.0;-0.0;0} ex");
                ImGui.SameLine();
                ImGui.TextDisabled($"· {FormatDuration(TimeSpan.FromSeconds(m.ActiveSeconds))}");
                ImGui.Separator();

                if (ImGui.BeginTable("map_loot_tbl", 3,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(this.L("lt.col_item", "Item"));
                    ImGui.TableSetupColumn(this.L("lt.col_count", "Count"), ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn(this.L("lt.col_value", "Value"), ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableHeadersRow();

                    foreach (var line in m.Loot)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(line.Label);
                        ImGui.TableNextColumn();
                        ImGui.TextColored(line.Count >= 0 ? GreenCol : RedCol, $"{line.Count:+0;-0}");
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(!line.Priced ? "—"
                            : divHist ? $"{line.Ex / rate:+0.##;-0.##;0} div"
                            : $"{line.Ex:+0.0;-0.0;0} ex");
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.End();
            if (!open)
            {
                this.lootMap = null;
                this.lootRun = null;
            }
        }

        private static readonly string[] TrackingPeriods = { "session", "total", "three_months", "month", "week", "today" };

        private static DateTime TrackingUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() :
            DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static DateTime TrackingPeriodStart(int period, DateTime nowUtc, TimeZoneInfo zone, DateTime resetUtc)
        {
            var today = TimeZoneInfo.ConvertTimeFromUtc(TrackingUtc(nowUtc), zone).Date;
            DateTime local = period switch
            {
                2 => today.AddMonths(-3),
                3 => new DateTime(today.Year, today.Month, 1),
                4 => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
                5 => today,
                _ => DateTime.MinValue,
            };
            // Some local zones change clocks at midnight. Start at the first valid local minute.
            if (local != DateTime.MinValue)
                while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var start = local == DateTime.MinValue ? DateTime.MinValue : TimeZoneInfo.ConvertTimeToUtc(local, zone);
            return resetUtc != default && TrackingUtc(resetUtc) > start ? TrackingUtc(resetUtc) : start;
        }

        private SessionRecord BuildTrackingView(DateTime nowUtc, TimeZoneInfo zone)
        {
            int period = Math.Clamp(this.Settings.TrackingPeriod, 0, TrackingPeriods.Length - 1);
            var start = TrackingPeriodStart(period, nowUtc, zone, this.Settings.TrackingResetUtc);
            var view = new SessionRecord { StartUtc = start, EndUtc = nowUtc, DivineRate = this.priceCache.DivineToExaltedRate };
            this.periodExcludedMaps = 0;
            this.periodFirstKnownUtc = null;
            var live = new SessionRecord { StartUtc = this.sessionStartUtc, EndUtc = nowUtc,
                HistoryIncomplete = this.trackingHistoryIncomplete,
                Maps = this.completed.Where(r => r.ParentHash.Length == 0).Reverse().SelectMany(this.RunFamily)
                    .Select(r => this.BuildSessionMap(r, false)).ToList() };
            if (period == 0) return live; // Original session scope, with live pricing/timers and no disk reads.
            if (this.sessionCache == null) this.LoadSessions();
            foreach (var session in new[] { live }.Concat((this.sessionCache ?? new List<SessionRecord>())
                .Where(session => session.StartUtc != live.StartUtc).GroupBy(session => session.StartUtc)
                .Select(group => group.OrderByDescending(session => session.EndUtc).First())))
            {
                if (session.Maps == null) { view.HistoryIncomplete = true; continue; }
                view.HistoryIncomplete |= session.HistoryIncomplete;
                foreach (var map in session.Maps.Where(m => m != null))
                {
                    var root = map.ParentHash.Length == 0 ? map : session.Maps.FirstOrDefault(m => m != null &&
                        m.ParentHash.Length == 0 && m.Hash.Length > 0 && m.Hash == map.ParentHash) ?? map;
                    string league = !string.IsNullOrEmpty(root.League) ? root.League : session.League;
                    if (string.IsNullOrEmpty(league)) { if (map.ParentHash.Length == 0) this.periodExcludedMaps++; continue; }
                    if (!string.Equals(league, this.Settings.League, StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime? date = root.StartedUtc == default ? null : TrackingUtc(root.StartedUtc);
                    if (date.HasValue && date.Value >= TrackingUtc(this.Settings.TrackingResetUtc) && date.Value <= nowUtc &&
                        (!this.periodFirstKnownUtc.HasValue || date.Value < this.periodFirstKnownUtc.Value))
                        this.periodFirstKnownUtc = date;
                    // Undated legacy maps may only be included if their ENTIRE known session fits.
                    if (!date.HasValue && (session.StartUtc == default || session.EndUtc == default ||
                        TrackingUtc(session.StartUtc) < start || TrackingUtc(session.EndUtc) > nowUtc))
                    { if (map.ParentHash.Length == 0) this.periodExcludedMaps++; continue; }
                    if (date.HasValue && (date.Value < start || date.Value > nowUtc)) continue;
                    if (!double.IsFinite(map.ProfitEx) || !double.IsFinite(map.ActiveSeconds) || map.ActiveSeconds < 0)
                    { view.HistoryIncomplete = true; continue; }
                    view.Maps.Add(map.LiveRun != null ? map : new SessionMap { Name = map.Name, Hash = map.Hash,
                        ParentHash = map.ParentHash, StartedUtc = map.StartedUtc, League = map.League,
                        ActiveSeconds = map.ActiveSeconds, ProfitEx = map.ProfitEx, Loot = map.Loot,
                        ArchivedDivineRate = session.DivineRate });
                }
            }
            view.HistoryIncomplete |= this.periodHistoryUnreadable || this.periodExcludedMaps > 0;
            return view;
        }

        private void DrawTrackingPeriodSelector()
        {
            int selected = Math.Clamp(this.Settings.TrackingPeriod, 0, TrackingPeriods.Length - 1);
            ImGui.SetNextItemWidth(Math.Min(260f, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo("##tracking_period", this.L("lt.period." + TrackingPeriods[selected], TrackingPeriods[selected])))
            {
                for (int i = 0; i < TrackingPeriods.Length; i++)
                    if (ImGui.Selectable(this.L("lt.period." + TrackingPeriods[i], TrackingPeriods[i]), i == selected))
                    { this.Settings.TrackingPeriod = i; this.SaveSettings(); }
                ImGui.EndCombo();
            }
        }

        // Compact-style table of the current session's maps (newest first) for the plugin settings, with
        // a per-row Loot button that opens the same loot window history uses — re-priced live.
        private void DrawActiveSessionTable()
        {
            float height = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 16f);
            bool visible = ImGui.BeginChild("active_session_dashboard", new System.Numerics.Vector2(0f, height));
            try
            {
                if (visible) this.DrawTrackerContents(height, 1f, true);
            }
            finally
            {
                ImGui.EndChild();
            }
        }
    }
}
