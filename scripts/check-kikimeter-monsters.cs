// Run via scripts/run-check.ps1 in an isolated fixture, with the release directory as argument.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using ExileCenter.Localization;
using ImGuiNET;
using KikiMeter;
using ExileCenter.RemoteEnums;
using Newtonsoft.Json;

internal static class KikiMonsterChecks
{
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static int checks;
    private static void Check(bool condition,string message)
    { if(!condition)throw new InvalidOperationException(message); checks++; }
    private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags)!.Invoke(target,args)!;
    private static T Get<T>(object target,string name)=>(T)target.GetType().GetProperty(name,Flags)!.GetValue(target)!;
    private static object Tracker()=>Activator.CreateInstance(typeof(KikiMeter.KikiMeter).Assembly.GetType("KikiMeter.controllers.DamageTracker")!,true)!;
    private readonly record struct Monster(uint Id,int Hp,Rarity? Rarity,bool Alive,long Address=0,int Shield=0);
    private static void Frame(object tracker,double time,params Monster[] monsters)
    {
        Call(tracker,"BeginFrame",time);
        foreach(var m in monsters)Call(tracker,"ObserveMonster",m.Id,new IntPtr(m.Address==0?1000+m.Id:m.Address),m.Hp,m.Shield,m.Rarity!,m.Alive);
        Call(tracker,"EndFrame");
    }
    private static void Visit(object tracker,string hash,double time)=>Call(tracker,"VisitMap",hash,"Test map",80,1u,time,"");

    public static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving+=(_,name)=>
            {
                string path=Path.Combine(root,name.Name+".dll");
                return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
            };
            if(args.Length>1&&args[1]=="--window-only")
            {
                CheckWindow(root);Console.WriteLine(checks+" Kikimeter window checks passed; no game process or OS input.");return 0;
            }
            Run(); Console.WriteLine(checks+" Kikimeter monster-counter checks passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }

    private static unsafe void CheckWindow(string root)
    {
        var context=ImGui.CreateContext();
        var theme=typeof(ExileCenter.Ui.ImGuiTheme);
        var textureField=theme.GetField("frameTexture",Flags)!;
        var previousTexture=textureField.GetValue(null);
        var previousLanguage=ExileCenter.Core.GHSettings.UiLanguage;
        try
        {
            var io=ImGui.GetIO();io.NativePtr->IniFilename=null;
            io.DisplaySize=new Vector2(1920,1080);io.DeltaTime=1f/60;
            io.Fonts.AddFontDefault();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels,out int width,out int height,out int bytes);
            io.Fonts.SetTexID(new IntPtr(2));io.FontGlobalScale=1.25f;
            textureField.SetValue(null,new IntPtr(1));
            var text=new PluginLocalization(Path.Combine(root,"Plugins","KikiMeter"));
            var rendererType=typeof(KikiMeter.KikiMeter).Assembly.GetType("KikiMeter.render.DamageMeterRenderer")!;
            foreach(var language in new[]{OverlayLanguage.French,OverlayLanguage.English})
            foreach(bool detached in new[]{false,true})
            foreach(bool locked in new[]{false,true})
            foreach(bool notice in new[]{false,true})
            foreach(var size in new[]{new Vector2(320,220),new Vector2(426,381),new Vector2(460,420)})
            {
                ExileCenter.Core.GHSettings.UiLanguage=language;
                var renderer=Activator.CreateInstance(rendererType,true)!;
                var tracker=Tracker();tracker.GetType().GetProperty("SkillChangeDetected",Flags)!.SetValue(tracker,notice);
                var settings=new KikiMeterSettings{DamageMeterPosition=new Vector2(1144,41),DamageMeterSize=size,DamageMeterLocked=locked};
                string saved=JsonConvert.SerializeObject(settings);
                string title=detached?"##AuraTrackerDesktop":text.Title("meter.title","Damage meter (estimated)","AuraTrackerDamageMeter");
                for(int frame=0;frame<40;frame++)
                {
                    ImGui.NewFrame();
                    bool save=(bool)Call(renderer,"RenderDamageMeter",tracker,settings,text,detached?size:io.DisplaySize,detached);
                    ImGui.Begin(title);
                    var draw=ImGui.GetWindowDrawList();
                    Vector2 artworkSize=Vector2.Zero;
                    for(int command=0;command<draw.CmdBuffer.Size;command++)
                    {
                        var cmd=draw.CmdBuffer[command];
                        if(cmd.GetTexID()!=new IntPtr(1)||cmd.ElemCount<3)continue;
                        var minimum=new Vector2(float.MaxValue);var maximum=new Vector2(float.MinValue);
                        for(int vertex=0;vertex<3;vertex++)
                        {
                            var position=draw.VtxBuffer[(int)cmd.VtxOffset+draw.IdxBuffer[(int)cmd.IdxOffset+vertex]].pos;
                            minimum=Vector2.Min(minimum,position);maximum=Vector2.Max(maximum,position);
                        }
                        artworkSize=maximum-minimum;break;
                    }
                    var positionNow=ImGui.GetWindowPos();var sizeNow=ImGui.GetWindowSize();
                    ImGui.End();ImGui.Render();
                    if(frame<8)continue;
                    string scenario=$"{language}, detached={detached}, locked={locked}, notice={notice}, size={size}, frame={frame}";
                    Check(Vector2.Distance(artworkSize,size)<.1f,"The decorated frame keeps the chosen size without scrollbar oscillation: "+scenario+", artwork="+artworkSize);
                    Check(sizeNow==size&&positionNow==(detached?Vector2.Zero:settings.DamageMeterPosition),"The window keeps its user-selected dimensions and position: "+scenario);
                    Check(!save&&JsonConvert.SerializeObject(settings)==saved,"Rendering never rewrites the user's placement or settings: "+scenario);
                }
            }
        }
        finally
        {
            textureField.SetValue(null,previousTexture);
            ExileCenter.Core.GHSettings.UiLanguage=previousLanguage;
            ImGui.DestroyContext(context);
        }
    }

    private static void Run()
    {
        var tracker=Tracker(); var maps=new List<KikiMeterSettings.DamageMeterMap>();Call(tracker,"BindMaps",maps);
        var all=Enumerable.Range(0,7).Select(i=>new Monster((uint)(i+1),1000,i<3?(Rarity)i:Rarity.Unique,true)).ToArray();
        Visit(tracker,"main",10);Frame(tracker,10,all);
        Check(Get<IReadOnlyCollection<KikiMeterSettings.DamageMeterEnemy>>(tracker,"CombatEnemies").Count==0,"A baseline is not a hit or a kill");
        Visit(tracker,"main",10.1);Frame(tracker,10.1,all.Select(m=>m with{Hp=900}).ToArray());
        var map=maps.Single();
        Check(map.Overall.Enemies.Count==7&&map.Overall.Damage==700,"Seven distinct hits preserve damage totals");
        for(int category=0;category<4;category++)
            Check(map.Overall.Enemies.Count(e=>e.Rarity==(Rarity)category)==(category==3?4:1),"Correct category "+category);
        Check(map.Overall.Enemies.All(e=>e.DeathObserved==false),"Live hits do not count as deaths");
        Check(map.MonsterCountsVersion==1&&!map.MonsterCountsPartial,"Fresh report has full counter coverage since tracking began");
        Visit(tracker,"main",10.2);Frame(tracker,10.2,all.Select(m=>m with{Hp=800}).ToArray());
        Check(map.Overall.Enemies.Count==7&&map.Overall.Damage==1400,"Repeated hits count each monster once");
        Visit(tracker,"main",10.3);Frame(tracker,10.3,all.Select(m=>m with{Hp=0,Alive=false}).ToArray());
        Check(map.Overall.Enemies.Count(e=>e.DeathObserved==true)==7,"Observed alive-to-dead transitions count all rarities");
        Check(map.Multi.Enemies.Count(e=>e.DeathObserved==true)==7,"Multi-target cohort deaths persist");
        Check(Get<IReadOnlyCollection<KikiMeterSettings.DamageMeterEnemy>>(tracker,"CombatEnemies").Count(e=>e.DeathObserved==true)==7,"Live combat has the same confirmed deaths");
        Visit(tracker,"main",10.4);Frame(tracker,10.4,all.Select(m=>m with{Hp=0,Alive=false}).ToArray());
        Check(map.Overall.Enemies.Count(e=>e.DeathObserved==true)==7,"Corpses do not duplicate deaths");
        var saved=JsonConvert.DeserializeObject<KikiMeterSettings.DamageMeterMap>(JsonConvert.SerializeObject(map))!;
        Check(saved.Overall.Enemies.Count(e=>e.DeathObserved==true)==7&&saved.Overall.Enemies.Count(e=>e.Rarity==Rarity.Unique)==4,"Rarities and deaths survive settings serialization");
        var renderer=typeof(KikiMeter.KikiMeter).Assembly.GetType("KikiMeter.render.DamageMeterRenderer")!;
        string Count(int category)=>(string)renderer.GetMethod("MonsterCount",Flags)!.Invoke(null,new object[]{saved.Overall.Enemies,category})!;
        Check(Count(0)=="1 / 1"&&Count(1)=="1 / 1"&&Count(2)=="1 / 1"&&Count(3)=="4 / 4","Actual map-column formatter reports hit / deaths");

        // A health-zero phase does not prove death. An actual alive flag is required.
        var phase=Tracker();var phaseMaps=new List<KikiMeterSettings.DamageMeterMap>();Call(phase,"BindMaps",phaseMaps);
        Visit(phase,"phase",20);Frame(phase,20,new Monster(1,1000,Rarity.Unique,true));
        Visit(phase,"phase",20.1);Frame(phase,20.1,new Monster(1,0,Rarity.Unique,true));
        Check(phaseMaps[0].Overall.Enemies.Single().DeathObserved==false,"Zero health with a live flag is not a boss kill");
        Visit(phase,"phase",20.2);Call(phase,"BeginFrame",20.2);
        Check((bool)Call(phase,"NeedsFinalSample",1u,new IntPtr(1001)),"A live zero-health enemy still needs a final death sample");
        Call(phase,"ObserveMonster",1u,new IntPtr(1001),0,0,Rarity.Unique,false);Call(phase,"EndFrame");
        Check(phaseMaps[0].Overall.Enemies.Single().DeathObserved==true&&phaseMaps[0].Single.Enemies.Single().DeathObserved==true,"Death with no new damage updates existing cohorts");
        Check(Get<double>(phase,"CurrentDps")==1000,"A death flag never adds fabricated damage");
        Call(phase,"EndArea");Visit(phase,"phase",21);Frame(phase,21,new Monster(1,0,Rarity.Unique,false));
        Check(phaseMaps.Count==1&&phaseMaps[0].Overall.Enemies.Count==1&&phaseMaps[0].Overall.Enemies.Single().DeathObserved==true,"Reentering a map preserves counts without counting its corpse again");

        var uncertain=Tracker();var uncertainMaps=new List<KikiMeterSettings.DamageMeterMap>();Call(uncertain,"BindMaps",uncertainMaps);
        Visit(uncertain,"uncertain",30);Frame(uncertain,30,new Monster(1,1000,null,true),new Monster(2,0,Rarity.Rare,false));
        Visit(uncertain,"uncertain",30.1);Frame(uncertain,30.1,new Monster(1,900,null,true),new Monster(2,0,Rarity.Rare,false));
        Check(uncertainMaps[0].Overall.Enemies.Single().Rarity==null,"Missing rarity stays unclassified, never normal");
        Visit(uncertain,"uncertain",30.2);Frame(uncertain,30.2);
        Check(uncertainMaps[0].Overall.Enemies.Single().DeathObserved==false,"Disappearance is not a death");
        Visit(uncertain,"uncertain",30.3);Frame(uncertain,30.3,new Monster(1,0,Rarity.Rare,false));
        Check(uncertainMaps[0].Overall.Enemies.Single().DeathObserved==false,"A corpse after a reading gap is not a confirmed death");
        Visit(uncertain,"uncertain",30.4);Frame(uncertain,30.4,new Monster(1,1000,Rarity.Magic,true,5001));
        Visit(uncertain,"uncertain",30.5);Frame(uncertain,30.5,new Monster(1,0,Rarity.Magic,false,5001));
        Check(uncertainMaps[0].Overall.Enemies.Count==2&&uncertainMaps[0].Overall.Enemies.Count(e=>e.DeathObserved==true)==1,"Entity-ID reuse with a new address starts a distinct baseline");
        Visit(uncertain,"uncertain",31.2);Frame(uncertain,31.2,new Monster(3,500,Rarity.Normal,true));
        Visit(uncertain,"uncertain",32);Frame(uncertain,32,new Monster(3,0,Rarity.Normal,false));
        Check(uncertainMaps[0].Overall.Enemies.Count==2,"Sampling interruptions do not invent hits or deaths");

        var legacy=JsonConvert.DeserializeObject<KikiMeterSettings.DamageMeterMap>("{\"Hash\":\"legacy\",\"Name\":\"Old\",\"ProcessId\":1,\"Overall\":{\"Damage\":999,\"Seconds\":5,\"Enemies\":[{\"Id\":9,\"Address\":1009,\"AreaHash\":\"legacy\",\"Damage\":999}]}}")!;
        Check(legacy.MonsterCountsVersion==0&&legacy.Overall.Enemies.Single().Rarity==null&&legacy.Overall.Enemies.Single().DeathObserved==null,"Old reports never invent historical categories or deaths");
        var migrated=Tracker();var oldMaps=new List<KikiMeterSettings.DamageMeterMap>{legacy};Call(migrated,"BindMaps",oldMaps);
        Visit(migrated,"legacy",40);Frame(migrated,40,new Monster(9,1000,Rarity.Rare,true));
        Visit(migrated,"legacy",40.1);Frame(migrated,40.1,new Monster(9,900,Rarity.Rare,true));
        Check(legacy.MonsterCountsVersion==1&&legacy.MonsterCountsPartial&&legacy.Overall.Damage==1099,"Resumed reports preserve totals and mark new counters partial");
        Check(legacy.Overall.Enemies.Single().Rarity==Rarity.Rare,"A new confirmed hit can classify an old entry");
        var restored=Tracker();Call(restored,"BindMaps",new List<KikiMeterSettings.DamageMeterMap>{saved});
        Visit(restored,"main",50);Frame(restored,50,new Monster(1,0,Rarity.Normal,false));
        Check(saved.Overall.Enemies.Count(e=>e.DeathObserved==true)==7,"Restarting with persisted counts does not duplicate kills");
        Call(restored,"RestartMeasurement");
        Check(Get<IReadOnlyCollection<KikiMeterSettings.DamageMeterEnemy>>(restored,"CombatEnemies").Count==0&&saved.Overall.Enemies.Count==7,"Live reset leaves saved map counters intact");

        var grouped=Tracker();var groupedMaps=new List<KikiMeterSettings.DamageMeterMap>();Call(grouped,"BindMaps",groupedMaps);
        Visit(grouped,"parent",60);Frame(grouped,60,new Monster(1,1000,Rarity.Normal,true));
        Visit(grouped,"parent",60.1);Frame(grouped,60.1,new Monster(1,0,Rarity.Normal,false));
        Visit(grouped,"child",60.2);Frame(grouped,60.2,new Monster(1,1000,Rarity.Unique,true));
        Visit(grouped,"child",60.3);Frame(grouped,60.3,new Monster(1,0,Rarity.Unique,false));
        Check(groupedMaps.Count==1&&groupedMaps[0].Overall.Enemies.Count==2&&groupedMaps[0].Overall.Enemies.Count(e=>e.DeathObserved==true)==2,"Parent includes subarea deaths, preserving area identity");
        Check(groupedMaps[0].SubAreas.Single().Overall.Enemies.Count(e=>e.DeathObserved==true)==1,"Subarea has its own counter without parent double counting");
        Check((long)groupedMaps[0].Overall.Damage==2000,"Categorization preserves existing DPS accounting");
        Call(grouped,"VisitHub");Frame(grouped,60.4,new Monster(2,1000,Rarity.Normal,true));Frame(grouped,60.5,new Monster(2,0,Rarity.Normal,false));
        Check(groupedMaps[0].Overall.Enemies.Count==2,"Hub sampling cannot append enemies to paused map reports");
        var late=Tracker();var lateMaps=new List<KikiMeterSettings.DamageMeterMap>();Call(late,"BindMaps",lateMaps);
        Visit(late,"late",70);Frame(late,70,new Monster(1,1000,null,true));
        Visit(late,"late",70.1);Frame(late,70.1,new Monster(1,0,null,true));
        Visit(late,"late",70.2);Frame(late,70.2,new Monster(1,0,Rarity.Unique,false));
        Check(lateMaps[0].Overall.Enemies.Single().Rarity==Rarity.Unique&&lateMaps[0].Overall.Enemies.Single().DeathObserved==true,"Rarity discovered on a final sample classifies a death without new damage");
        Check(Get<IReadOnlyCollection<KikiMeterSettings.DamageMeterEnemy>>(late,"CombatEnemies").Single().Rarity==Rarity.Unique,"Live combat receives the final confirmed rarity");
        Visit(late,"late",70.3);Frame(late,70.3,new Monster(1,1000,Rarity.Unique,true));
        Visit(late,"late",70.4);Frame(late,70.4,new Monster(1,0,Rarity.Unique,false));
        Check(lateMaps[0].Overall.Enemies.Count==1&&lateMaps[0].Overall.Enemies.Count(e=>e.DeathObserved==true)==1,"Reappearing same entity counts as one distinct monster");
        Call(late,"MarkMapSaved");
        Visit(late,"late",70.5);Frame(late,70.5,new Monster(2,1000,Rarity.Rare,true));
        Visit(late,"late",70.6);Frame(late,70.6,new Monster(2,0,Rarity.Rare,true));
        Call(late,"MarkMapSaved");
        Call(late,"BeginFrame",70.7);Call(late,"ObserveMonster",2u,new IntPtr(1002),0,0,Rarity.Rare,false);Call(late,"EndFrame");
        Check(Get<bool>(late,"MapDirty"),"Death-only updates trigger persistence even without new damage or elapsed map time");
    }
}
