// Run against a built StashUtility.dll; all dependencies are resolved from the supplied release directory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using StashUtility;
using StashUtility.Models;
using StashUtility.Data;
using ExileCenter.RemoteObjects.Components;
using Newtonsoft.Json;

internal static class TabletRankingCheck
{
    private static int checks;
    private static SixLabors.ImageSharp.PixelFormats.Rgba32[] framePreviewPixels;
    private static int framePreviewWidth, framePreviewHeight;

    private static void CheckWaystoneNavigation(string root)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var allocations=new List<IntPtr>();
        var readerType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Utils.SafeMemoryHandle");
        var reader=Activator.CreateInstance(readerType,flags,null,new object[]{System.Diagnostics.Process.GetCurrentProcess().Id},null);
        var plugin=new StashUtilityCore{DllDirectory=Path.Combine(root,"Plugins","StashUtility")};
        typeof(StashUtilityCore).GetField("handleObj",flags).SetValue(plugin,reader);
        typeof(StashUtilityCore).GetField("readStdWStringMethod",flags).SetValue(plugin,
            readerType.GetMethods(flags).First(m=>m.Name=="ReadStdWString"&&m.GetParameters().Length==1));
        IntPtr Allocate(int size){var p=Marshal.AllocHGlobal(size);allocations.Add(p);Marshal.Copy(new byte[size],0,p,size);return p;}
        void Visible(IntPtr address,bool visible)
        {
            var data=Marshal.PtrToStructure<GameOffsets.Objects.UiElement.UiElementBaseOffset>(address);
            data.Flags=visible?1u<<11:0;Marshal.StructureToPtr(data,address,false);
        }
        IntPtr Node(params IntPtr[] children)
        {
            var address=Allocate(0x600);
            var data=new GameOffsets.Objects.UiElement.UiElementBaseOffset{Self=address,Flags=1u<<11,LocalScaleMultiplier=1,
                UnscaledSize=new GameOffsets.Natives.StdTuple2D<float>(40,40)};
            if(children.Length>0)
            {
                var vector=Allocate(children.Length*IntPtr.Size);
                for(int i=0;i<children.Length;i++)
                {
                    Marshal.WriteIntPtr(vector,i*IntPtr.Size,children[i]);
                    var child=Marshal.PtrToStructure<GameOffsets.Objects.UiElement.UiElementBaseOffset>(children[i]);
                    child.ParentPtr=address;Marshal.StructureToPtr(child,children[i],false);
                }
                data.ChildrensPtr=new GameOffsets.Natives.StdVector{First=vector,Last=vector+children.Length*IntPtr.Size,End=vector+children.Length*IntPtr.Size};
            }
            Marshal.StructureToPtr(data,address,false);return address;
        }
        void Text(IntPtr address,int offset,string value)
        {
            var buffer=Allocate((Math.Max(16,value.Length)+1)*2);
            Marshal.Copy((value+'\0').ToCharArray(),0,buffer,value.Length+1);
            Marshal.StructureToPtr(new GameOffsets.Natives.StdWString{Buffer=buffer,Length=value.Length,Capacity=Math.Max(16,value.Length)},address+offset,false);
        }
        IntPtr Label(string text){var label=Node();Text(label,0x98,"label");Text(label,0x360,text);return label;}
        IntPtr Header(string[] names,IntPtr[] pages,out IntPtr dropdown)
        {
            var buttons=Node(names.Select(name=>Node(Node(Node(),Label(name)))).ToArray());Text(buttons,0x98,"buttons_container");
            var contents=Node(pages);Text(contents,0x98,"pages_container");
            var rows=Node(names.Select(name=>Node(Node(Node(),Label(name)))).ToArray());
            dropdown=Node(Node(),Node(),rows);
            return Node(buttons,contents,Node(),Node(),dropdown);
        }
        object Call(string name,params object[] args)=>typeof(StashUtilityCore).GetMethod(name,flags).Invoke(plugin,args);
        try
        {
            var tiers=new IntPtr[16];var subpages=new IntPtr[16][];
            string[] roman={"I","II","III","IV","V","VI","VII","VIII","IX","X","XI","XII","XIII","XIV","XV","XVI"};
            for(int tier=0;tier<16;tier++)
            {
                subpages[tier]=Enumerable.Range(0,6).Select(_=>Node()).ToArray();
                var header=Header(new[]{"Div - 1","Div - 2","300 - 700","200 - 299","20 - 49","10 - 19"},subpages[tier],out var dropdown);
                Visible(dropdown,false);tiers[tier]=Node(header);
                for(int page=1;page<6;page++)Visible(subpages[tier][page],false);
            }
            var tierHeader=Header(roman,tiers,out var tierDropdown);Visible(tierDropdown,false);
            var waystone=Node(tierHeader);for(int tier=1;tier<16;tier++)Visible(tiers[tier],false);
            var second=Node();Visible(second,false);
            var folderHeader=Header(new[]{"PTP","Fragment"},new[]{waystone,second},out var folderDropdown);
            var folder=Node(Node(Node(),folderHeader));
            var other=Node();Visible(other,false);
            var rootHeader=Header(new[]{"01 - Mapping","03 - Monétaires"},new[]{folder,other},out var rootDropdown);
            Check((IntPtr)Call("FindSelectedStashPage",rootHeader,false)==IntPtr.Zero,"Open root dropdown keeps sorting blocked");
            Check((IntPtr)Call("FindSelectedStashPage",rootHeader,true)==waystone,"Craft discovery resolves the selected waystone inside a folder with both dropdowns open");
            Visible(rootDropdown,false);Visible(folderDropdown,false);
            Check((IntPtr)Call("FindSelectedStashPage",rootHeader,false)==waystone,"Closed dropdowns retain existing selected-page detection");
            Check((bool)Call("CraftWaystoneLayout",waystone),"Waystone detection verifies sixteen tiers and six subpages");
            Visible(waystone,false);
            Check((bool)Call("CraftWaystoneLayout",waystone),"Waystone destination can be discovered before its tab is opened");
            Check(!(bool)Call("CraftWaystoneLayout",other),"A normal stash cannot be selected as the special waystone destination");
            Visible(waystone,true);
            var tabType=typeof(StashUtilityCore).GetNestedType("DetectedStashTab",flags);
            object Tab(string name,IntPtr address){var tab=Activator.CreateInstance(tabType,true);tabType.GetField("Name").SetValue(tab,name);tabType.GetField("Page").SetValue(tab,address);return tab;}
            object Select(IntPtr preferred,params object[] tabs){var array=Array.CreateInstance(tabType,tabs.Length);for(int i=0;i<tabs.Length;i++)array.SetValue(tabs[i],i);return Call("FindCraftWaystoneTab",array,preferred);}
            var source=Tab("Any renamed tab",waystone);
            Check(ReferenceEquals(Select(other,Tab("PTP",other),source),source),"A misleading name never overrides the actual special-tab structure");
            Check(Select(IntPtr.Zero,source,Tab("Another special tab",waystone))==null,"Ambiguous special destinations require an explicit choice");
            typeof(StashUtilityCore).GetField("craftSource",flags).SetValue(plugin,source);
            Call("QueueCraftPages");
            var jobs=((IEnumerable)typeof(StashUtilityCore).GetField("craftPages",flags).GetValue(plugin)).Cast<(int tier,int page)>().ToList();
            Check(jobs.Count==96&&jobs.Distinct().Count()==96&&jobs.All(job=>job.tier>=0&&job.tier<16&&job.page>=0&&job.page<6),"The production queue visits all 96 native tier/page combinations exactly once");
            Check(jobs.First()==(0,0)&&jobs.Last()==(15,5),"Full-stash traversal includes tiers I and XVI and both end pages");
            var discoveryArgs=new object[]{rootHeader,null};
            string savedSettings=JsonConvert.SerializeObject(plugin.Settings);
            Check((bool)typeof(StashUtilityCore).GetMethod("TryDiscoverNamedStashTabs",flags).Invoke(plugin,discoveryArgs),"Craft selection discovers live names without scanning contents or navigating");
            var namedTabs=((IEnumerable)discoveryArgs[1]).Cast<object>().ToList();
            var namedSource=namedTabs.Single(tab=>(IntPtr)tabType.GetField("Page").GetValue(tab)==waystone);
            Check((string)tabType.GetField("Name").GetValue(namedSource)=="PTP"&&
                ((IEnumerable<string>)tabType.GetField("Folders").GetValue(namedSource)).SequenceEqual(new[]{"01 - Mapping"}),"The dropdown retains the exact nested stash path");
            Check(JsonConvert.SerializeObject(plugin.Settings)==savedSettings,"Reading craft choices leaves saved settings unchanged");
            typeof(StashUtilityCore).GetField("craftExplicitDestinations",flags).SetValue(plugin,true);
            typeof(StashUtilityCore).GetField("craftSelectedSource",flags).SetValue(plugin,namedSource);
            for(int tier=0;tier<16;tier++)for(int page=0;page<6;page++)
            {
                typeof(StashUtilityCore).GetField("craftSelectedTier",flags).SetValue(plugin,tier);
                typeof(StashUtilityCore).GetField("craftSelectedPage",flags).SetValue(plugin,page);
                Call("QueueCraftPages");
                var selectedJobs=((IEnumerable)typeof(StashUtilityCore).GetField("craftPages",flags).GetValue(plugin)).Cast<(int tier,int page)>().ToList();
                Check(selectedJobs.SequenceEqual(new[]{(tier,page)}),"Explicit selection crafts exactly one native page: "+tier+"/"+page);
                Check(((string[])Call("CraftSelectionPageNames"))[page]==new[]{"Div - 1","Div - 2","300 - 700","200 - 299","20 - 49","10 - 19"}[page],"Each dropdown label maps to its canonical native page: "+tier+"/"+page);
            }
            var nativeHeader=(IntPtr)Call("ResolvePath",tiers[0],new[]{0});
            var nativeHeaderData=Marshal.PtrToStructure<GameOffsets.Objects.UiElement.UiElementBaseOffset>(nativeHeader);
            var nativeDropdown=Marshal.ReadIntPtr(nativeHeaderData.ChildrensPtr.First,4*IntPtr.Size);
            Marshal.WriteIntPtr(nativeHeaderData.ChildrensPtr.First,4*IntPtr.Size,Node());
            typeof(StashUtilityCore).GetField("craftSelectedTier",flags).SetValue(plugin,0);
            Visible(tiers[0],false);
            Check(((string[])Call("CraftSelectionPageNames")).SequenceEqual(new[]{"Div - 1","Div - 2","300 - 700","200 - 299","20 - 49","10 - 19"}),"Inactive native waystone tabs expose their actual names without a dropdown list");
            Visible(tiers[0],true);
            var nativeButtons=(IntPtr)Call("ResolvePath",nativeHeader,new[]{0});
            var nativeButtonsData=Marshal.PtrToStructure<GameOffsets.Objects.UiElement.UiElementBaseOffset>(nativeButtons);
            var savedButtons=Enumerable.Range(0,6).Select(i=>Marshal.ReadIntPtr(nativeButtonsData.ChildrensPtr.First,i*IntPtr.Size)).ToArray();
            for(int i=0;i<6;i++)Marshal.WriteIntPtr(nativeButtonsData.ChildrensPtr.First,i*IntPtr.Size,savedButtons[5-i]);
            Check(((string[])Call("CraftSelectionPageNames")).SequenceEqual(new[]{"10 - 19","20 - 49","200 - 299","300 - 700","Div - 2","Div - 1"}),"Reordered native buttons retain their actual labels instead of generated numbers");
            typeof(StashUtilityCore).GetField("craftTier",flags).SetValue(plugin,0);
            typeof(StashUtilityCore).GetField("craftPage",flags).SetValue(plugin,5);
            typeof(StashUtilityCore).GetField("craftSelectedPageName",flags).SetValue(plugin,"Div - 1");
            Check(!(bool)Call("CraftSelectWaystonePage",false),"A named native page needs confirmation before any craft");
            typeof(StashUtilityCore).GetField("craftNamedPageClicked",flags).SetValue(plugin,true);
            for(int i=0;i<6;i++)Visible(subpages[0][i],i==2);
            Check(!(bool)Call("CraftSelectWaystonePage",true),"The alternate-page probe never confirms a craft target");
            typeof(StashUtilityCore).GetField("craftNamedPageClicked",flags).SetValue(plugin,true);
            Check(!(bool)Call("CraftSelectWaystonePage",true),"An unchanged page after the target click is not an acknowledgement");
            for(int i=0;i<6;i++)Visible(subpages[0][i],i==1);
            Check((bool)Call("CraftSelectWaystonePage",true)&&
                (int)typeof(StashUtilityCore).GetField("craftPage",flags).GetValue(plugin)==1,"The acknowledged named button determines the native page independently of button order; no input executed");
            Check((bool)Call("CraftSelectWaystonePage",false),"The confirmed named page remains valid without another click");
            for(int i=0;i<6;i++)Visible(subpages[0][i],i==3);
            Check(!(bool)Call("CraftSelectWaystonePage",false),"A different visible page cannot replace the confirmed named target");
            for(int i=0;i<6;i++){Marshal.WriteIntPtr(nativeButtonsData.ChildrensPtr.First,i*IntPtr.Size,savedButtons[i]);Visible(subpages[0][i],i==0);}
            var directNames=new[]{"Div - 1","Div - 2","300 - 700","200 - 299","20 - 49","10 - 19"};
            for(int i=0;i<6;i++)Marshal.WriteIntPtr(nativeButtonsData.ChildrensPtr.First,i*IntPtr.Size,Node(Label(directNames[i])));
            Check(((string[])Call("CraftSelectionPageNames")).SequenceEqual(directNames),"Direct native labels are read without requiring a generic stash-dropdown layout");
            for(int i=0;i<6;i++)Marshal.WriteIntPtr(nativeButtonsData.ChildrensPtr.First,i*IntPtr.Size,savedButtons[i]);
            Marshal.WriteIntPtr(nativeHeaderData.ChildrensPtr.First,4*IntPtr.Size,nativeDropdown);
            typeof(StashUtilityCore).GetField("craftNamedPageResolved",flags).SetValue(plugin,false);
            typeof(StashUtilityCore).GetField("craftSelectedSource",flags).SetValue(plugin,null);
            Check(((string[])Call("CraftSelectionPageNames")).Length==0,"Unreadable sub-tab names never turn into invented numbered choices");
            typeof(StashUtilityCore).GetField("craftSelectedSource",flags).SetValue(plugin,namedSource);
            foreach(var invalidChoice in new[]{(-1,0),(16,0),(0,-1),(0,6)})
            {
                typeof(StashUtilityCore).GetField("craftSelectedTier",flags).SetValue(plugin,invalidChoice.Item1);
                typeof(StashUtilityCore).GetField("craftSelectedPage",flags).SetValue(plugin,invalidChoice.Item2);
                bool rejected=false;
                try{Call("QueueCraftPages");}catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){rejected=true;}
                Check(rejected,"Incomplete or invalid choices cannot queue a craft");
            }
            typeof(StashUtilityCore).GetField("craftExplicitDestinations",flags).SetValue(plugin,false);
            for(int tier=0;tier<16;tier++)for(int page=0;page<6;page++)
            {
                foreach(var t in tiers)Visible(t,false);Visible(tiers[tier],true);
                foreach(var p in subpages[tier])Visible(p,false);Visible(subpages[tier][page],true);
                typeof(StashUtilityCore).GetField("craftTier",flags).SetValue(plugin,tier);
                typeof(StashUtilityCore).GetField("craftPage",flags).SetValue(plugin,page);
                Check((bool)Call("CraftSelectWaystonePage",false),"Exact selected native tier/page confirmed without input: "+tier+"/"+page);
                typeof(StashUtilityCore).GetField("craftPage",flags).SetValue(plugin,(page+1)%6);
                Check(!(bool)Call("CraftSelectWaystonePage",false),"A different visible page cannot receive the planned craft: "+tier+"/"+page);
            }
            typeof(StashUtilityCore).GetField("craftTier",flags).SetValue(plugin,15);
            typeof(StashUtilityCore).GetField("craftPage",flags).SetValue(plugin,5);
            Visible(subpages[15][0],true);
            Check(!(bool)Call("CraftSelectWaystonePage",false),"Two visible native pages stop crafting instead of choosing an ambiguous target");
            typeof(StashUtilityCore).GetField("craftTier",flags).SetValue(plugin,16);
            bool invalid=false;
            try{Call("CraftSelectWaystonePage",false);}catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){invalid=true;}
            Check(invalid,"An out-of-range tier cannot become a navigation target");
            var buttonsAddress=(IntPtr)Call("ResolvePath",tierHeader,new[]{0});
            var buttonsData=Marshal.PtrToStructure<GameOffsets.Objects.UiElement.UiElementBaseOffset>(buttonsAddress);
            var originalButtons=Enumerable.Range(0,16).Select(i=>Marshal.ReadIntPtr(buttonsData.ChildrensPtr.First,i*IntPtr.Size)).ToArray();
            for(int i=0;i<16;i++)Check((IntPtr)Call("CraftPageButton",tierHeader,i,16)==originalButtons[i],"Tier labels resolve the exact native button: "+roman[i]);
            var grouped=Node(Node(originalButtons.Take(8).Reverse().ToArray()),Node(originalButtons.Skip(8).Reverse().ToArray()));
            Text(grouped,0x98,"buttons_container");
            var groupedHeader=Node(grouped,Node(tiers),Node(),Node(),Node());
            for(int i=0;i<16;i++)Check((IntPtr)Call("CraftPageButton",groupedHeader,i,16)==originalButtons[i],"Two rows with reordered button children still resolve the intended Roman tier: "+roman[i]);
            for(int i=0;i<16;i++)
            {
                var label=(IntPtr)Call("ResolvePath",originalButtons[i],new[]{0,1});
                Text(label,0x98,"waystone_tier_label");
                Check((IntPtr)Call("CraftPageButton",groupedHeader,i,16)==originalButtons[i],"A specialized native label ID still resolves the exact Roman tier: "+roman[i]);
            }
            Console.WriteLine("VALIDATION waystone navigation: real native-memory fixture; nested open dropdowns, renamed special tabs and all 96 tier/page confirmations; no game input executed.");
        }
        finally{foreach(var allocation in allocations)Marshal.FreeHGlobal(allocation);((IDisposable)reader).Dispose();}
    }

    private static void CheckWaystoneCraft(string root)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic;
        var plugin=new StashUtilityCore{DllDirectory=Path.Combine(root,"Plugins","StashUtility")};
        var planner=typeof(StashUtilityCore).GetMethod("NextWaystoneCurrency",flags);
        string Text(ExileCenter.RemoteEnums.Rarity rarity,string flag="",bool french=false) => french ?
            "Classe d'objet : Pierres de téléportation\r\nRareté : "+(rarity==ExileCenter.RemoteEnums.Rarity.Magic?"Magique":rarity.ToString())+
                "\r\nPierre de téléportation (Palier 15)\r\n--------\r\nNiveau de l'objet : 83\r\n"+flag :
            "Item Class: Waystones\r\nRarity: "+rarity+"\r\nWaystone (Tier 15)\r\n--------\r\nItem Level: 83\r\n"+flag;
        string Next(ExileCenter.RemoteEnums.Rarity rarity,int count,string flag="",bool corrupt=true,bool french=false,string path="Metadata/Items/Maps/MapKeyTier15") =>
            (string)planner.Invoke(plugin,new object[]{path,rarity,count,Text(rarity,flag,french),french?"Pierre de téléportation (Palier 15)":"Waystone (Tier 15)",corrupt});
        foreach(bool french in new[]{false,true})foreach(bool corrupt in new[]{false,true})
        {
            Check(Next(ExileCenter.RemoteEnums.Rarity.Normal,0,corrupt:corrupt,french:french)=="CurrencyUpgradeToRare","White waystone uses alchemy "+french+corrupt);
            foreach(int count in new[]{1,2})Check(Next(ExileCenter.RemoteEnums.Rarity.Magic,count,corrupt:corrupt,french:french)=="CurrencyUpgradeToRare","Magic waystone uses alchemy "+count+french+corrupt);
            for(int count=0;count<=6;count++)Check(Next(ExileCenter.RemoteEnums.Rarity.Rare,count,corrupt:corrupt,french:french)==
                (count<6?"CurrencyAddModToRare":corrupt?"CurrencyCorrupt":null),"Rare waystone fills six explicit modifiers before optional Vaal "+count+french+corrupt);
        }
        foreach(string flag in new[]{"Corrupted","Corrompu","Corrompue","Unidentified","Non identifié","Non identifiée","Mirrored","Dupliqué","Dupliquée","Sanctified","Sanctifié","Sanctifiée"})
            foreach(int count in new[]{2,6,8})Check(Next(ExileCenter.RemoteEnums.Rarity.Rare,count,flag)==null,"Read-only waystone never consumes currency: "+flag+count);
        Check(Next(ExileCenter.RemoteEnums.Rarity.Unique,6)==null,"Unique waystones are excluded");
        foreach(string path in new[]{"Metadata/Items/TowerAugment/GenericAugment","Metadata/Items/Currency/CurrencyCorrupt","Metadata/Items/Maps/NotAWaystone",null})
            Check(Next(ExileCenter.RemoteEnums.Rarity.Normal,0,path:path)==null,"Only waystones can be crafted: "+path);
        void Reject(int count,string text)
        {
            bool rejected=false;
            try{planner.Invoke(plugin,new object[]{"Metadata/Items/Maps/MapKeyTier15",ExileCenter.RemoteEnums.Rarity.Rare,count,text,"Waystone (Tier 15)",true});}
            catch(TargetInvocationException error)when(error.InnerException is InvalidOperationException){rejected=true;}
            Check(rejected,"Unverified or inconsistent state sends no currency");
        }
        Reject(7,Text(ExileCenter.RemoteEnums.Rarity.Rare));Reject(-1,Text(ExileCenter.RemoteEnums.Rarity.Rare));
        foreach(string text in new[]{"",null,"unrelated clipboard",Text(ExileCenter.RemoteEnums.Rarity.Magic),Text(ExileCenter.RemoteEnums.Rarity.Rare).Replace("Waystones","Tablets"),
            Text(ExileCenter.RemoteEnums.Rarity.Rare).Replace("Tier 15","Tier 14")})Reject(4,text);
        var tabType=typeof(StashUtilityCore).GetNestedType("DetectedStashTab",BindingFlags.NonPublic);
        object Tab(string name,params string[] ids)
        {
            var tab=Activator.CreateInstance(tabType,true);tabType.GetField("Name").SetValue(tab,name);
            var currencies=(HashSet<string>)tabType.GetField("CurrencyIds").GetValue(tab);foreach(string id in ids)currencies.Add(id);return tab;
        }
        var selector=typeof(StashUtilityCore).GetMethod("FindCraftCurrencyTab",flags);
        object Select(params object[] tabs){var values=Array.CreateInstance(tabType,tabs.Length);for(int i=0;i<tabs.Length;i++)values.SetValue(tabs[i],i);return selector.Invoke(null,new object[]{values});}
        foreach(string name in new[]{"PTP","Monnaies","02 - Réserve","Tout autre nom"})
        {
            var money=Tab(name,"CurrencyUpgradeToRare","CurrencyAddModToRare","CurrencyCorrupt");
            Check(ReferenceEquals(Select(Tab("Pierres"),money),money),"Currency detection is independent of renamed stash tabs: "+name);
            Check(Select(money,Tab("Autre source","CurrencyUpgradeToRare","CurrencyAddModToRare","CurrencyCorrupt"))==null,"Ambiguous currency sources are rejected");
        }
        Check(Select(Tab("Monnaies","CurrencyUpgradeToRare","CurrencyAddModToRare"))==null,"The tab name cannot replace missing currency evidence");
        var acknowledge=typeof(StashUtilityCore).GetMethod("CraftCurrencyAcknowledged",flags);
        bool Ack(string currency,int before,int after,int beforeCurrency=20,int afterCurrency=19,bool changed=true,ExileCenter.RemoteEnums.Rarity rarity=ExileCenter.RemoteEnums.Rarity.Rare) =>
            (bool)acknowledge.Invoke(null,new object[]{currency,rarity,before,ExileCenter.RemoteEnums.Rarity.Rare,after,beforeCurrency,afterCurrency,changed});
        Check(Ack("CurrencyUpgradeToRare",0,4,rarity:ExileCenter.RemoteEnums.Rarity.Normal),"Alchemy requires the confirmed four-mod rare result");
        Check(!Ack("CurrencyUpgradeToRare",1,3,rarity:ExileCenter.RemoteEnums.Rarity.Magic),"An unexpected alchemy result cannot advance the craft");
        Check(Ack("CurrencyAddModToRare",5,6)&&Ack("CurrencyAddModToRare",4,6),"Exaltation confirms one or multiple added mods within six slots");
        Check(!Ack("CurrencyAddModToRare",6,7)&&!Ack("CurrencyAddModToRare",5,5)&&!Ack("CurrencyAddModToRare",5,6,changed:false),"A full or unchanged waystone cannot acknowledge exaltation");
        Check(Ack("CurrencyCorrupt",6,6,changed:false),"Vaal can leave native mods unchanged; fresh corrupted item text is still required");
        Check(!Ack("CurrencyCorrupt",5,6)&&!Ack("CurrencyCorrupt",7,8)&&!Ack("CurrencyChaos",4,5),"Only the planned currency at the expected stage can acknowledge");
        foreach(string currency in new[]{"CurrencyUpgradeToRare","CurrencyAddModToRare","CurrencyCorrupt"})
            foreach(int afterCount in new[]{-1,18,20,21})Check(!Ack(currency,4,6,afterCurrency:afterCount),"No repeat or advance without exactly one confirmed currency consumption: "+currency+afterCount);
        var corruption=typeof(StashUtilityCore).GetMethod("CraftTextIsCorrupted",flags);
        foreach(string text in new[]{"Corrupted","Corrompu","Corrompue"})Check((bool)corruption.Invoke(null,new object[]{Text(ExileCenter.RemoteEnums.Rarity.Rare,text)}),"Corruption acknowledgement accepts a standalone localized status: "+text);
        foreach(string text in new[]{"", "Unidentified", "10% chance to find corrupted items"})Check(!(bool)corruption.Invoke(null,new object[]{Text(ExileCenter.RemoteEnums.Rarity.Rare,text)}),"Item modifiers or unreadable flags cannot acknowledge corruption");
        var defaults=JsonConvert.DeserializeObject<StashUtilitySettings>("{}");
        Check(defaults.WaystoneCraftCorrupt&&defaults.WaystoneCraftCurrencyLimit==200,"Existing settings receive bounded craft defaults without migration");
        var delta=typeof(StashUtilityCore).GetMethod("CraftLocalCurrencyDelta",flags);
        bool Delta(int before,int after,bool same=true,bool identity=true)=>(bool)delta.Invoke(null,new object[]{before,after,same,identity});
        foreach(int before in new[]{1,2,20,100})
        {
            Check(Delta(before,before),"A currency stack can remain unchanged while awaiting confirmation");
            Check(!Delta(before,before,identity:false),"An unrelated change to the selected currency is rejected");
            if(before>1)Check(Delta(before,before-1,identity:false),"Exactly one local orb may be consumed");
            foreach(int after in new[]{-1,before+1,before-2})Check(!Delta(before,after),"Invalid local currency delta stops the craft");
            Check(!Delta(before,before-1,same:false),"Replacing local currency with another item is rejected");
        }
        var cellType=typeof(StashUtilityCore).GetNestedType("SortItem",BindingFlags.NonPublic);
        object Cell(int address,int x,string identity)
        {
            var cell=Activator.CreateInstance(cellType,true);
            foreach(var pair in new Dictionary<string,object>{{"Address",new IntPtr(address)},{"X",x},{"Y",0},{"Width",1},{"Height",1},{"Identity",identity}})
                cellType.GetField(pair.Key).SetValue(cell,pair.Value);
            return cell;
        }
        var listType=typeof(List<>).MakeGenericType(cellType);
        IList Cells(params object[] cells){var list=(IList)Activator.CreateInstance(listType);foreach(var cell in cells)list.Add(cell);return list;}
        var baseline=(IList)typeof(StashUtilityCore).GetField("craftInventoryBefore",flags).GetValue(plugin);
        var invariant=typeof(StashUtilityCore).GetMethod("CraftInventoryUnchanged",flags);
        bool Unchanged(IList current,int except=0)=>(bool)invariant.Invoke(plugin,new object[]{current,new IntPtr(except)});
        var other=Cell(1,0,"other");var coin=Cell(2,1,"one exalt");var target=Cell(3,2,"waystone");
        baseline.Add(other);baseline.Add(coin);baseline.Add(target);
        Check(Unchanged(Cells(other,coin,target)),"Inventory targets start in the unchanged baseline");
        baseline.Remove(target);
        Check(Unchanged(Cells(other,coin,Cell(4,2,"crafted waystone")),4),"Only the active inventory waystone may change");
        Check(!Unchanged(Cells(Cell(1,0,"changed other"),coin,target),3),"Unrelated inventory changes stop the craft");
        Check(!Unchanged(Cells(other,Cell(2,3,"one exalt"),target),3),"Currency movement cannot be mistaken for consumption");
        typeof(StashUtilityCore).GetField("craftLocalCurrency",flags).SetValue(plugin,true);
        typeof(StashUtilityCore).GetField("craftCurrencyPending",flags).SetValue(plugin,true);
        typeof(StashUtilityCore).GetField("craftCurrencyCell",flags).SetValue(plugin,coin);
        typeof(StashUtilityCore).GetField("craftCurrencyCount",flags).SetValue(plugin,1);
        Check(Unchanged(Cells(other,target),3),"The last local orb may disappear pending acknowledgement");
        Check(!Unchanged(Cells(Cell(1,0,"changed other"),target),3),"Last-orb consumption still protects unrelated inventory items");
        typeof(StashUtilityCore).GetField("craftCurrencyCount",flags).SetValue(plugin,2);
        Check(!Unchanged(Cells(other,target),3),"A nonempty local currency stack may not disappear");
        baseline.Clear();
        var scanFlag=typeof(StashUtilityCore).GetField("stashScanForCraft",flags);
        var scanComplete=typeof(StashUtilityCore).GetField("craftScanCompleted",flags);
        var finishScan=typeof(StashUtilityCore).GetMethod("CompleteStashScan",flags);
        plugin.Settings.TransferDestinations["Equipment"]=new StashDestination{TabName="Keep my destination",Disabled=true};
        plugin.Settings.ScannedStashTabs.Add(new StashDestination{TabName="PTP",FolderPath=new List<string>{"01 - Mapping"}});
        string settingsBefore=JsonConvert.SerializeObject(plugin.Settings);
        scanFlag.SetValue(plugin,true);
        finishScan.Invoke(plugin,null);
        Check((bool)scanComplete.GetValue(plugin)&&!(bool)scanFlag.GetValue(plugin),"Automatic craft scanning completes without starting another scan");
        Check(JsonConvert.SerializeObject(plugin.Settings)==settingsBefore,"Automatic craft detection never changes saved routes, scan history or settings");
        var queue=typeof(StashUtilityCore).GetField("stashScanQueue",flags).GetValue(plugin);
        var enqueue=queue.GetType().GetMethod("Enqueue");
        var queuedType=queue.GetType().GetGenericArguments()[0];
        void EnqueueScan()=>enqueue.Invoke(queue,new[]{Activator.CreateInstance(queuedType,Tab("PTP"),new IntPtr(123),0)});
        int Queued()=>(int)queue.GetType().GetProperty("Count").GetValue(queue);
        var stopCraft=typeof(StashUtilityCore).GetMethod("StopWaystoneCraft",flags);
        EnqueueScan();scanFlag.SetValue(plugin,true);
        stopCraft.Invoke(plugin,new object[]{null});
        Check(Queued()==0&&!(bool)scanFlag.GetValue(plugin),"Stopping craft cancels its pending automatic scan without game input");
        EnqueueScan();
        stopCraft.Invoke(plugin,new object[]{null});
        Check(Queued()==1,"Stopping an idle craft leaves a separate manual stash scan alone");
        scanFlag.SetValue(plugin,true);scanComplete.SetValue(plugin,false);
        typeof(StashUtilityCore).GetMethod("CancelStashScan",flags).Invoke(plugin,new object[]{"fixture interruption"});
        Check(Queued()==0&&!(bool)scanComplete.GetValue(plugin),"An interrupted automatic scan cannot be mistaken for a completed scan");
        Check(JsonConvert.SerializeObject(plugin.Settings)==settingsBefore,"Scan cancellation leaves all saved settings unchanged");
        Console.WriteLine("VALIDATION waystone crafting: rarity, counts, French/English item text, exclusions and renamed currency sources passed; no native input executed.");
    }

    private static void CheckCompactCompositions()
    {
        int start = checks;
        var generated = TabletComposition.Generate().ToArray();
        Check(generated.Length == 210 && generated.Select(c => c.Label).Distinct().Count() == 210, "Exactly 210 theoretical four-grade multisets, not a physically attainable pool");
        foreach (var combination in generated)
        {
            Check(combination.Ranks.Count == 4 && combination.Counts.Values.Sum() == 4, "Every theoretical composition describes four real slots");
            Check(combination.Label.Length <= 4 && !combination.Label.Contains('1'), "Compact label has at most four characters and never writes a single occurrence count");
            Check(TabletComposition.Format(combination.Ranks.Reverse().ToArray()).Label == combination.Label, "Composition is independent of modifier order");
            int previous = -1, total = 0;
            for(int i=0;i<combination.Label.Length;i++)
            {
                int rank = "SABCDEF".IndexOf(combination.Label[i]);
                Check(rank > previous, "Each letter appears once, in canonical S-A-B-C-D-E-F order");
                previous = rank;
                total += i+1<combination.Label.Length && char.IsAsciiDigit(combination.Label[i+1]) ? combination.Label[++i]-'0' : 1;
            }
            Check(total == 4, "Independent compact-label parser reconstructs exactly four occurrences");
        }
        var examples = new Dictionary<string,string> {
            ["SSSS"]="S4",["SSSA"]="S3A",["SSSB"]="S3B",["SSAA"]="S2A2",["SSAB"]="S2AB",["SSBB"]="S2B2",["SSCD"]="S2CD",
            ["SAAA"]="SA3",["SAAB"]="SA2B",["SABB"]="SAB2",["SABC"]="SABC",["SABF"]="SABF",["SBBB"]="SB3",["SDDD"]="SD3",
            ["SFFF"]="SF3",["SSSD"]="S3D",["AABB"]="A2B2",["AAFF"]="A2F2",["AABC"]="A2BC",["ABCD"]="ABCD",["ACEF"]="ACEF",
            ["BBCC"]="B2C2",["BCCC"]="BC3",["CCCC"]="C4",["CDEF"]="CDEF",["DDDD"]="D4",["EEFF"]="E2F2",["FFFF"]="F4" };
        foreach(var example in examples)
            Check(TabletComposition.Format(example.Key.Select(c=>c.ToString()).ToArray()).Label == example.Value, "Required example " + example.Key + " -> " + example.Value);
        var letters = "SABCDEF".Select(c=>c.ToString()).ToArray();
        var allLabels = new HashSet<string>();
        foreach(string a in letters)foreach(string b in letters)foreach(string c in letters)foreach(string d in letters)
        {
            var input = new[]{a,b,c,d};
            var result = TabletComposition.Format(input);
            Check(result.Label == TabletComposition.Format(input.OrderBy(r=>Array.IndexOf(letters,r)).ToArray()).Label, "All 2401 input sequences format canonically");
            allLabels.Add(result.Label);
        }
        Check(allLabels.SetEquals(generated.Select(c=>c.Label)), "All 2401 ordered inputs collapse to exactly the generated 210 compositions");
        Check(TabletComposition.Generate(new[]{"S","A","B","C","D","F"}).Count()==126, "Generator respects a six-grade schema without inventing E");
        Check(TabletComposition.Generate(new[]{"F"}).Single().Label=="F4", "Single-grade schema");
        Check(!TabletComposition.Generate(Array.Empty<string>()).Any()&&!TabletComposition.Generate(new[]{"S","S"}).Any(), "Invalid or empty schemas cannot invent combinations");
        foreach(var invalid in new string[][] {null,Array.Empty<string>(),new[]{"S"},new[]{"S","A","B"},new[]{"S","A","B","C","D"},new[]{"S","A","B","Z"},new[]{"S","A",null,"F"},new[]{"S","A","B",""}})
            Check(TabletComposition.Format(invalid)==null, "Missing, excessive or invalid grades never get padded or guessed");
        var catalog=ModDatabase.TabletMeta;
        var engine=new TabletRankingEngine(catalog);
        foreach(string type in catalog.Types.Values)
        {
            var four=catalog.ForType(type).Where(m=>m.Affix=="prefix").Take(2).Concat(catalog.ForType(type).Where(m=>m.Affix=="suffix").Take(2)).ToArray();
            var observation=new TabletObservation{TabletType=type,Mods=four.Select(m=>new TabletDetectedMod{Modifier=m,RawId=m.GameModIds[0],Roll=10000}).ToList()};
            var priorities=four.Select((m,i)=>(m.Id,Grade:i<2?"S":"A")).ToDictionary(p=>p.Id,p=>new TabletPriority(p.Grade));
            var quality=engine.Evaluate(observation,priorities);
            Check(quality.Composition.Label=="S2A2"&&quality.PriorityCount==4&&quality.BestCount==2, "Composition and priority counts are separate: "+type);
            Check(quality.BadgeLabel=="S2A2"&&quality.GlobalPosition==null&&quality.RankingValidation!=TabletRankingValidation.Verified,"Composition alone while global numbering is unverified: "+type);
            priorities[four[0].Id]=new("F");
            var changed=engine.Evaluate(observation,priorities);
            Check(changed.Composition.Label=="SA2F"&&changed.OrderKey!=quality.OrderKey,"Preference edit updates composition and quality immediately: "+type);
            priorities.Clear();
            var reference=engine.Evaluate(observation,priorities);
            Check(reference.Composition.Label==TabletComposition.Format(four.Select(m=>m.Rank).ToArray()).Label&&reference.PriorityCount==0&&reference.Rank=="", "Unselected reference grades describe all four mods without creating priorities: "+type);
            priorities[four[0].Id]=new("F",10001);
            var below=engine.Evaluate(observation,priorities);
            Check(below.Composition.Label==reference.Composition.Label&&below.PriorityCount==0,"Below-minimum modifier keeps secondary reference quality only: "+type);
            for(int size=0;size<4;size++)
            {
                var partial=new TabletObservation{TabletType=type,Mods=observation.Mods.Take(size).ToList()};
                Check(engine.Evaluate(partial,priorities).Composition==null&&engine.Evaluate(partial,priorities).BadgeLabel=="N/C","Incomplete tablets never invent a four-affix composition: "+size+type);
            }
            var duplicated=new TabletObservation{TabletType=type,Mods=Enumerable.Repeat(observation.Mods[0],4).ToList()};
            Check(engine.Evaluate(duplicated,priorities).Composition==null,"One physical modifier cannot become four S modifiers: "+type);
            var unknown=new TabletObservation{TabletType=type,Mods=observation.Mods.Take(3).Concat(new[]{new TabletDetectedMod{RawId="unknown"}}).ToList()};
            Check(engine.Evaluate(unknown,priorities).BadgeLabel=="N/C","Unknown fourth modifier is explicit N/C: "+type);
        }
        var toy=new TabletMetaCatalog{RankOrder=letters.ToList()};
        for(int i=0;i<8;i++)toy.Modifiers.Add(new(){Id="mod:"+i,TabletType="toy",Rank=i<4?"S":"A",RankLabel=(i<4?"S":"A")+(i%4+1),SubRank=i%4+1,Affix=i%2==0?"prefix":"suffix",Groups=new(){"group"+i}});
        var model=new TabletRankingEngine(toy);
        TabletObservation Obs(params int[] indices)=>new(){TabletType="toy",Mods=indices.Select(i=>new TabletDetectedMod{Modifier=toy.Modifiers[i]}).ToList()};
        var selected=toy.Modifiers.ToDictionary(m=>m.Id,m=>new TabletPriority(m.Rank));
        var better=model.Evaluate(Obs(0,1,4,5),selected);var worse=model.Evaluate(Obs(2,3,6,7),selected);
        Check(better.Composition.Label==worse.Composition.Label&&string.CompareOrdinal(better.OrderKey,worse.OrderKey)<0,"Identical S2A2 compositions retain individual-subrank quality differences");
        selected=toy.Modifiers.Take(2).ToDictionary(m=>m.Id,_=>new TabletPriority("S"));
        var referenceS=model.Evaluate(Obs(0,1,2,3),selected);
        Check(referenceS.Composition.Label=="S4"&&referenceS.BestCount==2&&referenceS.PriorityCount==2&&referenceS.MaximumBestCountUpperBound==2,"Reference S letters do not become extra selected S priorities");
        Check((better with{GlobalPosition=1}).BadgeLabel=="S2A2","An ordinal without verification is never displayed");
        Check((better with{GlobalPosition=1,RankingValidation=TabletRankingValidation.Verified}).BadgeLabel=="S1 · S2A2"&&
            (better with{GlobalPosition=2,RankingValidation=TabletRankingValidation.Verified}).BadgeLabel=="S2 · S2A2","Conditional ordinal display stays distinct from composition; no production ordinal is enabled");
        var conflicting=JsonConvert.DeserializeObject<TabletMetaCatalog>(JsonConvert.SerializeObject(toy));
        conflicting.Modifiers[1].Groups=new(conflicting.Modifiers[0].Groups);
        var conflictObservation=new TabletObservation{TabletType="toy",Mods=conflicting.Modifiers.Take(4).Select(m=>new TabletDetectedMod{Modifier=m}).ToList()};
        var conflict=new TabletRankingEngine(conflicting).Evaluate(conflictObservation,selected);
        Check(conflict.Composition.Label=="S4"&&conflict.GlobalPosition==null&&conflict.RankingValidation==TabletRankingValidation.IncompatibleObservation&&conflict.Uncertain,"Known group conflict keeps descriptive composition but cannot receive a fictitious ordinal");
        Check(conflict.MaximumBestCountUpperBound==1,"Two mutually exclusive selected S modifiers never imply two compatible priorities");
        var minimumObservation=Obs(0,1,4,5);
        var minimums=new Dictionary<string,TabletPriority>{{toy.Modifiers[0].Id,new("F",10)}};
        Check(model.Evaluate(minimumObservation,minimums).Composition==null,"An unknown minimum roll cannot invent the effective custom grade");
        minimumObservation.Mods[0].Roll=9;
        Check(model.Evaluate(minimumObservation,minimums).Composition.Label=="S2A2"&&model.Evaluate(minimumObservation,minimums).PriorityCount==0,"A confirmed failed minimum uses reference quality without a priority match");
        minimumObservation.Mods[0].Roll=10;
        Check(model.Evaluate(minimumObservation,minimums).Composition.Label=="SA2F"&&model.Evaluate(minimumObservation,minimums).Rank=="F","Crossing a minimum updates composition and the independent global grade");
        minimumObservation.Mods.Add(new(){Modifier=toy.Modifiers[6]});
        Check(model.Evaluate(minimumObservation,minimums).Composition==null,"Five modifiers cannot receive a four-mod composition");
        var warm=Obs(0,1,4,5);var cached=model.Evaluate(warm,selected);
        var timer=System.Diagnostics.Stopwatch.StartNew();bool reused=true;
        for(int i=0;i<10000;i++)reused&=ReferenceEquals(model.Evaluate(warm,selected),cached);
        timer.Stop();
        Check(reused&&model.CachedResults<=TabletRankingEngine.CacheLimit&&timer.Elapsed<TimeSpan.FromSeconds(10),"Ten thousand unchanged observations reuse the immutable computed composition within a bounded cache");
        Console.WriteLine($"VALIDATION composition cache: 10000 unchanged observations in {timer.ElapsedMilliseconds} ms; immutable result reused.");
        Console.WriteLine($"VALIDATION composition: 210 theoretical multisets; 2401 input orders; 28 mandatory examples; 8 types; priorities, dynamic edits, partial/unknown inputs and independent sorting: {checks-start} checks passed.");
    }

    private static void CheckDynamicQuality()
    {
        var catalog = ModDatabase.TabletMeta;
        Check(catalog.SourceFile == "POE2_055_Tablettes_Pour_Codex_COMPLET.json", "Primary source is the supplied JSON");
        Check(catalog.Modifiers.Count == 254 && catalog.Types.Count == 8, "254 entries and eight types");
        Check("SABCDEF".Select(g => catalog.Modifiers.Count(m => m.Rank == g.ToString())).SequenceEqual(new[] {15,27,44,47,67,46,8}), "Exact S-F distribution, including all 46 E and eight F");
        Check(catalog.Modifiers.All(m => m.GameModIds.Count > 0 && m.Groups.Count > 0), "All native mappings and exclusion groups resolved");
        var engine = new TabletRankingEngine(catalog);
        var runtimeMatch = typeof(StashUtilityCore).GetMethod("MatchesTabletSelection", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string rank in catalog.RankOrder)
        {
            int before = checks;
            foreach (string type in catalog.Types.Values)
            {
                var available = catalog.ForType(type).ToArray();
                var four = available.Where(m => m.Affix == "prefix").Take(2)
                    .Concat(available.Where(m => m.Affix == "suffix").Take(2)).ToArray();
                for (int count = 0; count <= 4; count++)
                {
                    var priorities = four.Take(count).ToDictionary(m => m.Id, _ => new TabletPriority(rank));
                    for (int size = 0; size <= 4; size++)
                    {
                        var observation = new TabletObservation { TabletType = type, Mods = four.Take(size)
                            .Select(m => new TabletDetectedMod { Modifier = m, RawId = m.GameModIds[0], Roll = 10000 }).ToList() };
                        var result = engine.Evaluate(observation, priorities);
                        int expected = Math.Min(count, size);
                        Check(result.PriorityBadgeLabel == (expected == 0 ? "N/C" : rank + (expected == 1 ? "" : expected.ToString())),
                            $"{type} {rank} selected={count} affixes={size}: badge counts only matching selected mods");
                        Check(result.PriorityCount == expected && result.BestCount == expected, $"{type} {rank} selected={count} affixes={size}: custom counts");
                        Check(result.Rank == (expected == 0 ? "" : rank), $"{type} {rank}: no reference promotion");
                        Check(expected == 0 ? result.Label == "N/C" : result.Label.Contains('p') || result.Uncertain,
                            $"{type} {rank}: no invented ordinal");
                        observation.Mods.Reverse();
                        Check(engine.Evaluate(observation, priorities) == result, "Permutation invariant");
                        var roundtrip = JsonConvert.DeserializeObject<Dictionary<string, TabletPriority>>(JsonConvert.SerializeObject(priorities));
                        Check(engine.Evaluate(observation, roundtrip) == result, "Priority configuration serialization is stable");
                        var runtimeProfile = new TabletProfile { ModRanks = four.Take(count).ToDictionary(m => m.GameModIds[0], _ => rank) };
                        var runtimeMods = new Mods(IntPtr.Zero);
                        foreach (var modifier in four.Take(size)) runtimeMods.ExplicitMods.Add((modifier.GameModIds[0], (10000f,10000f)));
                        foreach (bool all in new[] {false,true})
                        {
                            runtimeProfile.RequireAllRankedMods = all;
                            bool actual = (bool)runtimeMatch.Invoke(null,new object[] { runtimeMods,runtimeProfile,type });
                            Check(actual == (expected > 0 && (!all || expected == count)), $"{type} {rank} selected={count} affixes={size}: actual {(all ? "ET" : "OU")} filter");
                        }
                    }
                }
                var one = four[0];
                var sample = new TabletObservation { TabletType = type, Mods = new() { new() { Modifier = one, RawId = one.GameModIds[0], Roll = 5 } } };
                var limits = new Dictionary<string, TabletPriority> { [one.Id] = new(rank, 6) };
                Check(engine.Evaluate(sample, limits).PriorityCount == 0, "Below minimum is secondary, never a priority");
                limits[one.Id] = new(rank, 5);
                Check(engine.Evaluate(sample, limits).Rank == rank, "Threshold edit immediately recalculates observed item");
                limits[one.Id] = new("F", 5);
                Check(engine.Evaluate(sample, limits).Rank == "F", "Rank edit immediately recalculates observed item");
                limits.Clear();
                Check(engine.Evaluate(sample, limits).Label == "N/C", "Removing selection immediately clears priority grade");
                sample.Mods[0].Roll = null; limits[one.Id] = new(rank, 5);
                Check(engine.Evaluate(sample, limits).Uncertain && engine.Evaluate(sample, limits).PriorityCount == 0, "Unknown roll never passes a threshold");
                sample.Mods.Add(new() { RawId = "unknown" });
                Check(engine.Evaluate(sample, limits).Uncertain, "Unknown modifier is explicit uncertainty");
                foreach (string affix in new[] {"prefix","suffix"})
                    for(int size=0;size<=4;size++)
                    {
                        var subset=available.Where(m=>m.Affix==affix).Take(size).ToArray();
                        var groups=subset.SelectMany(m=>m.Groups).ToArray();
                        bool expected=subset.Length<=2 && groups.Distinct().Count()==groups.Length;
                        Check(TabletRankingEngine.Compatible(subset,2,2)==expected,$"{type} {rank}: supplied affix-budget model {affix} count={size}");
                    }
            }
            Console.WriteLine($"VALIDATION {rank}: selected=0,1,2,3,4; affixes=0,1,2,3,4; 8 types; minima, edits, order, persistence: {checks-before} checks passed; global ordinal deliberately withheld.");
        }

        var toy = new TabletMetaCatalog { RankOrder = "SABCDEF".Select(c => c.ToString()).ToList() };
        for (int i = 0; i < 8; i++) toy.Modifiers.Add(new() { Id = "toy:"+i, TabletType = "toy", Rank = i < 4 ? "S" : "A",
            SubRank = i % 4 + 1, RankLabel = (i < 4 ? "S" : "A") + (i % 4 + 1), Affix = i % 2 == 0 ? "prefix" : "suffix", Groups = new() { "g"+i } });
        var t = toy.Modifiers.ToArray(); var model = new TabletRankingEngine(toy);
        TabletObservation Obs(params int[] ids) => new() { TabletType = "toy", Mods = ids.Select(i => new TabletDetectedMod { Modifier = t[i], RawId = t[i].Id }).ToList() };
        var config = t.ToDictionary(m => m.Id, m => new TabletPriority(m.Rank));
        TabletQuality Q(params int[] ids) => model.Evaluate(Obs(ids), config);
        bool Before(TabletQuality a, TabletQuality b) => string.CompareOrdinal(a.OrderKey, b.OrderKey) < 0;
        config[t[2].Id] = new("F"); config[t[3].Id] = new("F");
        Check(Before(Q(0,1,2,3), Q(0,4,5,6)), "SSFF outranks SAAA: custom count precedes secondary quality");
        Check(Q(0,2,3).Rank == "S", "Weak secondaries never lower S");
        config.Remove(t[1].Id);
        Check(Q(0,1).BestCount == 1 && Q(1).Rank == "", "Unselected reference S is never a custom S priority");
        Check(Before(Q(0,1), Q(0,4)), "Unselected reference quality breaks secondary ties");
        Check(Before(Q(0,4), Q(1,4)), "Selected S beats selected A even with an unselected reference S");
        config = t.ToDictionary(m => m.Id, m => new TabletPriority(m.Rank));
        Check(Before(Q(0,3), Q(1,2)), "S1+S4 outranks S2+S3 lexicographically, not by sum");
        Check(Before(Q(0,4), Q(0)), "Real secondary F/A slots are not padded imaginary affixes");
        var firstRoll=Obs(0); firstRoll.Mods[0].Roll=1;
        var secondRoll=Obs(0); secondRoll.Mods[0].Roll=2;
        Check(model.Evaluate(firstRoll,config).OrderKey==model.Evaluate(secondRoll,config).OrderKey,"Rolls above the threshold are equivalent quality, not invented positions");
        var forged=Obs(0);
        forged.Mods[0].Modifier=new TabletMetaModifier {Id=t[0].Id,TabletType="toy",Rank="S",SubRank=1};
        Check(model.Evaluate(forged,config).Uncertain && model.Evaluate(forged,config).Rank=="","A foreign definition cannot reuse a recognized observation's cached result");
        var repeated=Obs(0,0); repeated.Mods[0].Roll=1; repeated.Mods[1].Roll=2;
        var thresholds=new Dictionary<string,TabletPriority>{{t[0].Id,new("S",2)}};
        var duplicateResult=model.Evaluate(repeated,thresholds);
        repeated.Mods.Reverse();
        Check(duplicateResult.Uncertain && duplicateResult.BestCount==1 && model.Evaluate(repeated,thresholds)==duplicateResult,"Duplicates remain uncertain, count once and do not depend on input order");
        var nonfinite=Obs(0); nonfinite.Mods[0].Roll=float.NaN;
        Check(model.Evaluate(nonfinite,thresholds).Uncertain && model.Evaluate(nonfinite,thresholds).BestCount==0,"Nonfinite roll cannot count as a priority");
        var combos = new List<int[]>();
        for (int mask = 1; mask < 256; mask++)
        {
            var indices = Enumerable.Range(0,8).Where(i => (mask & (1<<i)) != 0).ToArray();
            if (indices.Length <= 4 && TabletRankingEngine.Compatible(indices.Select(i => t[i]).ToArray(),2,2)) combos.Add(indices);
        }
        // Independent oracle compares value sequences directly, never engine keys.
        int Compare(int[] x, int[] y)
        {
            int bestX = x.Min(i => "SABCDEF".IndexOf(t[i].Rank)), bestY = y.Min(i => "SABCDEF".IndexOf(t[i].Rank));
            int comparison = bestX.CompareTo(bestY); if (comparison != 0) return comparison;
            comparison = y.Count(i => "SABCDEF".IndexOf(t[i].Rank)==bestY).CompareTo(x.Count(i => "SABCDEF".IndexOf(t[i].Rank)==bestX));
            if (comparison != 0) return comparison;
            int[] gx = x.Select(i => "SABCDEF".IndexOf(t[i].Rank)).Order().Concat(Enumerable.Repeat(7,4-x.Length)).ToArray();
            int[] gy = y.Select(i => "SABCDEF".IndexOf(t[i].Rank)).Order().Concat(Enumerable.Repeat(7,4-y.Length)).ToArray();
            for (int j=0;j<4;j++) { comparison=gx[j].CompareTo(gy[j]); if(comparison!=0)return comparison; }
            var ix=x.OrderBy(i=>"SABCDEF".IndexOf(t[i].Rank)).ThenBy(i=>t[i].SubRank).ToArray();
            var iy=y.OrderBy(i=>"SABCDEF".IndexOf(t[i].Rank)).ThenBy(i=>t[i].SubRank).ToArray();
            for(int j=0;j<ix.Length;j++) { comparison=t[ix[j]].SubRank.CompareTo(t[iy[j]].SubRank); if(comparison!=0)return comparison; }
            return 0;
        }
        foreach(var x in combos) foreach(var y in combos)
            Check(Math.Sign(Compare(x,y)) == Math.Sign(string.CompareOrdinal(Q(x).OrderKey,Q(y).OrderKey)), "Independent admissible-combination ordering oracle");
        Check(!TabletRankingEngine.Compatible(new[] {t[0],t[2],t[4]},2,2), "Three prefixes violate a supplied verified 2-prefix budget");
        Check(!TabletRankingEngine.Compatible(new[] {t[1],t[3],t[5]},2,2), "Three suffixes violate a supplied verified 2-suffix budget");
        Check(!TabletRankingEngine.Compatible(new[] {t[0],t[0]}), "Modifier uniqueness");
        Check(!TabletRankingEngine.Compatible(t.Take(5).ToArray()), "At most four affixes");
        var tooMany=model.Evaluate(Obs(0,1,2,3,4),config);
        Check(tooMany.Uncertain && tooMany.Label.EndsWith('?'),"Excessive affixes never claim a complete quality or ordinal");
        var shrine = catalog.ForType("Irradiated Tablet").Where(m=>m.Groups.Contains("MapAdditionalShrine")).ToArray();
        Check(shrine.Length == 2 && !TabletRankingEngine.Compatible(shrine), "Real shrine chance and shrine count cannot coexist");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<5000;i++)
        {
            var varied = new Dictionary<string,TabletPriority> { [t[0].Id]=new("S",i) };
            model.Evaluate(Obs(0),varied);
        }
        watch.Stop();
        Check(model.CachedResults <= TabletRankingEngine.CacheLimit, "Bounded cache over 5000 different configurations");
        Check(watch.Elapsed < TimeSpan.FromSeconds(15), "Bounded performance over configuration edits");
        Console.WriteLine($"VALIDATION oracle: {combos.Count} admissible reference combinations, {combos.Count*combos.Count} pair comparisons; 5000 configuration edits in {watch.ElapsedMilliseconds} ms; cache={model.CachedResults}.");
    }
    private static int nativeWindows;
    private static bool CountWindow(IntPtr window, IntPtr data) { nativeWindows++; return true; }
    private static bool StopWindows(IntPtr window, IntPtr data) { nativeWindows++; return false; }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks++;
    }

    public static int Main(string[] args)
    {
        try
        {
            string root = args[0];
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string file = Path.Combine(root, name.Name + ".dll");
                if (!File.Exists(file)) file = Path.Combine(root, "Plugins", "StashUtility", name.Name + ".dll");
                return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
            };
            CheckWaystoneCraft(root);
            CheckWaystoneNavigation(root);
            CheckCompactCompositions();
            Run();
            CheckTabletPresets();
            CheckTabletModes();
            RunMetaFilters(args.Length > 1 && args[1] != "none" ? args[1] : null);
            CheckWorkbookPreset();
            CheckTooltipGeometry(root, args.Length > 2 ? args[2] : null);
            CheckFontReload(root);
            CheckTabletSettingsUi(root, args.Length > 2 ? args[2] : null);
            Console.WriteLine(checks + " tablet classification checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckTabletPresets()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var plugin = new StashUtilityCore { DllDirectory = Path.Combine(Path.GetTempPath(), "TabletPresets-" + Guid.NewGuid().ToString("N")) };
        var save = typeof(StashUtilityCore).GetMethod("SaveTabletPreset", flags);
        var load = typeof(StashUtilityCore).GetMethod("LoadTabletPreset", flags);
        Check(plugin.Settings.TabletPresets.Length == 5 && plugin.Settings.TabletPresets.All(p => p == null), "Five initially empty preset slots");
        string original = JsonConvert.SerializeObject(plugin.Settings);
        Check(!(bool)load.Invoke(plugin, new object[] { 0 }) && JsonConvert.SerializeObject(plugin.Settings) == original, "An empty preset never replaces current choices");
        string[] expected = new string[5];
        string[] grades = { "S", "A", "B", "C", "D" };
        for (int slot = 0; slot < 5; slot++)
        {
            plugin.Settings.TabletProfiles = new();
            foreach (string type in StashUtilitySettings.TabletTypes)
            {
                string id = ModDatabase.AllTabletMods.First(m => m.TabletTypes.Contains(type)).Id;
                plugin.Settings.TabletProfiles[type] = new()
                {
                    ModRanks = new() { [id] = grades[slot] }, GoodModPatterns = new() { id },
                    ModRequiredMinRolls = new() { [id] = slot + 12 }, UseMetaFilters = slot % 2 == 0,
                    RequireAllRankedMods = slot % 2 == 1,
                    ActiveMetaProfileId = "custom-" + slot,
                    MetaProfiles = new() { new() { Id = "custom-" + slot, Name = "Custom " + slot, TabletType = type,
                        Rule = new() { Kind = TabletRuleKind.All, Children = new() {
                            new() { Kind = TabletRuleKind.MinimumCount, Rank = "B", Minimum = 1 } } } } }
                };
            }
            plugin.Settings.MinTabletGoodMods = slot % 4 + 1;
            plugin.Settings.FilterTabletGreat = slot % 2 == 0;
            Check((bool)save.Invoke(plugin, new object[] { slot }), "Save preset " + (slot + 1));
            expected[slot] = JsonConvert.SerializeObject(plugin.Settings.TabletProfiles);
        }
        plugin.Settings = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(plugin.Settings));
        plugin.Settings.ProtectedInventoryCells.Add(17);
        Check(plugin.Settings.TabletPresets.Count(p => p != null) == 5, "All five presets survive settings serialization");
        for (int slot = 0; slot < 5; slot++)
        {
            Check((bool)load.Invoke(plugin, new object[] { slot }) && JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == expected[slot], "Restore all eight tablet types, ranks, minimums and nested rules from slot " + (slot + 1));
            Check(plugin.Settings.MinTabletGoodMods == slot % 4 + 1 && plugin.Settings.FilterTabletGreat == (slot % 2 == 0), "Restore tablet highlight thresholds from slot " + (slot + 1));
            Check(plugin.Settings.TabletProfiles.Values.All(p => p.RequireAllRankedMods == (slot % 2 == 1)), "Restore ET / OU independently for all tablet types in preset " + (slot + 1));
            plugin.Settings.TabletProfiles.Values.First().ModRanks.Clear();
            load.Invoke(plugin, new object[] { slot });
            Check(JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == expected[slot], "Editing active rules never mutates the saved preset " + (slot + 1));
            Check(plugin.Settings.ProtectedInventoryCells.Contains(17), "Loading presets preserves inventory protections " + (slot + 1));
        }
        Check(!(bool)save.Invoke(plugin, new object[] { -1 }) && !(bool)load.Invoke(plugin, new object[] { 5 }), "Out-of-range preset requests are harmless");
        Check(JsonConvert.DeserializeObject<StashUtilitySettings>("{}").TabletPresets.Length == 5, "Old settings gain five slots without losing their choices");
        var legacy = new TabletProfile { ModRanks = new() { ["fixture"] = "S" }, GoodModPatterns = new() { "fixture" } };
        typeof(StashUtilityCore).GetMethod("MigrateTabletModRanks", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { legacy });
        Check(legacy.ModRanks["fixture"] == "S" && legacy.GodModPatterns.Contains("fixture") && !legacy.GoodModPatterns.Contains("fixture"), "Legacy bookkeeping never downgrades an explicit saved S choice");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunMetaFilters(string workbookPath)
    {
        var catalog = ModDatabase.TabletMeta;
        Check(catalog.Validate().Count == 0 && catalog.Modifiers.Count == 254, "Complete validated reference catalogue");
        Check(catalog.RankOrder.SequenceEqual("SABCDEF".Select(c => c.ToString())), "Exact S to F order, with E and F distinct");
        Check(catalog.ImportIssues.Count == 0 && catalog.Modifiers.All(m => m.GameModIds.Count > 0), "Every workbook row has a game-identifier mapping");
        foreach (string typeName in catalog.Types.Values)
        {
            var entries = catalog.ForType(typeName).ToArray();
            Check(entries.Length > 0 && entries.Select(m => m.Id).Distinct().Count() == entries.Length, "No duplicate IDs: " + typeName);
            foreach (string rank in catalog.RankOrder)
            {
                var numbers = entries.Where(m => m.Rank == rank).Select(m => m.SubRank).ToArray();
                Check(numbers.SequenceEqual(numbers.OrderBy(n => n)) && numbers.Distinct().Count() == numbers.Length, "Numeric subrank order: " + typeName + " " + rank);
            }
            var testProfile = new TabletFilterProfile { TabletType = typeName, Rule = new() { Kind = TabletRuleKind.SubRank, RankLabel = "S1" } };
            var top = entries.Single(m => m.RankLabel == "S1");
            Check(TabletFilterEngine.Evaluate(testProfile, catalog, catalog.Recognize(typeName, new[] { top.GameModIds[0] + "1" })).State == TabletFilterState.Accepted, "Real game identifiers: " + typeName);
            Check(top.SourceFields["rank"].ToString() == top.RankLabel && top.SourceFields["mod_fr"].ToString() == top.FrenchText && top.SourceFields["mod_en"].ToString() == top.EnglishText, "Original fields retained: " + typeName);
        }

        const string type = "Irradiated Tablet";
        TabletRuleNode Sub(string label) => new() { Kind = TabletRuleKind.SubRank, RankLabel = label };
        TabletRuleNode Group(TabletRuleKind kind, params TabletRuleNode[] nodes) => new() { Kind = kind, Children = nodes.ToList() };
        TabletObservation Obs(params string[] labels) => new() { TabletType = type, Mods = labels.Select(label =>
            new TabletDetectedMod { RawId = label, Modifier = catalog.ForType(type).Single(m => m.RankLabel == label) }).ToList() };
        TabletFilterResult Evaluate(TabletRuleNode rule, params string[] labels) => TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = rule }, catalog, Obs(labels));
        TabletRuleNode Mixed() => Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), Group(TabletRuleKind.All, Sub("S1"), Sub("B1")));
        Check(Evaluate(Mixed(), "S1", "B1", "C1").State == TabletFilterState.Accepted, "(S1 AND A1) OR (S1 AND B1): S1+B1+C1 passes");
        Check(Evaluate(Mixed(), "S1", "A2", "C1").State == TabletFilterState.Rejected, "(S1 AND A1) OR (S1 AND B1): S1+A2+C1 fails");
        Check(Evaluate(Mixed(), "S1", "A1", "B1").State == TabletFilterState.Accepted, "(S1 AND A1) OR (S1 AND B1): S1+A1+B1 passes");
        Check(Evaluate(Group(TabletRuleKind.All, Sub("S1"), Sub("A2")), "S1", "A2", "B1", "D3").State == TabletFilterState.Accepted, "Four modifiers: S1 AND A2");
        Check(Evaluate(Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), "S1", "A2", "B1", "D3").State == TabletFilterState.Rejected, "Four modifiers: missing A1");
        Check(Evaluate(Mixed(), "S1", "A2", "B1", "D3").State == TabletFilterState.Accepted, "Four modifiers: mixed groups pass via B1");
        Check(Evaluate(Group(TabletRuleKind.Any, Sub("S2"), Sub("A1")), "S1", "A2", "B1", "D3").State == TabletFilterState.Rejected, "Four modifiers: S2 OR A1 absent");
        Check(Evaluate(Group(TabletRuleKind.All, Group(TabletRuleKind.Any, Sub("S1"), Sub("S2")), Group(TabletRuleKind.Any, Sub("A1"), Sub("A2"))), "S2", "A2").State == TabletFilterState.Accepted, "(S1 OR S2) AND (A1 OR A2)");
        Check(Evaluate(Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1"), Sub("B1")), Group(TabletRuleKind.All, Sub("S1"), Sub("A2"))), "S1", "A2").State == TabletFilterState.Accepted, "Three-way AND mixed with two-way AND");
        var sequence = new[] { "S1", "A1", "B1", "D3" };
        for (int count = 1; count <= 4; count++) Check(Evaluate(Sub("S1"), sequence.Take(count).ToArray()).State == TabletFilterState.Accepted, "One to four affixes: " + count);
        foreach (string a in sequence) foreach (string b in sequence.Where(s => s != a))
            foreach (string c in sequence.Where(s => s != a && s != b))
            {
                string d = sequence.Single(s => s != a && s != b && s != c);
                Check(Evaluate(Mixed(), a, b, c, d).State == TabletFilterState.Accepted, "All 24 affix permutations");
            }
        TabletRuleNode Count(string rank, int count, bool exact = false) => new() { Kind = TabletRuleKind.MinimumCount, Rank = rank, Minimum = count, ExactCountRank = exact };
        Check(Evaluate(Count("A", 2), "S1", "S2").State == TabletFilterState.Accepted, "At least two A or better counts S");
        Check(Evaluate(Count("A", 2, true), "S1", "S2").State == TabletFilterState.Rejected, "Exact A count excludes S");
        Check(Evaluate(Count("B", 3), "S1", "A1", "B1").State == TabletFilterState.Accepted, "Three B or better");
        Check(Evaluate(Count("B", 3), "S1", "A1", "C1").State == TabletFilterState.Rejected, "Lower rank not counted");
        Check(Evaluate(Group(TabletRuleKind.All, Count("A", 2), Group(TabletRuleKind.Any, Sub("S1"), Sub("S2"))), "S1", "A2").State == TabletFilterState.Accepted, "Count criteria participate in nested logic");
        Check(Evaluate(Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), Count("B", 3)), "A1", "A2", "B1").State == TabletFilterState.Accepted, "OR accepts the quality-count branch");
        Check(Evaluate(new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" }, "S1", "A1", "C1").State == TabletFilterState.Accepted, "No rank below C accepts C");
        Check(Evaluate(new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" }, "S1", "D3").State == TabletFilterState.Rejected, "No rank below C rejects D");
        Check(Evaluate(new() { Kind = TabletRuleKind.RankExact, Rank = "A" }, "S1", "S2").State == TabletFilterState.Rejected, "Exact rank remains distinct from minimum rank");
        Check(Evaluate(Sub("S1"), "S1").State == TabletFilterState.Accepted, "Absence of unchosen useful modifiers is not a penalty");
        Check(Evaluate(Sub("F2"), "S1").State == TabletFilterState.Invalid, "Absent individual subrank is not invented");
        Check(Evaluate(new() { Kind = TabletRuleKind.RankExact, Rank = "Q" }, "S1").State == TabletFilterState.Invalid, "Unknown rank rejected");
        foreach (var kind in new[] { TabletRuleKind.All, TabletRuleKind.Any }) Check(Evaluate(Group(kind), "S1").State == TabletFilterState.Invalid, "Empty logical groups never approve");
        Check(Evaluate(Count("A", 0), "S1").State == TabletFilterState.Invalid && Evaluate(Count("A", 5), "S1").State == TabletFilterState.Invalid, "Invalid count bounds");
        var exactMod = catalog.ForType(type).Single(m => m.RankLabel == "A2");
        Check(Evaluate(new() { Kind = TabletRuleKind.Modifier, ModifierId = exactMod.Id }, "A2").State == TabletFilterState.Accepted, "Exact canonical modifier");
        Check(Evaluate(new() { Kind = TabletRuleKind.Modifier, ModifierId = "Breach Tablet:A2" }, "A2").State == TabletFilterState.Invalid, "Other tablet modifier cannot leak into profile");
        var common = catalog.ForType(type).Single(m => m.Code == "G01");
        Check(catalog.Recognize(type, common.GameModIds).Mods.Single().Modifier.RankLabel == "S2" &&
            catalog.Recognize("Overseer Tablet", common.GameModIds).Mods.Single().Modifier.RankLabel == "S1", "Same native modifier has independent grades per tablet");
        Check(catalog.Recognize(type, new[] { common.GameModIds[0] + "12" }).Mods.Single().Modifier?.Id == common.Id, "Numeric tier suffixes recognized precisely");
        Check(catalog.Recognize(type, new[] { common.GameModIds[0] + "Different" }).Uncertain, "Similar native names do not collide");
        Check(catalog.Recognize(type, new[] { common.FrenchText, common.EnglishText }).Uncertain, "Translations and numeric descriptions do not guess native IDs");
        var profile = new TabletFilterProfile { TabletType = type, Rule = Mixed() };
        var unknown = Obs("S1", "B1"); unknown.Mods.Add(new() { RawId = "UnrecognizedGameMod1" });
        Check(TabletFilterEngine.Evaluate(profile, catalog, unknown).State == TabletFilterState.Uncertain, "Unknown modifiers force explicit uncertainty");
        Check(TabletFilterEngine.Evaluate(profile, catalog, unknown).Action == TabletFilterAction.Signal, "Unknown modifiers always signal instead of silently keeping or ignoring");
        var belowUnknown = Obs("D3"); belowUnknown.Mods.Add(new() { RawId = "UnknownExplicit" });
        var belowResult = TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" } }, catalog, belowUnknown);
        Check(belowResult.State == TabletFilterState.Uncertain && belowResult.Rule.Matches == false, "Known below-threshold modifier remains a failed condition despite unknown data");
        var spoofed = new TabletObservation { TabletType = type, Mods = new() { new() { RawId = exactMod.Id,
            Modifier = new() { Id = exactMod.Id, TabletType = type, Rank = "S", RankLabel = "S1" } } } };
        Check(TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = new() { Kind = TabletRuleKind.RankExact, Rank = "S" } }, catalog, spoofed).State == TabletFilterState.Rejected, "Observed modifiers use canonical catalogue grades, not supplied rank text");
        var actionProfile = new TabletFilterProfile { TabletType = type, Rule = Sub("S1"), OnMatch = TabletFilterAction.Signal, OnMiss = TabletFilterAction.Keep };
        Check(TabletFilterEngine.Evaluate(actionProfile, catalog, Obs("S1")).Action == TabletFilterAction.Signal &&
            TabletFilterEngine.Evaluate(actionProfile, catalog, Obs("A1")).Action == TabletFilterAction.Keep, "Configured match and miss actions remain independent");
        var duplicates = Obs("S1", "S1");
        Check(TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = Count("S", 2) }, catalog, duplicates).State == TabletFilterState.Uncertain, "Duplicate modifiers never inflate counts");
        var unreadable = Obs("S1"); unreadable.Readable = false;
        Check(TabletFilterEngine.Evaluate(profile, catalog, unreadable).State == TabletFilterState.Uncertain, "Unreadable game data remains uncertain");
        Check(Evaluate(Sub("S1"), "S1", "S2", "A1", "A2", "B1").State == TabletFilterState.Uncertain, "More than four modifiers are not silently accepted");
        var cycle = new TabletRuleNode(); cycle.Children.Add(cycle);
        Check(Evaluate(cycle, "S1").State == TabletFilterState.Invalid, "Cyclic input guarded without recursion failure");
        var duplicateNode = Group(TabletRuleKind.All, Sub("S1"), Sub("S2")); duplicateNode.Children[1].Id = duplicateNode.Children[0].Id;
        Check(Evaluate(duplicateNode, "S1").State == TabletFilterState.Invalid, "Duplicate node IDs rejected");
        var deepest = Sub("S1");
        for (int i = 0; i < TabletFilterEngine.MaximumDepth; i++) deepest = Group(TabletRuleKind.All, deepest);
        Check(Evaluate(deepest, "S1").State == TabletFilterState.Accepted, "Supported deep groups evaluate");
        Check(Evaluate(Group(TabletRuleKind.All, deepest), "S1").State == TabletFilterState.Invalid, "Excessive depth rejected");

        var settings = new StashUtilitySettings();
        foreach (string tabletType in catalog.Types.Values)
        {
            var p = new TabletFilterProfile { TabletType = tabletType, Rule = Sub("S1") };
            settings.TabletProfiles[tabletType] = new() { UseMetaFilters = true, ActiveMetaProfileId = p.Id, MetaProfiles = new() { p }, MetaSearch = "test", ShowMetaEnglish = false };
            settings.TabletProfiles[tabletType].ModRanks["LegacyMod"] = "B";
        }
        var reloaded = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(settings), new JsonSerializerSettings { MaxDepth = 128 });
        foreach (string tabletType in catalog.Types.Values)
        {
            var saved = reloaded.TabletProfiles[tabletType];
            Check(saved.MetaProfiles.Single().TabletType == tabletType && saved.ActiveMetaProfileId == settings.TabletProfiles[tabletType].ActiveMetaProfileId && saved.MetaSearch == "test" && !saved.ShowMetaEnglish && saved.ModRanks["LegacyMod"] == "B", "Persistent independent profiles and legacy settings: " + tabletType);
        }
        settings.TabletProfiles[type].MetaProfiles[0].Rule = deepest;
        var deepReload = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(settings), new JsonSerializerSettings { MaxDepth = 128 });
        Check(TabletFilterEngine.Validate(deepReload.TabletProfiles[type].MetaProfiles[0], catalog) == "", "Deep tree persists without truncation");
        var archive = new TabletProfileArchive { TabletType = type, CatalogSha256 = catalog.SourceSha256, Enabled = true, Profiles = new() { profile }, ActiveProfileId = profile.Id };
        var imported = TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, type);
        Check(imported.ActiveProfileId == profile.Id && imported.Enabled && TabletFilterEngine.Evaluate(imported.Profiles.Single(), catalog, Obs("S1", "B1")).State == TabletFilterState.Accepted, "Profile export/import round-trip");
        void Refuse(Action action, string label) { bool failed = false; try { action(); } catch (FormatException) { failed = true; } catch (Newtonsoft.Json.JsonException) { failed = true; } Check(failed, label); }
        Refuse(() => TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, "Breach Tablet"), "Cross-type profile import refused");
        Refuse(() => TabletFilterEngine.Import("{bad json}", catalog, type), "Malformed import refused");
        Refuse(() => TabletFilterEngine.Import(new string('x', 1024 * 1024 + 1), catalog, type), "Oversized import refused");
        archive.Profiles[0].Rule = new();
        Refuse(() => TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, type), "Invalid rule import refused");
        var core = new StashUtilityCore { Settings = settings };
        var analyze = typeof(StashUtilityCore).GetMethod("AnalyzeMetaTablet", BindingFlags.Instance | BindingFlags.NonPublic);
        var nativeMods = new Mods(IntPtr.Zero);
        nativeMods.ExplicitMods.Add((catalog.ForType(type).Single(m => m.RankLabel == "S1").GameModIds[0] + "1", (1f, 0f)));
        var actualResult = (TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods });
        Check(actualResult.State == TabletFilterState.Accepted, "Existing Mods reader feeds actual filter adapter");
        nativeMods.ImplicitMods.Add(("UnknownImplicit", (1f, 0f)));
        Check(((TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods })).State == TabletFilterState.Accepted, "Non-affix implicit modifiers do not inflate explicit counts");
        nativeMods.ExplicitMods.Add(("UnknownExplicit", (1f, 0f)));
        Check(((TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods })).State == TabletFilterState.Uncertain, "Actual adapter exposes unknown explicit affixes");
        var factory = typeof(StashUtilityCore).GetMethod("CreateFubgunTabletProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var fubgun = (TabletFilterProfile)factory.Invoke(null, null);
        Check(TabletFilterEngine.Validate(fubgun, catalog) == "", "Fubgun preset has real workbook IDs and valid AND / OR rules");
        TabletObservation FubgunObs(float? roll, string second) => new()
        {
            TabletType = type, Mods = new()
            {
                new() { Modifier = catalog.Recognize(type, new[] { "TowerMapAdditionalModifier" }).Mods[0].Modifier, Roll = roll },
                new() { Modifier = catalog.Recognize(type, new[] { second }).Mods[0].Modifier, Roll = 30 },
            }
        };
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerMonsterEffectiveness")).State == TabletFilterState.Accepted, "Fubgun: 2 map mods AND effectiveness");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerRarePackIncrease")).State == TabletFilterState.Accepted, "Fubgun: 2 map mods AND rare monsters");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(1, "TowerRarePackIncrease")).State == TabletFilterState.Rejected, "Fubgun: only 1 extra map mod rejected");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(null, "TowerRarePackIncrease")).State == TabletFilterState.Uncertain, "Unreadable minimum roll never accepted");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerDroppedItemRarityIncrease")).State == TabletFilterState.Rejected, "Fubgun: required companion modifier missing");
        var fubgunReload = JsonConvert.DeserializeObject<TabletFilterProfile>(JsonConvert.SerializeObject(fubgun));
        Check(fubgunReload.Rule.Children[0].MinimumRoll == 2 && TabletFilterEngine.Evaluate(fubgunReload, catalog, FubgunObs(1, "TowerRarePackIncrease")).State == TabletFilterState.Rejected, "Minimum roll survives profile save and reload");
        var fubgunCore = new StashUtilityCore();
        fubgunCore.Settings.TabletProfiles[type] = new() { UseMetaFilters = true, ActiveMetaProfileId = fubgun.Id, MetaProfiles = new() { fubgun } };
        var rawFubgun = new Mods(IntPtr.Zero);
        rawFubgun.ExplicitMods.Add(("TowerMapAdditionalModifier1", (2, 0)));
        rawFubgun.ExplicitMods.Add(("TowerMonsterEffectiveness1", (12, 0)));
        Check(((TabletFilterResult)analyze.Invoke(fubgunCore, new object[] { type, rawFubgun })).State == TabletFilterState.Accepted, "Runtime adapter reads real mod rolls for AND / OR");
        rawFubgun.ExplicitMods[0] = ("TowerMapAdditionalModifier1", (1, 0));
        Check(((TabletFilterResult)analyze.Invoke(fubgunCore, new object[] { type, rawFubgun })).State == TabletFilterState.Rejected, "Runtime adapter rejects low roll");
        Check(Evaluate(new() { Kind = TabletRuleKind.All, Children = new() { Sub("E1"), new() { Kind = TabletRuleKind.Any, Children = new() { Sub("F1"), Sub("A1") } } } }, "E1", "F1").State == TabletFilterState.Accepted, "E AND (F OR A) accepts E+F");
        Check(Evaluate(new() { Kind = TabletRuleKind.All, Children = new() { Sub("E1"), new() { Kind = TabletRuleKind.Any, Children = new() { Sub("F1"), Sub("A1") } } } }, "E1", "B1").State == TabletFilterState.Rejected, "E AND (F OR A) rejects wrong second modifier");
        foreach (string native in new[] {"TowerDeliriumFogPersistence","TowerRitualRerollCostIncrease","TowerRitualDeferCostIncrease"})
        {
            string tabletType=ModDatabase.AllTabletMods.Single(m=>m.Id==native).TabletTypes[0];
            var signedProfile=new TabletProfile {ModRanks=new(){[native]="S"},ModRequiredMinRolls=new(){[native]=25}};
            var signedCore=new StashUtilityCore(); signedCore.Settings.TabletProfiles[tabletType]=signedProfile;
            var definition=catalog.Recognize(tabletType,new[]{native}).Mods[0].Modifier;
            var rule=new TabletFilterProfile {TabletType=tabletType,Rule=new(){Kind=TabletRuleKind.Modifier,ModifierId=definition.Id,MinimumRoll=25}};
            signedProfile.MetaProfiles.Add(rule); signedProfile.ActiveMetaProfileId=rule.Id;
            var signedMods=new Mods(IntPtr.Zero); signedMods.ExplicitMods.Add((native,(-25,0)));
            var basic=typeof(StashUtilityCore).GetMethod("ClassifyTabletMods",BindingFlags.Static|BindingFlags.NonPublic);
            Check(((TabletClassification)basic.Invoke(null,new object[]{signedMods,signedProfile,tabletType})).Rank=="S","Negative native reduction meets positive custom minimum: "+native);
            Check(((TabletFilterResult)analyze.Invoke(signedCore,new object[]{tabletType,signedMods})).State==TabletFilterState.Accepted,"Negative native reduction meets advanced minimum: "+native);
            signedMods.ExplicitMods[0]=(native,(-24,0));
            Check(!((TabletClassification)basic.Invoke(null,new object[]{signedMods,signedProfile,tabletType})).IsRanked,"Below-minimum negative reduction remains unranked: "+native);
            Check(((TabletFilterResult)analyze.Invoke(signedCore,new object[]{tabletType,signedMods})).State==TabletFilterState.Rejected,"Below-minimum reduction rejects advanced rule: "+native);
        }
        if (workbookPath != null)
        {
            Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(workbookPath))).Equals(catalog.SourceSha256, StringComparison.OrdinalIgnoreCase), "Primary JSON fingerprint matches embedded data");
            var source = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(workbookPath));
            int importedEntries = 0;
            foreach(var tablet in source["tablet_types"])
                foreach(var mod in tablet["mods"])
                {
                    var entry = catalog.Modifiers.Single(m => m.CanonicalType == (string)mod["tablet_type"] && m.Code == (string)mod["mod_code"]);
                    foreach(var field in ((Newtonsoft.Json.Linq.JObject)mod).Properties())
                        Check(Newtonsoft.Json.Linq.JToken.DeepEquals(entry.SourceFields[field.Name] == null ? Newtonsoft.Json.Linq.JValue.CreateNull() : Newtonsoft.Json.Linq.JToken.FromObject(entry.SourceFields[field.Name]), field.Value), "Exact primary JSON field: " + entry.Id + " " + field.Name);
                    importedEntries++;
                }
            Check(importedEntries == 254, "Independent JSON extraction confirms every entry");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckTabletModes()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var plugin = new StashUtilityCore();
        var change = typeof(StashUtilityCore).GetMethod("SetTabletMode", flags);
        bool Set(string mode, string strategy = null) => (bool)change.Invoke(plugin, new object[] { mode, strategy });
        plugin.Settings.TabletProfiles["Irradiated Tablet"] = new() { ModRanks = new() { ["TowerMonsterEffectiveness"] = "F" },
            ModRequiredMinRolls = new() { ["TowerMonsterEffectiveness"] = 14 }, RequireAllRankedMods = true };
        var save = typeof(StashUtilityCore).GetMethod("SaveTabletPreset", flags);
        var load = typeof(StashUtilityCore).GetMethod("LoadTabletPreset", flags);
        save.Invoke(plugin, new object[] { 2 });
        string personal = JsonConvert.SerializeObject(plugin.Settings.TabletProfiles);
        string slots = JsonConvert.SerializeObject(plugin.Settings.TabletPresets);
        Check(JsonConvert.DeserializeObject<StashUtilitySettings>("{}").TabletMode == "custom", "Existing settings stay in Custom mode");
        Check(Set("farm_trade"), "Select farming and resale mode");
        Check(plugin.Settings.TabletProfiles.Count == 8 && plugin.Settings.TabletProfiles["Irradiated Tablet"].ModRanks["TowerMonsterEffectiveness"] == "A", "Resale mode applies reference grades to all eight types");
        string reference = JsonConvert.SerializeObject(plugin.Settings.TabletProfiles);
        Check(!(bool)save.Invoke(plugin, new object[] { 2 }) && !(bool)load.Invoke(plugin, new object[] { 2 }), "Automatic modes cannot save or load a personal preset");
        Check(Set("farm") && JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) != reference, "Farming applies its own loot and density priorities");
        Check(plugin.Settings.TabletProfiles["Irradiated Tablet"].ModRanks["TowerMonsterEffectiveness"] == "S", "Farming mode prioritizes monster effectiveness");
        foreach (string strategy in new[] { "fubgun", "ritual", "hiveblood" })
        {
            Check(Set("strategy", strategy), "Select specific strategy " + strategy);
            var active = plugin.Settings.TabletProfiles.Values.Single(p => p.UseMetaFilters);
            var rule = active.MetaProfiles.Single();
            Check(TabletFilterEngine.Validate(rule, ModDatabase.TabletMeta) == "", "Strategy uses valid canonical mods " + strategy);
            var ids = strategy switch
            {
                "fubgun" => new[] { "TowerMapAdditionalModifier", "TowerMonsterEffectiveness" },
                "ritual" => new[] { "TowerRitualOmenChance", "TowerRitualRerollCostIncrease" },
                _ => new[] { "TowerBreachHivebloodQuantity" }
            };
            var observation = ModDatabase.TabletMeta.Recognize(rule.TabletType, ids);
            foreach (var mod in observation.Mods) mod.Roll = 10000;
            Check(TabletFilterEngine.Evaluate(rule, ModDatabase.TabletMeta, observation).State == TabletFilterState.Accepted, "Strategy accepts its farming combination " + strategy);
            observation.Mods.RemoveAt(0);
            Check(TabletFilterEngine.Evaluate(rule, ModDatabase.TabletMeta, observation).State == TabletFilterState.Rejected, "Strategy rejects its missing key modifier " + strategy);
            Check(JsonConvert.SerializeObject(plugin.Settings.TabletPresets) == slots, "Changing strategy preserves all five saved slots " + strategy);
        }
        plugin.Settings = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(plugin.Settings));
        Check(Set("custom") && JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == personal, "Returning to Custom after restart restores ranks, rolls and ET / OU");
        Check((bool)load.Invoke(plugin, new object[] { 2 }), "Custom re-enables preset loading");
        string stable = JsonConvert.SerializeObject(plugin.Settings);
        Check(!Set("unknown") && !Set("strategy", "unknown") && JsonConvert.SerializeObject(plugin.Settings) == stable, "Invalid mode or strategy never changes current choices");
    }

    private static void CheckWorkbookPreset()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var apply = typeof(StashUtilityCore).GetMethod("ApplyTabletFarmPreset", flags);
        var set = typeof(StashUtilityCore).GetMethod("SetTabletModRank", flags);
        void Set(TabletProfile p, string id, string rank) => set.Invoke(null, new object[] { id, rank, p.ModRanks, p.GoodModPatterns, p.BadModPatterns, p.GodModPatterns });
        var presetCore = new StashUtilityCore();
        foreach (string type in StashUtilitySettings.TabletTypes)
        {
            var p = new TabletProfile(); presetCore.Settings.TabletProfiles[type] = p;
            foreach (var mod in ModDatabase.AllTabletMods.Where(m => m.TabletTypes.Contains(type)))
                Set(p, mod.Id, ModDatabase.GetTabletFarmRank(type, mod.Id) ?? "");
            p.ModRequiredMinRolls["TowerMonsterEffectiveness"] = 14;
        }
        presetCore.Settings.TabletFarmPresetVersion = 1;
        apply.Invoke(presetCore, new object[] { false });
        foreach (var mod in ModDatabase.TabletMeta.Modifiers)
        {
            var p = presetCore.Settings.TabletProfiles[mod.TabletType];
            var old = ModDatabase.AllTabletMods.Single(m => m.TabletTypes.Contains(mod.TabletType) && mod.GameModIds.Contains(m.Id));
            Check(p.ModRanks[old.Id] == mod.Rank, "Workbook grade preserved: " + mod.Id);
        }
        foreach (string type in StashUtilitySettings.TabletTypes)
        {
            var p = presetCore.Settings.TabletProfiles[type];
            int count = p.ModRanks.Count(m => m.Value == "S");
            Check(count is >= 1 and <= 2, "Only 1 or 2 S modifiers for " + type);
            Check(p.ModRequiredMinRolls["TowerMonsterEffectiveness"] == 14, "Migration preserves minimum rolls " + type);
        }
        Check(!(bool)apply.Invoke(presetCore, new object[] { false }), "Workbook migration is applied once");
        var custom = new StashUtilityCore();
        custom.Settings.TabletFarmPresetVersion = 1;
        var profile = new TabletProfile { UseMetaFilters = true, MetaProfiles = new() { new() { Name = "Custom", TabletType = "Irradiated Tablet" } } };
        Set(profile, "TowerMonsterEffectiveness", "F");
        profile.ModRequiredMinRolls["TowerMonsterEffectiveness"] = 13;
        custom.Settings.TabletProfiles["Irradiated Tablet"] = profile;
        string before = JsonConvert.SerializeObject(profile);
        apply.Invoke(custom, new object[] { false });
        Check(JsonConvert.SerializeObject(profile) == before, "Manual F, selections, rolls and AND / OR profile stay unchanged");
        apply.Invoke(custom, new object[] { true });
        Check(profile.ModRanks["TowerMonsterEffectiveness"] == "A" && profile.UseMetaFilters && profile.MetaProfiles.Count == 1 && profile.ModRequiredMinRolls["TowerMonsterEffectiveness"] == 13, "Explicit workbook reset keeps AND / OR and roll settings");
        foreach (string rank in new[] { "E", "F" })
        {
            Set(profile, "TowerMonsterEffectiveness", rank);
            var migrate = typeof(StashUtilityCore).GetMethod("MigrateTabletModRanks", flags);
            migrate.Invoke(null, new object[] { profile });
            Check(profile.ModRanks["TowerMonsterEffectiveness"] == rank, "Saved " + rank + " never folded into D");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CraftClipboardGetter(IntPtr context);

    private static void CheckCraftClipboard()
    {
        var platform=ImGuiNET.ImGui.GetPlatformIO();
        IntPtr previous=platform.Platform_GetClipboardTextFn;
        IntPtr value=IntPtr.Zero;
        CraftClipboardGetter getter=_=>value;
        var read=typeof(StashUtilityCore).GetMethod("CraftClipboardText",BindingFlags.Static|BindingFlags.NonPublic);
        try
        {
            // Replace the native callback only in this isolated ImGui context.
            // Never read or replace the user's actual Windows clipboard.
            platform.Platform_GetClipboardTextFn=Marshal.GetFunctionPointerForDelegate(getter);
            bool reproduced=false;
            try{ImGuiNET.ImGui.GetClipboardText();}catch(NullReferenceException){reproduced=true;}
            Check(reproduced,"The reported NullReferenceException is reproduced by the old wrapper when the native clipboard returns null");
            Check((string)read.Invoke(null,null)=="","An unavailable or empty native clipboard no longer aborts craft startup");
            value=Marshal.StringToCoTaskMemUTF8("");
            Check((string)read.Invoke(null,null)=="","An explicit empty clipboard string remains safe");
            Marshal.FreeCoTaskMem(value);value=Marshal.StringToCoTaskMemUTF8("Classe d’objet : Pierres de téléportation\nRareté : Rare\nCorrompu");
            Check((string)read.Invoke(null,null)=="Classe d’objet : Pierres de téléportation\nRareté : Rare\nCorrompu","Fresh copied item text retains accents and line breaks");
            Check((string)read.Invoke(null,null)==ImGuiNET.ImGui.GetClipboardText(),"Nonempty native clipboard text matches the existing wrapper exactly");
        }
        finally
        {
            platform.Platform_GetClipboardTextFn=previous;
            if(value!=IntPtr.Zero)Marshal.FreeCoTaskMem(value);
            GC.KeepAlive(getter);
        }
        Console.WriteLine("VALIDATION craft startup: exact null-clipboard crash reproduced; null, empty and UTF-8 clipboard reads are safe; no system clipboard or game input changed.");
    }

    private static void CheckTooltipGeometry(string root, string previewDirectory)
    {
        NativeLibrary.SetDllImportResolver(typeof(ImGuiNET.ImGui).Assembly, (name, assembly, path) =>
            name == "cimgui" ? NativeLibrary.Load(Path.Combine(root, "cimgui.dll")) : IntPtr.Zero);
        var context = ImGuiNET.ImGui.CreateContext();
        CheckCraftClipboard();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        string previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetDirectoryName(typeof(TabletRankingCheck).Assembly.Location));
        var process = ExileCenter.Core.Process;
        var handle = process.GetType().GetProperty("Handle", flags);
        var previousReader = handle.GetValue(process);
        var readerType = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Utils.SafeMemoryHandle");
        var reader = Activator.CreateInstance(readerType, flags, null, new object[] { System.Diagnostics.Process.GetCurrentProcess().Id }, null);
        var parent = Marshal.AllocHGlobal(Marshal.SizeOf<GameOffsets.Objects.UiElement.UiElementBaseOffset>());
        var child = Marshal.AllocHGlobal(Marshal.SizeOf<GameOffsets.Objects.UiElement.UiElementBaseOffset>());
        var children = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            handle.SetValue(process, reader);
            ImGuiNET.ImGui.GetIO().DisplaySize = new Vector2(640, 360);
            ImGuiNET.ImGui.GetIO().DeltaTime = 1f / 60f;
            unsafe { ImGuiNET.ImGui.GetIO().NativePtr->IniFilename = null; }
            ImGuiNET.ImGui.GetIO().Fonts.AddFontDefault();
            ImGuiNET.ImGui.GetIO().Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytes);
            var highlightPlugin = new StashUtilityCore { DllDirectory = root };
            highlightPlugin.Settings.EnableTabletManager = true;
            highlightPlugin.Settings.HideTabletHighlightsOnItemHover = false;
            var itemType = typeof(ExileCenter.RemoteObjects.States.InGameStateObjects.Item);
            var entityType = itemType.BaseType;
            var highlight = typeof(StashUtilityCore).GetMethod("EvaluateAndHighlightItem", flags);
            void CheckItemOverlay(string path, string name, bool expected)
            {
                var item = Activator.CreateInstance(itemType, flags, null, new object[] { IntPtr.Zero }, null);
                entityType.GetProperty("Path").SetValue(item, path);
                var baseComponent = new Base(IntPtr.Zero);
                typeof(Base).GetProperty("BaseItemName").SetValue(baseComponent, name);
                var components = (System.Collections.Concurrent.ConcurrentDictionary<string, ExileCenter.RemoteObjects.Components.ComponentBase>)entityType.GetField("componentCache", flags).GetValue(item);
                components["Base"] = baseComponent;
                ImGuiNET.ImGui.NewFrame();
                int before = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size;
                highlight.Invoke(highlightPlugin, new object[] { item, new Vector2(200, 40), new Vector2(52) });
                Check((ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size > before) == expected, "Tablet overlay only on real tablets: " + path);
                ImGuiNET.ImGui.EndFrame();
            }
            CheckItemOverlay("Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Infuseur antique", false);
            CheckItemOverlay("Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Ancient Infuser", false);
            CheckItemOverlay("Metadata/Items/Currency/Tablet", "Tablet", false);
            CheckItemOverlay("Metadata/Items/TowerAugment/GenericAugment", "Tablette irradiée", true);
            var orphanItem = Activator.CreateInstance(itemType, flags, null, new object[] { IntPtr.Zero }, null);
            entityType.GetProperty("Path").SetValue(orphanItem, "Metadata/Items/TowerAugment/GenericAugment");
            var orphanBase = new Base(IntPtr.Zero);
            typeof(Base).GetProperty("BaseItemName").SetValue(orphanBase, "Tablette irradiée");
            var orphanMods = new Mods(IntPtr.Zero);
            orphanMods.ExplicitMods.Add(("TowerMonsterEffectiveness", (15f, 0f)));
            var orphanComponents = (System.Collections.Concurrent.ConcurrentDictionary<string, ExileCenter.RemoteObjects.Components.ComponentBase>)entityType.GetField("componentCache", flags).GetValue(orphanItem);
            orphanComponents["Base"] = orphanBase; orphanComponents["Mods"] = orphanMods;
            var orphanProfile = new TabletProfile { ModRanks = new() { ["TowerMonsterEffectiveness"] = "S" } };
            highlightPlugin.Settings.TabletProfiles["Irradiated Tablet"] = orphanProfile;
            (Vector2, Vector2, uint)[] OrphanOverlay(bool advanced)
            {
                orphanProfile.UseMetaFilters = advanced;
                ImGuiNET.ImGui.NewFrame();
                highlight.Invoke(highlightPlugin, new object[] { orphanItem, new Vector2(200, 40), new Vector2(52) });
                var buffer = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
                var rendered = Enumerable.Range(0, buffer.Size).Select(i => (buffer[i].pos, buffer[i].uv, buffer[i].col)).ToArray();
                ImGuiNET.ImGui.EndFrame(); return rendered;
            }
            var basicOverlay = OrphanOverlay(false);
            Check(basicOverlay.Length > 0 && OrphanOverlay(true).SequenceEqual(basicOverlay), "Enabled advanced filter with no profile renders the normal rank without a question mark");
            var uiRoot = Path.Combine(Directory.GetCurrentDirectory(), "tablet-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uiRoot);
            var uiPlugin = new StashUtilityCore { DllDirectory = uiRoot };
            var uiProfile = new TabletProfile();
            uiPlugin.Settings.TabletProfiles["Irradiated Tablet"] = uiProfile;
            var rankCheckbox = typeof(StashUtilityCore).GetMethod("DrawTabletRankCheckbox", flags);
            Vector2 RankFrame(int rank, Vector2 mouse, bool down)
            {
                var io = ImGuiNET.ImGui.GetIO(); io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(40, 40)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(320, 150));
                ImGuiNET.ImGui.Begin("rank fixture");
                rankCheckbox.Invoke(uiPlugin, new object[] { "TowerMonsterEffectiveness", rank, uiProfile.ModRanks, uiProfile.GoodModPatterns, uiProfile.BadModPatterns, uiProfile.GodModPatterns, true });
                var center = (ImGuiNET.ImGui.GetItemRectMin() + ImGuiNET.ImGui.GetItemRectMax()) / 2;
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render(); return center;
            }
            foreach (int rank in new[] { 5, 6 })
            {
                var center = RankFrame(rank, new Vector2(-100), false);
                RankFrame(rank, center, false); RankFrame(rank, center, true); RankFrame(rank, center, false);
                Check(uiProfile.ModRanks["TowerMonsterEffectiveness"] == (rank == 5 ? "E" : "F"), "Actual E/F checkbox updates stored rank");
            }
            var persisted = JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(uiRoot, "config", "settings.txt")));
            Check(persisted.TabletProfiles["Irradiated Tablet"].ModRanks["TowerMonsterEffectiveness"] == "F", "UI F selection persists to disk");
            var badgeMods = new Mods(IntPtr.Zero);
            foreach (string label in new[] { "E1", "E2", "E3", "F1" })
                badgeMods.ExplicitMods.Add((ModDatabase.TabletMeta.FindSubRank("Irradiated Tablet", label).GameModIds[0], (10000f, 10000f)));
            uiProfile.ModRanks.Clear();
            uiProfile.GoodModPatterns.Clear(); uiProfile.GodModPatterns.Clear();
            foreach (string label in new[] { "E1", "E2", "E3", "F1" })
            {
                string id = ModDatabase.TabletMeta.FindSubRank("Irradiated Tablet", label).GameModIds[0];
                uiProfile.ModRanks[id] = label[..1]; uiProfile.GoodModPatterns.Add(id);
            }
            var badgeProfile = new TabletFilterProfile { TabletType = "Irradiated Tablet", Rule = new() { Kind = TabletRuleKind.RankExact, Rank = "E" } };
            uiProfile.UseMetaFilters = true; uiProfile.ActiveMetaProfileId = badgeProfile.Id; uiProfile.MetaProfiles.Add(badgeProfile);
            uiPlugin.Settings.ShowTabletModBorder = false; uiPlugin.Settings.ShowRarityBorder = false; uiPlugin.Settings.HideTabletHighlightsOnItemHover = false;
            ImGuiNET.ImGui.GetIO().AddMousePosEvent(-100, -100);
            ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawMetaTabletHighlight", flags).Invoke(uiPlugin, new object[] { "Fixture", "Irradiated Tablet", new Vector2(200,40), new Vector2(52), badgeMods });
            var vertices = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            var actualBadge = Enumerable.Range(0, vertices.Size).Select(i => (vertices[i].pos, vertices[i].uv, vertices[i].col)).ToArray();
            ImGuiNET.ImGui.EndFrame(); ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawTabletRankBadge", flags | BindingFlags.Static).Invoke(null, new object[] { new Vector2(200,40), new Vector2(52), "E3F", 1f, Math.Max(2f, uiPlugin.Settings.BorderMargin) });
            vertices = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            var expectedBadge = Enumerable.Range(0, vertices.Size).Select(i => (vertices[i].pos, vertices[i].uv, vertices[i].col)).ToArray();
            Check(actualBadge.Length > 0 && actualBadge.SequenceEqual(expectedBadge), "Overlay shows the exact compact composition; priorities and filter status remain separate");
            ImGuiNET.ImGui.EndFrame();
            uiProfile.ModRanks[badgeMods.ExplicitMods[0].name] = "S";
            uiPlugin.Settings.ShowTabletModBorder = true;
            ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawMetaTabletHighlight", flags).Invoke(uiPlugin, new object[] { "Fixture", "Irradiated Tablet", new Vector2(200,40), new Vector2(52), badgeMods });
            vertices = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            uint gold = ExileCenter.Utils.ImGuiHelper.Color(new Vector4(.92f, .76f, .32f, 1));
            Check(Enumerable.Range(0, vertices.Size).Any(i => vertices[i].col == gold), "AND / OR border uses the best user rank rather than a yellow status override");
            ImGuiNET.ImGui.EndFrame();
            string absent = ModDatabase.AllTabletMods.First(m => m.TabletTypes.Contains("Irradiated Tablet") &&
                !uiProfile.ModRanks.ContainsKey(m.Id)).Id;
            uiProfile.ModRanks[absent] = "A";
            uiProfile.RequireAllRankedMods = true;
            uiPlugin.Settings.DisableBadTabletHighlight = true;
            ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawMetaTabletHighlight", flags).Invoke(uiPlugin, new object[] { "Fixture", "Irradiated Tablet", new Vector2(200,40), new Vector2(52), badgeMods });
            Check(ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size == 0, "ET suppresses a nonmatching tablet even when its advanced profile accepts it");
            ImGuiNET.ImGui.EndFrame();
            uiProfile.RequireAllRankedMods = false;
            ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawMetaTabletHighlight", flags).Invoke(uiPlugin, new object[] { "Fixture", "Irradiated Tablet", new Vector2(200,40), new Vector2(52), badgeMods });
            Check(ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size > 0, "OU restores the highlight for the same tablet with only some selected mods");
            ImGuiNET.ImGui.EndFrame();
            var parentData = new GameOffsets.Objects.UiElement.UiElementBaseOffset
            {
                Self = parent, Flags = 1u << 11, LocalScaleMultiplier = 1,
                RelativePosition = new GameOffsets.Natives.StdTuple2D<float>(20, 20),
                UnscaledSize = new GameOffsets.Natives.StdTuple2D<float>(40, 40),
                ChildrensPtr = new GameOffsets.Natives.StdVector { First = children, Last = children + IntPtr.Size, End = children + IntPtr.Size }
            };
            var childData = new GameOffsets.Objects.UiElement.UiElementBaseOffset
            {
                Self = child, ParentPtr = parent, Flags = 1u << 11, LocalScaleMultiplier = 1,
                RelativePosition = new GameOffsets.Natives.StdTuple2D<float>(60, 0),
                UnscaledSize = new GameOffsets.Natives.StdTuple2D<float>(30, 30)
            };
            Marshal.StructureToPtr(parentData, parent, false);
            Marshal.StructureToPtr(childData, child, false);
            Marshal.WriteIntPtr(children, child);
            var plugin = new StashUtilityCore();
            typeof(StashUtilityCore).GetField("handleObj", flags).SetValue(plugin, reader);
            var collect = typeof(StashUtilityCore).GetMethod("CollectTooltipOcclusions", flags);
            var overlaps = typeof(StashUtilityCore).GetMethod("IsTabletOccluded", flags);
            bool Covered(Vector2 position) => (bool)overlaps.Invoke(plugin, new object[] { position, new Vector2(5) });
            collect.Invoke(plugin, new object[] { parent, 0, 128 });
            Check(Covered(new Vector2(25, 25)), "Real tooltip parent rectangle covered");
            Check(Covered(new Vector2(85, 25)), "Tooltip text outside parent bounds also covered");
            Check(!Covered(new Vector2(150, 150)), "Unrelated tablets remain visible");
            ((IList)typeof(StashUtilityCore).GetField("tabletOcclusions", flags).GetValue(plugin)).Clear();
            childData.Flags = 0;
            Marshal.StructureToPtr(childData, child, false);
            collect.Invoke(plugin, new object[] { parent, 0, 128 });
            Check(!Covered(new Vector2(85, 25)), "Hidden text child does not mask tablets");

            var queue = (IList)typeof(StashUtilityCore).GetField("pendingItemHighlights", flags).GetValue(plugin);
            var flush = typeof(StashUtilityCore).GetMethod("FlushItemHighlights", flags);
            var suppressed = typeof(StashUtilityCore).GetMethod("TabletHighlightsSuppressed", flags);
            var toggle = typeof(StashUtilityCore).GetMethod("ToggleTabletHighlights", flags);
            bool Hidden() => (bool)suppressed.Invoke(plugin, null);
            typeof(StashUtilityCore).GetField("tabletTooltipVisible", flags).SetValue(plugin, false);
            void Hover(Vector2 mouse)
            {
                ImGuiNET.ImGui.GetIO().MousePos = mouse;
                ImGuiNET.ImGui.GetIO().WantCaptureMouse = false;
                queue.Add(((ExileCenter.RemoteObjects.States.InGameStateObjects.Item)null, new Vector2(200, 40), new Vector2(30)));
                flush.Invoke(plugin, null);
            }
            Check(plugin.Settings.HideTabletHighlightsOnItemHover, "Automatic hiding enabled by default");
            Check(JsonConvert.DeserializeObject<StashUtilitySettings>("{}").HideTabletHighlightsOnItemHover, "Existing settings receive automatic hiding default");
            Hover(new Vector2(205, 45)); Check(Hidden(), "Hover hides all tablet indications");
            Hover(new Vector2(100, 100)); Check(!Hidden(), "Leaving item restores indications immediately");
            toggle.Invoke(plugin, null); Hover(new Vector2(100, 100)); Check(Hidden(), "Manual hiding survives leaving item");
            plugin.Settings.HideTabletHighlightsOnItemHover = false;
            Hover(new Vector2(205, 45)); Check(Hidden(), "Manual hiding takes priority over disabling automatic hiding");
            toggle.Invoke(plugin, null); Hover(new Vector2(205, 45)); Check(!Hidden(), "Show restores indications with automatic hiding disabled");
            Check(!JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(plugin.Settings)).HideTabletHighlightsOnItemHover, "Automatic preference persists through settings reload");
            plugin.Settings.HideTabletHighlightsOnItemHover = true;
            Hover(new Vector2(230, 45)); Check(!Hidden(), "Pointer on right item edge is outside hover area");
            typeof(StashUtilityCore).GetField("tabletTooltipVisible", flags).SetValue(plugin, true);
            Check(Hidden(), "Detected game tooltip hides all tablet indications");
            plugin.Settings.HideTabletHighlightsOnItemHover = false;
            Check(!Hidden(), "Disabling automatic mode overrides tooltip hiding");
            Check(queue.Count == 0, "Hover checks do not retain stale queued items");
            CheckCompositionUi(root, previewDirectory, pixels, width, height);
        }
        finally
        {
            handle.SetValue(process, previousReader);
            ((IDisposable)reader).Dispose();
            Marshal.FreeHGlobal(parent); Marshal.FreeHGlobal(child); Marshal.FreeHGlobal(children);
            ImGuiNET.ImGui.DestroyContext(context);
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    private static void CheckCompositionUi(string root, string previewDirectory, IntPtr atlas, int atlasWidth, int atlasHeight)
    {
        int before = checks;
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var io=ImGuiNET.ImGui.GetIO();io.DisplaySize=new Vector2(1280,960);
        var previousLanguage=ExileCenter.Core.GHSettings.UiLanguage;
        ExileCenter.Core.GHSettings.UiLanguage=ExileCenter.Localization.OverlayLanguage.French;
        var colors=(Vector4[])typeof(StashUtilityCore).GetField("TabletModRankColors",flags).GetValue(null);
        var badge=typeof(StashUtilityCore).GetMethod("DrawTabletCompositionBadge",flags);
        var marker=typeof(StashUtilityCore).GetMethod("DrawTabletFilterMarker",flags);
        var atlasBytes=new byte[atlasWidth*atlasHeight*4];Marshal.Copy(atlas,atlasBytes,0,atlasBytes.Length);
        foreach(float size in new[]{32f,40f,52f,104f})foreach(var composition in TabletComposition.Generate())
        {
            string grade=composition.Ranks[0];
            var classification=new TabletClassification(grade,0,composition.Label,colors["SABCDEF".IndexOf(grade)],0);
            ImGuiNET.ImGui.NewFrame();
            badge.Invoke(null,new object[]{new Vector2(100,60),new Vector2(size),classification,size/52f,2f});
            var vertices=ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            Check(vertices.Size>0,"Every compact label is drawn in a "+size+"px cell: "+composition.Label);
            Check(Enumerable.Range(0,vertices.Size).All(i=>vertices[i].pos.X>=99.99f&&vertices[i].pos.X<=100+size+.01f&&vertices[i].pos.Y>=59.99f&&vertices[i].pos.Y<=60+size+.01f),"Badge stays inside its cell: "+composition.Label);
            int start=vertices.Size;
            marker.Invoke(null,new object[]{new Vector2(100,60),new Vector2(size),"!",size/52f});
            vertices=ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            Check(Enumerable.Range(start,vertices.Size-start).All(i=>vertices[i].pos.X<100+size*.5f),"Filter marker leaves the quantity's right half clear");
            ImGuiNET.ImGui.EndFrame();
        }
        Console.WriteLine("VALIDATION composition UI: all 840 cell layouts and filter-marker bounds passed.");
        ImGuiNET.ImGui.NewFrame();
        var draw=ImGuiNET.ImGui.GetBackgroundDrawList();draw.AddText(new Vector2(20,16),0xFFFFFFFF,"210 compositions theoriques - pas une liste de tablettes realisables");
        int slot=0;
        foreach(var composition in TabletComposition.Generate())
        {
            var point=new Vector2(20+slot%21*58,52+slot/21*58);
            draw.AddRectFilled(point,point+new Vector2(52),0xFF29231E,3);
            var classification=new TabletClassification(composition.Ranks[0],0,composition.Label,colors["SABCDEF".IndexOf(composition.Ranks[0])],0);
            badge.Invoke(null,new object[]{point,new Vector2(52),classification,1f,2f});slot++;
        }
        ImGuiNET.ImGui.Render();
        if(previewDirectory!=null){Directory.CreateDirectory(previewDirectory);SaveUiPreview(Path.Combine(previewDirectory,"tablet-compositions-210.png"),atlasBytes,atlasWidth,atlasHeight);}

        var plugin=new StashUtilityCore{DllDirectory=Path.Combine(root,"Plugins","StashUtility")};
        var profile=new TabletProfile();plugin.Settings.TabletProfiles["Irradiated Tablet"]=profile;
        var available=ModDatabase.TabletMeta.ForType("Irradiated Tablet").ToArray();
        var four=available.Where(m=>m.Affix=="prefix").Take(2).Concat(available.Where(m=>m.Affix=="suffix").Take(2)).ToArray();
        var mods=new Mods(IntPtr.Zero);
        for(int i=0;i<4;i++){profile.ModRanks[four[i].GameModIds[0]]=i<2?"S":"A";mods.ExplicitMods.Add((four[i].GameModIds[0],(10000f,10000f)));}
        var classify=typeof(StashUtilityCore).GetMethod("ClassifyTabletMods",flags);
        TabletClassification Classify()=>(TabletClassification)classify.Invoke(null,new object[]{mods,profile,"Irradiated Tablet"});
        foreach(var composition in TabletComposition.Generate())
        {
            for(int i=0;i<4;i++)profile.ModRanks[four[i].GameModIds[0]]=composition.Ranks[i];
            Check(Classify().RankColor==colors["SABCDEF".IndexOf(composition.Ranks[0])],"Runtime color follows strongest effective grade: "+composition.Label);
        }
        for(int i=0;i<4;i++)profile.ModRanks[four[i].GameModIds[0]]=i<2?"S":"A";
        Check(Classify().RankLabel=="S2A2"&&Classify().RankIndex==0,"Runtime classifier supplies composition independently of disabled positions");
        profile.ModRanks[four[0].GameModIds[0]]="F";
        Check(Classify().RankLabel=="SA2F","Runtime classifier updates the same tablet immediately after a preference edit");
        profile.ModRanks.Clear();
        Check(!Classify().IsRanked&&Classify().RankLabel=="N/C","No selected modifiers means N/C despite known reference grades");
        Check(Classify().RankColor==new Vector4(.65f,.69f,.75f,1f),"Unselected reference grades leave the badge neutral");
        var referenceS=available.First(m=>m.Rank=="S"&&m.Id!=four[0].Id);
        var savedMods=mods.ExplicitMods.ToArray();
        mods.ExplicitMods.Clear();
        mods.ExplicitMods.Add((referenceS.GameModIds[0],(10000f,10000f)));
        mods.ExplicitMods.Add((four[0].GameModIds[0],(10000f,10000f)));
        profile.ModRanks[four[0].GameModIds[0]]="A";
        Check(Classify().Rank=="A"&&Classify().RankLabel=="A"&&Classify().RankColor==colors[1],"Reference S never overrides matching selected A in badge or color");
        mods.ExplicitMods.Clear();mods.ExplicitMods.AddRange(savedMods);
        profile.ModRanks[four[0].GameModIds[0]]="A";
        var originalFourth=mods.ExplicitMods[3];mods.ExplicitMods[3]=("unknown",(0f,0f));
        Check(Classify().RankLabel=="A"&&Classify().Uncertain,"Unknown unrelated modifiers retain the confirmed selected A with uncertainty marker");
        mods.ExplicitMods[3]=originalFourth;
        ImGuiNET.ImGui.NewFrame();
        var red=new TabletClassification("A",0,"A",colors[1],1);
        badge.Invoke(null,new object[]{new Vector2(100,60),new Vector2(52),red,1f,2f});
        var buffer=ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
        Check(Enumerable.Range(0,buffer.Size).Any(i=>buffer[i].col==ExileCenter.Utils.ImGuiHelper.Color(colors[1]))&&
            !Enumerable.Range(0,buffer.Size).Any(i=>buffer[i].col==ExileCenter.Utils.ImGuiHelper.Color(colors[0])),"Selected A badge renders red without reference S gold");
        ImGuiNET.ImGui.EndFrame();
        var screenshotMods=new Mods(IntPtr.Zero);
        screenshotMods.ExplicitMods.AddRange(new[]{("TowerMapDroppedMapsIncrease",(32f,0f)),("TowerMonsterEffectiveness",(15f,0f)),
            ("TowerRarePackIncrease",(32f,0f)),("TowerAdditionalSpiritChance",(80f,0f))});
        var screenshotProfile=new TabletProfile();
        foreach(var choice in new[]{("TowerPackSizeIncrease","B"),("TowerMonsterEffectiveness","A"),("TowerMapAdditionalModifier","S"),("TowerAdditionalStrongboxChance","E")})
            screenshotProfile.ModRanks[choice.Item1]=choice.Item2;
        TabletClassification Screenshot()=>(TabletClassification)classify.Invoke(null,new object[]{screenshotMods,screenshotProfile,"Irradiated Tablet"});
        Check(Screenshot().RankLabel=="A"&&Screenshot().RankColor==colors[1],"Screenshot 5: four configured mods, only effectiveness matches, red A");
        screenshotProfile.ModRanks.Clear();screenshotProfile.ModRanks["TowerMonsterEffectiveness"]="S";
        Check(Screenshot().RankLabel=="S"&&Screenshot().RankColor==colors[0],"Screenshot 2: single effectiveness S rule yields S when present");
        screenshotProfile.ModRequiredMinRolls["TowerMonsterEffectiveness"]=16;
        Check(Screenshot().RankLabel=="N/C"&&!Screenshot().IsRanked&&Screenshot().RankColor==new Vector4(.65f,.69f,.75f,1f),"Single rule below minimum: neutral N/C despite reference S");
        screenshotProfile.ModRequiredMinRolls.Clear();screenshotMods.ExplicitMods.RemoveAt(1);
        Check(Screenshot().RankLabel=="N/C"&&!Screenshot().IsRanked,"Single rule absent: reference S cannot create a badge match");
        var itemType=typeof(ExileCenter.RemoteObjects.States.InGameStateObjects.Item);var entityType=itemType.BaseType;
        var item=Activator.CreateInstance(itemType,flags,null,new object[]{IntPtr.Zero},null);
        entityType.GetProperty("Path").SetValue(item,"Metadata/Items/TowerAugment/GenericAugment");
        var baseComponent=new Base(IntPtr.Zero);typeof(Base).GetProperty("BaseItemName").SetValue(baseComponent,"Tablette de validation");
        var components=(System.Collections.Concurrent.ConcurrentDictionary<string,ExileCenter.RemoteObjects.Components.ComponentBase>)entityType.GetField("componentCache",flags).GetValue(item);
        components["Base"]=baseComponent;components["Mods"]=mods;
        var highlight=typeof(StashUtilityCore).GetMethod("EvaluateAndHighlightItem",flags);
        typeof(StashUtilityCore).GetField("tabletItemHovered",flags).SetValue(plugin,true);
        typeof(StashUtilityCore).GetField("tabletTooltipVisible",flags).SetValue(plugin,true);
        string savedSettings=JsonConvert.SerializeObject(plugin.Settings);
        int hoverBackgroundVertices=0;
        for(int frame=0;frame<3;frame++)
        {
            io.AddMousePosEvent(110,90);ImGuiNET.ImGui.NewFrame();io.WantCaptureMouse=false;
            highlight.Invoke(plugin,new object[]{item,new Vector2(100,80),new Vector2(52)});
            hoverBackgroundVertices=ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size;
            ImGuiNET.ImGui.Render();
        }
        Check(ImGuiNET.ImGui.GetDrawData().TotalVtxCount>0,"Hover tooltip renders even while automatic hiding protects the item quantity");
        var tooltipData=ImGuiNET.ImGui.GetDrawData();
        var tooltipXs=Enumerable.Range(0,tooltipData.CmdListsCount).SelectMany(i=>Enumerable.Range(0,tooltipData.CmdLists[i].VtxBuffer.Size).Select(j=>tooltipData.CmdLists[i].VtxBuffer[j].pos.X)).ToArray();
        Check(tooltipData.TotalVtxCount>500&&tooltipXs.Max()-tooltipXs.Min()>200&&tooltipXs.Max()-tooltipXs.Min()<500,"Tooltip has a readable text area, not an empty narrow auto-size column");
        Check(hoverBackgroundVertices==0,"Hover tooltip does not restore the hidden item badge or cover its quantity");
        Check(((TabletObservation)typeof(StashUtilityCore).GetField("lastMetaObservation",flags).GetValue(plugin)).Mods.Count==4,"Hover shares the same four observed modifiers with Analysis");
        if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"tablet-composition-tooltip.png"),atlasBytes,atlasWidth,atlasHeight);
        Check(JsonConvert.SerializeObject(plugin.Settings)==savedSettings,"Rendering composition and tooltips never changes saved profiles");
        foreach(var point in new[]{new Vector2(8,8),new Vector2(1200,8),new Vector2(8,880),new Vector2(1200,880)})
        {
            for(int frame=0;frame<2;frame++)
            {
                io.AddMousePosEvent(point.X+8,point.Y+8);ImGuiNET.ImGui.NewFrame();io.WantCaptureMouse=false;
                highlight.Invoke(plugin,new object[]{item,point,new Vector2(52)});ImGuiNET.ImGui.Render();
            }
            var data=ImGuiNET.ImGui.GetDrawData();
            var points=Enumerable.Range(0,data.CmdListsCount).SelectMany(i=>Enumerable.Range(0,data.CmdLists[i].VtxBuffer.Size).Select(j=>data.CmdLists[i].VtxBuffer[j].pos)).ToArray();
            Check(data.TotalVtxCount>500&&points.All(p=>p.X>=-2&&p.Y>=-2&&p.X<=io.DisplaySize.X+2&&p.Y<=io.DisplaySize.Y+2),"Tooltip text fits the viewport near each corner");
        }
        plugin.Settings.TabletHelpTooltips=false;
        io.AddMousePosEvent(110,90);ImGuiNET.ImGui.NewFrame();io.WantCaptureMouse=false;
        highlight.Invoke(plugin,new object[]{item,new Vector2(100,80),new Vector2(52)});ImGuiNET.ImGui.Render();
        Check(ImGuiNET.ImGui.GetDrawData().TotalVtxCount==0,"Existing help switch disables the new tooltip");
        plugin.Settings.TabletHelpTooltips=true;
        typeof(StashUtilityCore).GetField("tabletHighlightsHiddenManually",flags).SetValue(plugin,true);
        ImGuiNET.ImGui.NewFrame();io.WantCaptureMouse=false;
        highlight.Invoke(plugin,new object[]{item,new Vector2(100,80),new Vector2(52)});ImGuiNET.ImGui.Render();
        Check(ImGuiNET.ImGui.GetDrawData().TotalVtxCount==0,"Manual hiding remains authoritative over badges and tooltips");
        typeof(StashUtilityCore).GetField("tabletHighlightsHiddenManually",flags).SetValue(plugin,false);
        typeof(StashUtilityCore).GetField("tabletItemHovered",flags).SetValue(plugin,false);
        typeof(StashUtilityCore).GetField("tabletTooltipVisible",flags).SetValue(plugin,false);
        plugin.Settings.HideTabletHighlightsOnItemHover=false;
        var advanced=new TabletFilterProfile{TabletType="Irradiated Tablet",Rule=new(){Kind=TabletRuleKind.RankExact,Rank="A"},OnMatch=TabletFilterAction.Signal};
        profile.MetaProfiles.Add(advanced);profile.ActiveMetaProfileId=advanced.Id;profile.UseMetaFilters=true;
        io.AddMousePosEvent(900,800);ImGuiNET.ImGui.NewFrame();io.WantCaptureMouse=false;
        highlight.Invoke(plugin,new object[]{item,new Vector2(100,80),new Vector2(52)});
        var advancedVertices=ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
        uint amber=ExileCenter.Utils.ImGuiHelper.Color(new Vector4(1,.73f,.3f,1));
        Check(Enumerable.Range(0,advancedVertices.Size).Any(i=>advancedVertices[i].col==amber),"Advanced Signal action keeps its separate visible alert marker");
        ImGuiNET.ImGui.EndFrame();
        ExileCenter.Core.GHSettings.UiLanguage=previousLanguage;
        Console.WriteLine($"VALIDATION composition UI: 210 labels at 32/40/52/104px; bounds, quantity space, colors, live edits and hover/help switches: {checks-before} checks passed.");
    }

    private static unsafe void CheckFontReload(string root)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var context = ImGuiNET.ImGui.CreateContext();
        IntPtr sharedContext = IntPtr.Zero;
        var previousOverlay = ExileCenter.Core.Overlay;
        var overlayProperty = typeof(ExileCenter.Core).GetProperty("Overlay");
        var overlay = (ExileCenter.GameOverlay)RuntimeHelpers.GetUninitializedObject(typeof(ExileCenter.GameOverlay));
        GC.SuppressFinalize(overlay);
        var queueField = typeof(ClickableTransparentOverlay.Overlay).GetField("fontUpdates", flags);
        var queue = Activator.CreateInstance(queueField.FieldType);
        queueField.SetValue(overlay, queue);
        var settings = ExileCenter.Core.GHSettings;
        var oldPath = settings.FontPathName; var oldRange = settings.FontCustomGlyphRange;
        var oldLanguage = settings.FontLanguage; var oldUniversal = settings.UniversalFont;
        var theme = typeof(ExileCenter.Ui.ImGuiTheme);
        var frameLoaded = theme.GetField("frameLoaded", flags); var frameTexture = theme.GetField("frameTexture", flags);
        var oldLoaded = frameLoaded.GetValue(null); var oldTexture = frameTexture.GetValue(null);
        var heading = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Utils.UniversalFont");
        var atlas = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "Plugins", "Atlas", "Atlas.dll"))
            .GetType("Atlas.UniversalFont");
        bool HasHeading() => (bool)heading.GetMethod("TryGetHeadingFont", flags).Invoke(null, new object[] { default(ImGuiNET.ImFontPtr) });
        void BuildFonts()
        {
            var fonts = ImGuiNET.ImGui.GetIO().Fonts;
            fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytes);
            fonts.SetTexID((IntPtr)1);
            Check(width > 0 && height > 0 && width <= 16384 && height <= 16384, "Reloaded font atlas fits the renderer");
            fonts.ClearTexData();
        }
        void DrainFontUpdates()
        {
            foreach (Delegate update in ((IEnumerable)queue).Cast<Delegate>().ToArray())
            {
                ImGuiNET.ImGui.GetIO().Fonts.Clear();
                var config = ImGuiNET.ImGuiNative.ImFontConfig_ImFontConfig();
                try { update.DynamicInvoke(Pointer.Box(config, typeof(ImGuiNET.ImFontConfig*))); BuildFonts(); }
                finally { ImGuiNET.ImGuiNative.ImFontConfig_destroy(config); }
            }
            queue.GetType().GetMethod("Clear").Invoke(queue, null);
        }
        void DrawTitle(IntPtr target)
        {
            ImGuiNET.ImGui.SetCurrentContext(target);
            var io = ImGuiNET.ImGui.GetIO(); io.NativePtr->IniFilename = null; io.NativePtr->FontDefault = null;
            io.DisplaySize = new Vector2(1100, 800); io.DeltaTime = 1f / 60;
            ImGuiNET.ImGui.NewFrame();
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(1000, 700));
            bool open = true;
            var args = new object[] { "Font lifecycle###font-reload", open, ImGuiNET.ImGuiWindowFlags.NoTitleBar };
            theme.GetMethod("BeginSettingsWindow", flags).Invoke(null, args);
            Check(ImGuiNET.ImGui.GetFont().Scale > 0 && ImGuiNET.ImGui.GetFontSize() > 0, "Title rendering restores a valid body font");
            ImGuiNET.ImGui.TextUnformatted("Font reload check"); ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render();
        }
        try
        {
            overlayProperty.SetValue(null, overlay);
            frameLoaded.SetValue(null, true); frameTexture.SetValue(null, (IntPtr)2);
            settings.FontPathName = Path.Combine(root, "fonts", "DejaVuSans.ttf");
            settings.FontLanguage = ClickableTransparentOverlay.FontGlyphRangeType.English;
            settings.FontCustomGlyphRange = "";
            heading.GetMethod("ApplyConfigured", flags).Invoke(null, null); DrainFontUpdates();
            Check(HasHeading(), "Configured startup loads the separate title font");
            sharedContext = ImGuiNET.ImGui.CreateContext(ImGuiNET.ImGui.GetIO().Fonts);
            DrawTitle(context); DrawTitle(sharedContext); ImGuiNET.ImGui.SetCurrentContext(context);
            settings.UniversalFont = true;
            heading.GetMethod("ApplyFromSettings", flags).Invoke(null, null); DrainFontUpdates();
            atlas.GetMethod("Apply", flags).Invoke(null, new object[] { Path.Combine(root, "Plugins", "Atlas") }); DrainFontUpdates();
            Check(HasHeading(), "Atlas startup reload recreates the title font after core initialization");
            DrawTitle(context); DrawTitle(sharedContext); ImGuiNET.ImGui.SetCurrentContext(context);
            atlas.GetMethod("Restore", flags).Invoke(null, null); DrainFontUpdates();
            Check(HasHeading(), "Disabling Atlas preserves the title font");
            // Reproduce an uncoordinated reload without dereferencing the retired pointer.
            ImGuiNET.ImGui.GetIO().Fonts.Clear(); ImGuiNET.ImGui.GetIO().Fonts.AddFontDefault(); BuildFonts();
            Check(!HasHeading(), "A font retired by another reload is rejected before any native access");
            DrawTitle(context); DrawTitle(sharedContext); DrawTitle(context);
            Console.WriteLine("VALIDATION fonts: real core/Atlas reload callbacks, shared contexts and retired-font fallback passed.");
        }
        finally
        {
            ImGuiNET.ImGui.SetCurrentContext(context);
            if (sharedContext != IntPtr.Zero) ImGuiNET.ImGui.DestroyContext(sharedContext);
            ImGuiNET.ImGui.DestroyContext(context);
            overlayProperty.SetValue(null, previousOverlay);
            settings.FontPathName = oldPath; settings.FontCustomGlyphRange = oldRange;
            settings.FontLanguage = oldLanguage; settings.UniversalFont = oldUniversal;
            frameLoaded.SetValue(null, oldLoaded); frameTexture.SetValue(null, oldTexture);
        }
    }

    private static void CheckTabletSettingsUi(string root, string previewDirectory)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var context = ImGuiNET.ImGui.CreateContext();
        string previousDirectory = Directory.GetCurrentDirectory();
        var previousLanguage = ExileCenter.Core.GHSettings.UiLanguage;
        var isolated = Path.Combine(Path.GetDirectoryName(typeof(TabletRankingCheck).Assembly.Location), "tablet-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(isolated, "Localization"));
        foreach (string language in new[] { "fr-FR", "en-US" })
            File.Copy(Path.Combine(root, "Plugins", "StashUtility", "Localization", language + ".json"), Path.Combine(isolated, "Localization", language + ".json"));
        try
        {
            Directory.SetCurrentDirectory(isolated);
            ExileCenter.Core.GHSettings.UiLanguage = ExileCenter.Localization.OverlayLanguage.French;
            var io = ImGuiNET.ImGui.GetIO();
            unsafe { io.NativePtr->IniFilename = null; }
            io.DisplaySize = new Vector2(1280, 960); io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            var headingFonts = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Utils.UniversalFont");
            headingFonts.GetMethod("AddHeadingFont", flags | BindingFlags.Public).Invoke(null, new object[] { io.Fonts });
            io.Fonts.GetTexDataAsRGBA32(out IntPtr atlas, out int atlasWidth, out int atlasHeight, out int bytes);
            Check(io.Fonts.Fonts.Size == 2 && io.Fonts.Fonts[0].FontSize == 13,
                "The serif heading font is separate from the unchanged body font");
            io.Fonts.SetTexID(new IntPtr(1));
            var pixels = new byte[atlasWidth * atlasHeight * bytes]; Marshal.Copy(atlas, pixels, 0, pixels.Length);
            typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("Apply", flags).Invoke(null, null);
            Vector4[] Colors() => Enumerable.Range(0, (int)ImGuiNET.ImGuiCol.COUNT).Select(i => ImGuiNET.ImGui.GetStyle().Colors[i]).ToArray();
            var original = Colors();
            var decorate=typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("DrawWindowBackground",flags);
            foreach(var panelFlags in new[]{ImGuiNET.ImGuiWindowFlags.None,ImGuiNET.ImGuiWindowFlags.NoBackground,ImGuiNET.ImGuiWindowFlags.NoInputs})
            {
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(380,220));
                ImGuiNET.ImGui.Begin("Cadre de pierre et bronze##poe2-panel",panelFlags);
                var drawList=ImGuiNET.ImGui.GetWindowDrawList();int beforeVertices=drawList.VtxBuffer.Size;
                var beforeClip=drawList.CmdBuffer[drawList.CmdBuffer.Size-1].ClipRect;
                var beforeCursor=ImGuiNET.ImGui.GetCursorPos();
                decorate.Invoke(null,new object[]{panelFlags,.88f});
                Check(panelFlags==ImGuiNET.ImGuiWindowFlags.None?drawList.VtxBuffer.Size>beforeVertices:drawList.VtxBuffer.Size==beforeVertices,
                    "Opaque panels receive metalwork while transparent and input-free overlays receive no decoration: "+panelFlags);
                Check(ImGuiNET.ImGui.GetCursorPos()==beforeCursor&&drawList.CmdBuffer[drawList.CmdBuffer.Size-1].ClipRect==beforeClip,
                    "Panel decoration preserves control layout and clipping: "+panelFlags);
                ImGuiNET.ImGui.TextUnformatted("ExileCenter - Configuration");
                bool sampleEnabled=true;ImGuiNET.ImGui.Checkbox("Activer le filtre",ref sampleEnabled);
                ImGuiNET.ImGui.Button("Appliquer",new Vector2(-1,0));
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                if(previewDirectory!=null&&panelFlags==ImGuiNET.ImGuiWindowFlags.None)
                {Directory.CreateDirectory(previewDirectory);SaveUiPreview(Path.Combine(previewDirectory,"poe2-panel.png"),pixels,atlasWidth,atlasHeight);}
            }
            var names = (string[])typeof(ExileCenter.Ui.ImGuiTheme).GetField("PluginThemeNames", flags).GetValue(null);
            using(var frameStream=typeof(ExileCenter.Ui.ImGuiTheme).Assembly.GetManifestResourceStream("ExileCenter.Data.Poe2Frame.png"))
            using(var frameImage=SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(frameStream))
            {
                framePreviewWidth=frameImage.Width;framePreviewHeight=frameImage.Height;
                framePreviewPixels=new SixLabors.ImageSharp.PixelFormats.Rgba32[framePreviewWidth*framePreviewHeight];
                frameImage.CopyPixelDataTo(framePreviewPixels);
            }
            typeof(ExileCenter.Ui.ImGuiTheme).GetField("frameTexture",flags).SetValue(null,new IntPtr(2));
            var updatesType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.LocalUpdates",true);
            var offerField=updatesType.GetField("offer",flags);var previousOffer=offerField.GetValue(null);
            var settingsType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Settings.SettingsWindow",true);
            var selectedPageField=settingsType.GetField("selectedSettingsPage",flags);var previousPage=selectedPageField.GetValue(null);
            float previousToolbarFontScale=io.FontGlobalScale;
            try
            {
                io.FontGlobalScale=1.35f;
                var packageType=updatesType.GetNestedType("Package",BindingFlags.NonPublic);
                offerField.SetValue(null,Activator.CreateInstance(packageType,new object[]{"fixture:toolbar-update","fixture.zip",new string('a',64),"ExileCenter fixture"}));
                foreach(bool independent in new[]{false,true})
                foreach(int width in new[]{800,1200})
                {
                    Vector2 buttonCenter=Vector2.Zero;
                    for(int frame=0;frame<6;frame++)
                    {
                        io.AddMousePosEvent(frame>=3?buttonCenter.X:-100,frame>=3?buttonCenter.Y:-100);
                        io.AddMouseButtonEvent(0,frame==4);
                        ImGuiNET.ImGui.NewFrame();
                        typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("PushSettingsStyle",flags).Invoke(null,null);
                        ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width,600));
                        var menuFlags=ImGuiNET.ImGuiWindowFlags.MenuBar|(independent?ImGuiNET.ImGuiWindowFlags.NoTitleBar:ImGuiNET.ImGuiWindowFlags.None);
                        typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("BeginSettingsWindow",flags).Invoke(null,new object[]{"Mise a jour###toolbar-fixture",true,menuFlags});
                        settingsType.GetMethod("DrawManuBar",flags).Invoke(null,new object[]{independent});
                        var buttonMin=ImGuiNET.ImGui.GetItemRectMin();var buttonMax=ImGuiNET.ImGui.GetItemRectMax();
                        buttonCenter=(buttonMin+buttonMax)/2;
                        if(frame==2)Check(buttonMin.X>=16&&buttonMax.X<=16+width,
                            "The persistent update button beside Save stays inside the window: independent="+independent+", width="+width+", bounds="+buttonMin+".."+buttonMax);
                        if(frame==5)Check((string)selectedPageField.GetValue(null)=="core:updates","The real persistent update button opens the update page by mouse click");
                        ImGuiNET.ImGui.End();typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("PopSettingsStyle",flags).Invoke(null,null);
                        ImGuiNET.ImGui.Render();
                        if(frame==2&&previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,$"updates-toolbar-{width}-{independent}.png"),pixels,atlasWidth,atlasHeight);
                    }
                    selectedPageField.SetValue(null,previousPage);
                }
            }
            finally {offerField.SetValue(null,previousOffer);selectedPageField.SetValue(null,previousPage);io.FontGlobalScale=previousToolbarFontScale;}
            CheckSingleScreenHide();
            CheckSettingsCaption();
            io.AddMousePosEvent(-100,-100);io.AddMouseButtonEvent(0,false);
            var beforeRealWindowSize=io.DisplaySize;float beforeRealWindowFont=io.FontGlobalScale;
            var managerType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Plugin.PManager",true);
            var containers=(IList)managerType.GetField("Plugins",flags).GetValue(null);var previousContainers=containers.Cast<object>().ToArray();
            try
            {
                io.FontGlobalScale=18f/13f;
                var containerType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Plugin.PluginContainer",true);
                var metadataType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Plugin.PluginMetadata",true);
                containers.Clear();
                // Populate the real list/table without enabling plugins or attaching to a game.
                var listPlugin=new StashUtilityCore{DllDirectory=isolated};listPlugin.GetDescription();
                foreach(var pluginName in names)containers.Add(Activator.CreateInstance(containerType,new object[]{pluginName,listPlugin,Activator.CreateInstance(metadataType),null}));
                foreach(var displaySize in new[]{new Vector2(1824,976),new Vector2(1920,1010),new Vector2(1100,800)})
                {
                    io.DisplaySize=displaySize;Vector4 stableFrame=Vector4.Zero;
                    for(int frame=0;frame<120;frame++)
                    {
                        if(frame==40||frame==50)
                        {
                            io.AddMousePosEvent(displaySize.X*(frame==40?.6f:.2f),displaySize.Y*.5f);
                            io.AddMouseWheelEvent(0,-3);
                        }
                        ImGuiNET.ImGui.NewFrame();settingsType.GetMethod("DrawWindow",flags).Invoke(null,new object[]{true});ImGuiNET.ImGui.Render();
                        var data=ImGuiNET.ImGui.GetDrawData();Vector4 bounds=Vector4.Zero;
                        for(int listIndex=0;listIndex<data.CmdListsCount;listIndex++)
                        {
                            var list=data.CmdLists[listIndex];
                            for(int commandIndex=0;commandIndex<list.CmdBuffer.Size;commandIndex++)
                            {
                                var command=list.CmdBuffer[commandIndex];if(command.TextureId!=new IntPtr(2))continue;
                                for(int offset=0;offset<command.ElemCount;offset+=3)
                                {
                                    var vertices=Enumerable.Range(0,3).Select(i=>list.VtxBuffer[(int)command.VtxOffset+list.IdxBuffer[(int)command.IdxOffset+offset+i]]).ToArray();
                                    var patchBounds=new Vector4(vertices.Min(v=>v.pos.X),vertices.Min(v=>v.pos.Y),vertices.Max(v=>v.pos.X),vertices.Max(v=>v.pos.Y));
                                    bounds=bounds==Vector4.Zero?patchBounds:new Vector4(Math.Min(bounds.X,patchBounds.X),Math.Min(bounds.Y,patchBounds.Y),Math.Max(bounds.Z,patchBounds.Z),Math.Max(bounds.W,patchBounds.W));
                                }
                            }
                        }
                        if(frame==16)stableFrame=bounds;
                        if(frame>16)Check(bounds!=Vector4.Zero&&Vector4.Distance(bounds,stableFrame)<.1f,
                            "The real settings layout keeps the original frame stationary across consecutive renders: viewport="+displaySize+", frame="+frame+", before="+stableFrame+", now="+bounds);
                        if(frame==119&&previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,$"settings-stable-{displaySize.X}x{displaySize.Y}.png"),pixels,atlasWidth,atlasHeight);
                    }
                }
            }
            finally
            {
                containers.Clear();foreach(var container in previousContainers)containers.Add(container);
                io.DisplaySize=beforeRealWindowSize;io.FontGlobalScale=beforeRealWindowFont;selectedPageField.SetValue(null,previousPage);
                io.AddMousePosEvent(-100,-100);
            }
            var ornate=typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("DrawOrnateWindow",flags);
            foreach(var panelFlags in new[]{ImGuiNET.ImGuiWindowFlags.None,ImGuiNET.ImGuiWindowFlags.NoBackground,ImGuiNET.ImGuiWindowFlags.NoInputs})
            {
                ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(500,300));
                ImGuiNET.ImGui.Begin("EXILECENTER##ornate-fixture",panelFlags);
                var list=ImGuiNET.ImGui.GetWindowDrawList();var oldClip=list.CmdBuffer[list.CmdBuffer.Size-1].ClipRect;
                var oldCursor=ImGuiNET.ImGui.GetCursorScreenPos();
                bool drawn=(bool)ornate.Invoke(null,new object[]{"ExileCenter##hidden-id",panelFlags,true,false,-1f});
                Check(drawn==(panelFlags==ImGuiNET.ImGuiWindowFlags.None),"The textured skin excludes transparent and input-free overlays: "+panelFlags);
                var contentClip=list.CmdBuffer[list.CmdBuffer.Size-1].ClipRect;
                Check(drawn?contentClip.X>=oldClip.X&&contentClip.Z<=oldClip.Z&&contentClip.W<=oldClip.W&&contentClip.Y>=oldClip.Y&&
                    contentClip.Y>=ImGuiNET.ImGui.GetWindowPos().Y+ImGuiNET.ImGui.GetWindowWidth()*(48f/1672)*1.35f&&
                    contentClip.W<=ImGuiNET.ImGui.GetWindowPos().Y+ImGuiNET.ImGui.GetWindowHeight()-ImGuiNET.ImGui.GetWindowWidth()*(52f/1672):contentClip==oldClip,
                    "Content clipping protects the cartouche and carved borders: "+panelFlags);
                Check(drawn?ImGuiNET.ImGui.GetCursorScreenPos().Y>=oldCursor.Y:ImGuiNET.ImGui.GetCursorScreenPos()==oldCursor,
                    "Only opaque titled windows reserve space for their gold cartouche: "+panelFlags);
                ImGuiNET.ImGui.TextUnformatted("Filtres et configuration");
                bool enabled=true;ImGuiNET.ImGui.Checkbox("Activer le filtre",ref enabled);
                ImGuiNET.ImGui.SetNextItemWidth(-1);int tier=13;ImGuiNET.ImGui.Combo("##tier-preview",ref tier,new[]{"I","II","III","IV","V","VI","VII","VIII","IX","X","XI","XII","XIII","XIV","XV","XVI"},16);
                ImGuiNET.ImGui.Button("Appliquer",new Vector2(-1,0));
                Check(ImGuiNET.ImGui.GetItemRectMax().Y<316,"Native controls remain inside the textured frame");
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                if(previewDirectory!=null&&drawn)SaveUiPreview(Path.Combine(previewDirectory,"poe2-carved-panel.png"),pixels,atlasWidth,atlasHeight);
            }
            bool closableOpen=true;
            foreach(var click in new[]{false,true,false})
            {
                io.AddMousePosEvent(16+500-ImGuiNET.ImGui.GetStyle().FramePadding.X-ImGuiNET.ImGui.GetFontSize()/2,16+ImGuiNET.ImGui.GetFrameHeight()/2);
                io.AddMouseButtonEvent(0,click);ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(500,300));
                ExileCenter.Ui.ImGuiTheme.BeginWindow("FERMETURE##ornate-close",ref closableOpen,ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
            }
            Check(!closableOpen,"The repainted close button keeps its original native hit area and behavior");
            io.AddMousePosEvent(-100,-100);
            float scrollBodyStart=0;
            for(int frame=0;frame<4;frame++)
            {
                ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(500,300));
                ExileCenter.Ui.ImGuiTheme.BeginWindow("DEFILEMENT##ornate-scroll",ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                if(frame==1)scrollBodyStart=ImGuiNET.ImGui.GetCursorPosY();
                // ImGui can shift the aligned cursor by one pixel when the scrollbar first appears.
                else if(frame>1)Check(ImGuiNET.ImGui.GetScrollY()>0&&Math.Abs(ImGuiNET.ImGui.GetCursorPosY()-scrollBodyStart)<=1,
                    "Header reservation stays constant while the body scrolls: frame="+frame+", scroll="+ImGuiNET.ImGui.GetScrollY()+", start="+scrollBodyStart+", current="+ImGuiNET.ImGui.GetCursorPosY());
                ImGuiNET.ImGui.Button("Bouton sous le cartouche",new Vector2(200,20));
                if(frame>1)Check(!ImGuiNET.ImGui.IsItemVisible(),"Scrolled controls cannot paint over or receive clicks through the cartouche");
                ImGuiNET.ImGui.Dummy(new Vector2(0,800));
                if(frame==1)ImGuiNET.ImGui.SetScrollY(100);
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
            }
            foreach(bool independent in new[]{false,true})
            for(int frame=0;frame<2;frame++)
            {
                ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(800,600));
                var settingsFlags=ImGuiNET.ImGuiWindowFlags.MenuBar|(independent?ImGuiNET.ImGuiWindowFlags.NoTitleBar:ImGuiNET.ImGuiWindowFlags.None);
                typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("BeginSettingsWindow",flags).Invoke(null,new object[]{"REGLAGES##ornate-settings",true,settingsFlags});
                float bodyTop=ImGuiNET.ImGui.GetCursorScreenPos().Y;
                if(ImGuiNET.ImGui.BeginMenuBar())
                {
                    ImGuiNET.ImGui.MenuItem("Fichier");ImGuiNET.ImGui.MenuItem("Affichage");ImGuiNET.ImGui.MenuItem("Aide");
                    Check(ImGuiNET.ImGui.GetItemRectMax().Y<bodyTop,"Native menus remain above the ornamental header and content: "+independent);
                    ImGuiNET.ImGui.EndMenuBar();
                }
                typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("SectionHeader",flags).Invoke(null,new object[]{"Configuration",null});
                bool filter=true;ImGuiNET.ImGui.Checkbox("Activer les filtres",ref filter);ImGuiNET.ImGui.Button("Appliquer");
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"poe2-settings-"+independent+".png"),pixels,atlasWidth,atlasHeight);
            }
            foreach(var windowSize in new[]{new Vector2(1000,1000),new Vector2(1890,1024),new Vector2(460,900)})
            {
                io.DisplaySize=windowSize;
                for(int frame=0;frame<6;frame++)
                {
                    ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(Vector2.Zero);ImGuiNET.ImGui.SetNextWindowSize(windowSize);
                    var frameFlags=ImGuiNET.ImGuiWindowFlags.MenuBar|ImGuiNET.ImGuiWindowFlags.NoTitleBar|ImGuiNET.ImGuiWindowFlags.NoSavedSettings;
                    typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("BeginSettingsWindow",flags).Invoke(null,new object[]{"Proportions###frame-aspect",true,frameFlags});
                    if(frame==5)
                    {
                        var list=ImGuiNET.ImGui.GetWindowDrawList();
                        int artworkTriangles=0,headerTriangles=0,footerTriangles=0;
                        var artworkMin=new Vector2(float.MaxValue);var artworkMax=new Vector2(float.MinValue);
                        var viewport=windowSize;
                        float menuHeight=ImGuiNET.ImGui.GetFrameHeight();
                        float imageHeight=viewport.Y-menuHeight;
                        float imageWidth=viewport.X;
                        var imageStart=new Vector2(0,menuHeight);
                        foreach(int commandIndex in Enumerable.Range(0,list.CmdBuffer.Size))
                        {
                            var command=list.CmdBuffer[commandIndex];if(command.TextureId!=new IntPtr(2))continue;
                            for(int offset=0;offset<command.ElemCount;offset+=3)
                            {
                                var vertices=Enumerable.Range(0,3).Select(i=>list.VtxBuffer[(int)command.VtxOffset+list.IdxBuffer[(int)command.IdxOffset+offset+i]]).ToArray();
                                float dx=vertices.Max(v=>v.pos.X)-vertices.Min(v=>v.pos.X),dy=vertices.Max(v=>v.pos.Y)-vertices.Min(v=>v.pos.Y);
                                float du=vertices.Max(v=>v.uv.X)-vertices.Min(v=>v.uv.X),dv=vertices.Max(v=>v.uv.Y)-vertices.Min(v=>v.uv.Y);
                                if(dx>.1f&&dy>.1f&&du>.0001f&&dv>.0001f)
                                    Check(Math.Abs(dx/du/framePreviewWidth-dy/dv/framePreviewHeight)<.0002f,"Every artwork patch retains its original pixel aspect ratio: "+windowSize);
                                Check(vertices.All(v=>v.uv.X>=0&&v.uv.X<=1&&v.uv.Y>=0&&v.uv.Y<=1),
                                    "Adaptive frame samples stay inside the chosen image: "+windowSize);
                                Check(vertices.All(v=>v.col==0xffffffff),"The original frame colors have no tint or opacity modification: "+windowSize);
                                artworkMin=Vector2.Min(artworkMin,new Vector2(vertices.Min(v=>v.pos.X),vertices.Min(v=>v.pos.Y)));
                                artworkMax=Vector2.Max(artworkMax,new Vector2(vertices.Max(v=>v.pos.X),vertices.Max(v=>v.pos.Y)));
                                if(du>.5f&&Math.Abs(vertices.Min(v=>v.uv.Y))<.0001f&&Math.Abs(dv-48f/941)<.0001f)
                                {
                                    headerTriangles++;
                                    Check(Math.Abs(dx-imageWidth*(1592f/1672))<1&&Math.Abs(dy-imageWidth*(48f/1672)*1.35f)<1,
                                        "The upper frieze is enlarged uniformly by 35 percent between the unchanged pillars: "+windowSize);
                                }
                                if(Math.Abs(du-1)<.0001f&&Math.Abs(vertices.Min(v=>v.uv.Y)-875f/941)<.0001f&&Math.Abs(vertices.Max(v=>v.uv.Y)-1)<.0001f)footerTriangles++;
                                Check(vertices.All(v=>v.pos.X>=command.ClipRect.X-.01f&&v.pos.X<=command.ClipRect.Z+.01f&&
                                    v.pos.Y>=command.ClipRect.Y-.01f&&v.pos.Y<=command.ClipRect.W+.01f),
                                    "No image edge or corner is clipped, including beside native scrollbars: "+windowSize);
                                artworkTriangles++;
                            }
                        }
                        Check(artworkTriangles>=14&&headerTriangles==2&&footerTriangles==2,"The enlarged upper frieze and complete footer each span their width once, with preserved corners: "+windowSize);
                        Check(Vector2.Distance(artworkMin,imageStart)<1&&Vector2.Distance(artworkMax,imageStart+new Vector2(imageWidth,imageHeight))<1,
                            "Artwork fills all viewport edges without black lateral margins: "+windowSize);
                        var cursor=ImGuiNET.ImGui.GetCursorScreenPos();var available=ImGuiNET.ImGui.GetContentRegionAvail();
                        Check(cursor.X>=imageStart.X+imageWidth*(40f/1672)-1&&cursor.X+available.X<=imageStart.X+imageWidth*(1-40f/1672)+1,
                            "Content and fill-width controls fit between the proportional pillars: "+windowSize);
                        Check(cursor.Y>=imageStart.Y+imageWidth*(48f/1672)*1.35f&&cursor.Y+available.Y<=imageStart.Y+imageHeight-imageWidth*(52f/1672)+1,
                            "The work rectangle reserves both the cartouche and the carved footer: "+windowSize);
                        Check(cursor.X-imageStart.X-imageWidth*(40f/1672)<=4&&imageStart.X+imageWidth*(1-40f/1672)-cursor.X-available.X<=4&&
                            cursor.Y-imageStart.Y-imageWidth*(48f/1672)*1.35f<=4&&imageStart.Y+imageHeight-imageWidth*(52f/1672)-cursor.Y-available.Y<=4,
                            "Settings content stays within four pixels of every protected ornamental border: "+windowSize);
                    }
                    if(ImGuiNET.ImGui.BeginMenuBar()){ImGuiNET.ImGui.MenuItem("ExileCenter 1.6.1");ImGuiNET.ImGui.EndMenuBar();}
                    ImGuiNET.ImGui.BeginChild("Navigation",new Vector2(Math.Min(220,ImGuiNET.ImGui.GetContentRegionAvail().X*.3f),0),ImGuiNET.ImGuiChildFlags.Borders);
                    foreach(var label in new[]{"Application","General","Plugins","Mises a jour","Plugins actifs","Auto-craft TP"})ImGuiNET.ImGui.Selectable(label,label=="Plugins");
                    ImGuiNET.ImGui.EndChild();ImGuiNET.ImGui.SameLine();
                    ImGuiNET.ImGui.BeginChild("Contenu",Vector2.Zero,ImGuiNET.ImGuiChildFlags.Borders);
                    typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("SectionHeader",flags).Invoke(null,new object[]{"Gestion des plugins",null});
                    ImGuiNET.ImGui.TextWrapped("Activez ou desactivez les plugins. Les modifications sont enregistrees automatiquement.");
                    bool enabled=true;foreach(var label in new[]{"Atlas et itineraires","Regles de raccourcis","AutoPot","Zoom de la camera","Barres de vie","Suivi du butin","Auto-craft TP"})ImGuiNET.ImGui.Checkbox(label,ref enabled);
                    ImGuiNET.ImGui.Button("Appliquer",new Vector2(-1,0));ImGuiNET.ImGui.EndChild();
                    ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                    if(frame==5&&previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,$"poe2-proportions-{windowSize.X}x{windowSize.Y}.png"),pixels,atlasWidth,atlasHeight);
                }
            }
            io.DisplaySize=new Vector2(1280,960);
            for(int frame=0;frame<4;frame++)
            {
                ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(460,300));
                ExileCenter.Ui.ImGuiTheme.BeginWindow("Commandes larges##frame-horizontal",ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                ImGuiNET.ImGui.Button("Commande large",new Vector2(800,20));
                if(frame==2)ImGuiNET.ImGui.SetScrollX(100);
                if(frame==3)Check(ImGuiNET.ImGui.GetScrollMaxX()>0&&ImGuiNET.ImGui.GetScrollX()>0,
                    "Narrow framed panels allow real horizontal scrolling to overflowing controls");
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
            }
            Check(names.Select(ExileCenter.Ui.ImGuiTheme.PluginAccent).Distinct().Count() == names.Length, "Every bundled plugin has a distinct accent");
            // OKLab measures perceived differences; unequal RGB values alone do not prevent lookalikes.
            Vector3 PerceptualColor(Vector4 color)
            {
                float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
                float r = Linear(color.X), g = Linear(color.Y), b = Linear(color.Z);
                float l = MathF.Cbrt(.4122214708f * r + .5363325363f * g + .0514459929f * b);
                float m = MathF.Cbrt(.2119034982f * r + .6806995451f * g + .1073969566f * b);
                float s = MathF.Cbrt(.0883024619f * r + .2817188376f * g + .6299787005f * b);
                return new Vector3(.2104542553f * l + .793617785f * m - .0040720468f * s,
                    1.9779984951f * l - 2.428592205f * m + .4505937099f * s,
                    .0259040371f * l + .7827717662f * m - .808675766f * s);
            }
            for (int i = 0; i < names.Length; i++)
                for (int j = 0; j < i; j++)
                {
                    Check(Vector3.Distance(PerceptualColor(ExileCenter.Ui.ImGuiTheme.PluginAccent(names[i])),
                        PerceptualColor(ExileCenter.Ui.ImGuiTheme.PluginAccent(names[j]))) >= .075f, "Muted plugin colours remain perceptually separated: " + names[i] + " / " + names[j]);
                    Check(Vector3.Distance(PerceptualColor(ExileCenter.Ui.ImGuiTheme.PluginAccent(names[i] + "/appearance")),
                        PerceptualColor(ExileCenter.Ui.ImGuiTheme.PluginAccent(names[j] + "/appearance"))) >= .075f, "Muted section colours remain separated: " + names[i] + " / " + names[j]);
                }
            var backgrounds = new HashSet<Vector4>();
            foreach (string name in names)
            {
                using (ExileCenter.Ui.ImGuiTheme.UsePluginTheme(name))
                {
                    var accent = ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.CheckMark];
                    Check(accent == ExileCenter.Ui.ImGuiTheme.PluginAccent(name), "Plugin theme is applied: " + name);
                    ImGuiNET.ImGui.ColorConvertRGBtoHSV(accent.X, accent.Y, accent.Z, out _, out float saturation, out float brightness);
                    Check(saturation <= .73f && brightness <= .881f, "Plugin accents stay subdued: " + name);
                    backgrounds.Add(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.WindowBg]);
                    float Luminance(Vector4 color)
                    {
                        float Linear(float component)=>component<=.04045f?component/12.92f:MathF.Pow((component+.055f)/1.055f,2.4f);
                        return .2126f*Linear(color.X)+.7152f*Linear(color.Y)+.0722f*Linear(color.Z);
                    }
                    var body=ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.WindowBg];
                    Check(Math.Max(body.X,Math.Max(body.Y,body.Z))<.15f,"Every plugin retains the shared dark stone background: "+name);
                    foreach(var control in new[]{ImGuiNET.ImGuiCol.WindowBg,ImGuiNET.ImGuiCol.FrameBg,ImGuiNET.ImGuiCol.Button,ImGuiNET.ImGuiCol.HeaderActive})
                        Check((Luminance(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.Text])+.05f)/
                            (Luminance(ImGuiNET.ImGui.GetStyle().Colors[(int)control])+.05f)>=4.5f,"Ivory text remains readable on themed content: "+name+control);
                    Check(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.FrameBg] != original[(int)ImGuiNET.ImGuiCol.FrameBg], "Input fields follow the plugin palette: " + name);
                    using (ExileCenter.Ui.ImGuiTheme.UsePluginTheme(name + "/appearance"))
                    {
                        Check(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.CheckMark] == accent, "Sections retain the exact plugin accent: " + name);
                    }
                    Check(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.CheckMark] == accent, "Nested theme restores plugin accent: " + name);
                }
                Check(Colors().SequenceEqual(original), "Theme cannot leak into the next plugin: " + name);
            }
            Check(backgrounds.Count == names.Length, "Every plugin has a distinct dark background");
            ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.WindowBg, Vector4.Zero);
            ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.ChildBg, Vector4.Zero);
            using (ExileCenter.Ui.ImGuiTheme.UsePluginTheme("Radar"))
            {
                Check(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.WindowBg].W == 0, "Transparent gameplay windows remain transparent");
                Check(ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.ChildBg].W == 0, "Transparent gameplay children remain transparent");
            }
            ImGuiNET.ImGui.PopStyleColor(2);
            if (previewDirectory != null)
            {
                Directory.CreateDirectory(previewDirectory);
                for (int frame = 0; frame < 2; frame++)
                {
                    ImGuiNET.ImGui.NewFrame();
                    for (int index = 0; index < names.Length; index++)
                    {
                        using var theme = ExileCenter.Ui.ImGuiTheme.UsePluginTheme(names[index]);
                        ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10 + index % 5 * 254, 10 + index / 5 * 188));
                        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(244, 180));
                        ExileCenter.Ui.ImGuiTheme.BeginWindow(names[index] + "##ThemePreview", ImGuiNET.ImGuiWindowFlags.NoSavedSettings | ImGuiNET.ImGuiWindowFlags.NoResize | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoCollapse);
                        ImGuiNET.ImGui.TextColored(ExileCenter.Ui.ImGuiTheme.PluginAccent(names[index]), "Relique ancienne");
                        bool enabled = true; float intensity = .65f;
                        ImGuiNET.ImGui.Checkbox("Activer", ref enabled);
                        ImGuiNET.ImGui.SetNextItemWidth(-1);
                        ImGuiNET.ImGui.SliderFloat("##Intensity", ref intensity, 0, 1);
                        ImGuiNET.ImGui.Button("Options", new Vector2(-1, 0));
                        ImGuiNET.ImGui.End();
                    }
                    ImGuiNET.ImGui.Render();
                }
                SaveUiPreview(Path.Combine(previewDirectory, "plugin-themes.png"), pixels, atlasWidth, atlasHeight);
                Check(Colors().SequenceEqual(original), "All plugin preview windows restore the shared style");
            }
            var secondary = ImGuiNET.ImGui.CreateContext();
            ImGuiNET.ImGui.SetCurrentContext(secondary);
            var secondaryColors = Colors();
            ImGuiNET.ImGui.SetCurrentContext(context);
            var scope = ExileCenter.Ui.ImGuiTheme.UsePluginTheme("LootTracker");
            ImGuiNET.ImGui.SetCurrentContext(secondary);
            scope.Dispose();
            Check(ImGuiNET.ImGui.GetCurrentContext() == secondary && Colors().SequenceEqual(secondaryColors), "Theme disposal respects the native-window ImGui context");
            ImGuiNET.ImGui.DestroyContext(secondary); ImGuiNET.ImGui.SetCurrentContext(context);
            Check(Colors().SequenceEqual(original), "Original context also restored after native-window theme");
            using (ExileCenter.Utils.ImGuiHelper.UseHelpTooltips(false))
            {
                Check(!ExileCenter.Utils.ImGuiHelper.HelpTooltipsEnabled, "Help can be disabled");
                using (ExileCenter.Utils.ImGuiHelper.UseHelpTooltips(true)) Check(ExileCenter.Utils.ImGuiHelper.HelpTooltipsEnabled, "Nested help can be enabled");
                Check(!ExileCenter.Utils.ImGuiHelper.HelpTooltipsEnabled, "Help preference restored after nested page");
            }
            Check(ExileCenter.Utils.ImGuiHelper.HelpTooltipsEnabled, "Tablet help does not disable another plugin's help");
            var plugin = new StashUtilityCore { DllDirectory = isolated };
            typeof(StashUtilityCore).GetMethod("ApplyTabletFarmPreset", flags).Invoke(plugin, new object[] { true });
            var profile = plugin.Settings.TabletProfiles["Breach Tablet"];
            var meta = typeof(StashUtilityCore).GetMethod("DrawTabletMetaFilters", flags);
            var draw = typeof(StashUtilityCore).GetMethod("DrawTabletFilterSettings", flags);
            var push = typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("PushSettingsStyle", flags);
            var pop = typeof(ExileCenter.Ui.ImGuiTheme).GetMethod("PopSettingsStyle", flags);
            (Vector2 center, float height, bool referenceOpen) MetaFrame(Vector2 mouse, bool down)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(1200, 900));
                ImGuiNET.ImGui.Begin("Meta layout fixture");
                uint reference = ImGuiNET.ImGui.GetID("###TabletReferenceCatalogue");
                meta.Invoke(plugin, new object[] { "Breach Tablet", profile });
                var result = ((ImGuiNET.ImGui.GetItemRectMin() + ImGuiNET.ImGui.GetItemRectMax()) / 2, ImGuiNET.ImGui.GetCursorPosY(), ImGuiNET.ImGui.GetStateStorage().GetInt(reference, 0) != 0);
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render(); return result;
            }
            string existingRanks = JsonConvert.SerializeObject(profile.ModRanks);
            profile.UseMetaFilters = true;
            var closed = MetaFrame(new Vector2(-100), false);
            Check(!profile.UseMetaFilters && JsonConvert.SerializeObject(profile.ModRanks) == existingRanks,
                "Opening settings repairs an orphan advanced toggle without changing the ranks");
            var repaired = JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(isolated, "config", "settings.txt")));
            Check(!repaired.TabletProfiles["Breach Tablet"].UseMetaFilters, "Orphan toggle repair persists to disk");
            Check(!closed.referenceOpen && closed.height < 200, "Reference catalogue and rule editor are collapsed by default");
            MetaFrame(closed.center, false); MetaFrame(closed.center, true); var opened = MetaFrame(closed.center, false);
            Check(opened.referenceOpen && opened.height > closed.height + 250, "Reference catalogue opens with one click");
            MetaFrame(closed.center, true); var hidden = MetaFrame(closed.center, false);
            Check(!hidden.referenceOpen && hidden.height == closed.height, "Reference catalogue hides again with one click");
            Vector2 AdvancedFrame(Vector2 mouse, bool down)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(20)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(1000, 750));
                ImGuiNET.ImGui.Begin("advanced enable fixture");
                var start = ImGuiNET.ImGui.GetCursorScreenPos();
                float frameHeight = ImGuiNET.ImGui.GetFrameHeight();
                ImGuiNET.ImGui.SetNextItemOpen(true);
                meta.Invoke(plugin, new object[] { "Breach Tablet", profile });
                var center = start + new Vector2(frameHeight / 2, frameHeight * 1.5f + ImGuiNET.ImGui.GetStyle().ItemSpacing.Y);
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render(); return center;
            }
            var enableCenter = AdvancedFrame(new Vector2(-100), false);
            AdvancedFrame(enableCenter, false); AdvancedFrame(enableCenter, true); AdvancedFrame(enableCenter, false);
            Check(!profile.UseMetaFilters, "Advanced filter checkbox cannot enable a missing profile");
            var editable = new TabletFilterProfile { Name = "UI validation", TabletType = "Breach Tablet" };
            profile.MetaProfiles.Add(editable); profile.ActiveMetaProfileId = editable.Id;
            AdvancedFrame(enableCenter, false); AdvancedFrame(enableCenter, true); AdvancedFrame(enableCenter, false);
            Check(!profile.UseMetaFilters, "Advanced filter checkbox cannot enable an incomplete rule");
            editable.Rule = new() { Kind = TabletRuleKind.RankExact, Rank = "S" };
            AdvancedFrame(enableCenter, false); AdvancedFrame(enableCenter, true); AdvancedFrame(enableCenter, false);
            Check(profile.UseMetaFilters, "Advanced filter checkbox enables a completed valid profile");
            profile.UseMetaFilters = false; profile.ActiveMetaProfileId = ""; profile.MetaProfiles.Clear();
            var mode = typeof(StashUtilityCore).GetMethod("DrawTabletMatchMode", flags);
            (Vector2 all, Vector2 any) ModeFrame(Vector2 mouse, bool down)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(30)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(650, 180));
                ImGuiNET.ImGui.Begin("ET OU fixture");
                var start = ImGuiNET.ImGui.GetCursorScreenPos();
                float box = ImGuiNET.ImGui.GetFrameHeight();
                var all = start + new Vector2(box / 2);
                var any = all + new Vector2(box + ImGuiNET.ImGui.GetStyle().ItemInnerSpacing.X +
                    ImGuiNET.ImGui.CalcTextSize("ET").X + ImGuiNET.ImGui.GetStyle().ItemSpacing.X, 0);
                mode.Invoke(plugin, new object[] { "Breach Tablet", profile });
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render();
                return (all, any);
            }
            var modeBoxes = ModeFrame(new Vector2(-100), false);
            Check(!profile.RequireAllRankedMods, "OU is selected initially without activating analysis or advanced rules");
            ModeFrame(modeBoxes.all, false); ModeFrame(modeBoxes.all, true); ModeFrame(modeBoxes.all, false);
            Check(profile.RequireAllRankedMods && !profile.UseMetaFilters, "Clicking ET selects only ET and keeps advanced filtering off");
            ModeFrame(modeBoxes.all, true); ModeFrame(modeBoxes.all, false);
            Check(profile.RequireAllRankedMods, "Clicking active ET cannot leave neither choice selected");
            Check(JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(isolated, "config", "settings.txt"))).TabletProfiles["Breach Tablet"].RequireAllRankedMods,
                "ET persists independently of analysis");
            ModeFrame(modeBoxes.any, false); ModeFrame(modeBoxes.any, true); ModeFrame(modeBoxes.any, false);
            Check(!profile.RequireAllRankedMods && !profile.UseMetaFilters, "Clicking OU replaces ET without enabling advanced rules");
            ModeFrame(modeBoxes.any, true); ModeFrame(modeBoxes.any, false);
            Check(!profile.RequireAllRankedMods, "Clicking active OU cannot leave neither choice selected");
            float previousPaneWidth = 0;
            Vector2 Pane(float width, string filename, Vector2? mouse = null, bool down = false)
            {
                var position = mouse ?? new Vector2(-100);
                io.AddMousePosEvent(position.X, position.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame(); push.Invoke(null, null);
                Vector2 helpCenter;
                using (ExileCenter.Ui.ImGuiTheme.UsePluginTheme("StashUtility/tablets"))
                {
                    ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width, 900));
                    // These widths exercise the tablet controls' viewport, independently of
                    // the ornamental outer frame tested above at square/wide/narrow sizes.
                    ImGuiNET.ImGui.Begin("Tablettes · ExileCenter", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                    helpCenter = ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(70, ImGuiNET.ImGui.GetFrameHeight() + ImGuiNET.ImGui.GetStyle().ItemSpacing.Y + ImGuiNET.ImGui.GetFontSize() / 2);
                    draw.Invoke(plugin, null);
                    if (previousPaneWidth == width)
                        Check(ImGuiNET.ImGui.GetScrollMaxX()==0,"Tablet layout fits horizontally at width "+width);
                    ImGuiNET.ImGui.End();
                }
                pop.Invoke(null, null); ImGuiNET.ImGui.Render();
                if (previewDirectory != null)
                {
                    Directory.CreateDirectory(previewDirectory);
                    SaveUiPreview(Path.Combine(previewDirectory, filename), pixels, atlasWidth, atlasHeight);
                }
                Check(Colors().SequenceEqual(original), "Tablet pane restores plugin style and help state");
                previousPaneWidth = width;
                return helpCenter;
            }
            Pane(1200, "tablets-wide.png"); Pane(1200, "tablets-wide.png"); Pane(460, "tablets-compact.png");
            var catalogDraw=typeof(StashUtilityCore).GetMethod("DrawModCatalog",flags);
            var searchProfile=new TabletProfile();searchProfile.ModRanks["TowerMonsterEffectiveness"]="A";
            var searchMods=ModDatabase.AllTabletMods.Where(m=>m.TabletTypes.Contains("Irradiated Tablet")&&
                new[]{"TowerMonsterEffectiveness","TowerPackSizeIncrease","TowerMapAdditionalModifier","TowerAdditionalSpiritChance"}.Contains(m.Id)).ToList();
            string searchSettings=JsonConvert.SerializeObject(searchProfile);
            uint searchColor=ExileCenter.Utils.ImGuiHelper.Color(new Vector4(.95f,.75f,.2f,.28f));
            (float height,int highlights) SearchFrame(string query,float width)
            {
                io.AddMousePosEvent(-100,-100);ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width,750));
                ImGuiNET.ImGui.Begin("Recherche des mods##search-fixture",ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                object[] arguments={"tablet",searchMods,searchProfile.GoodModPatterns,searchProfile.BadModPatterns,searchProfile.GodModPatterns,
                    searchProfile.ModRequiredMinRolls,query,false,0,searchProfile.ModRanks};
                catalogDraw.Invoke(plugin,arguments);
                float height=ImGuiNET.ImGui.GetCursorPosY();
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                var data=ImGuiNET.ImGui.GetDrawData();
                int highlights=Enumerable.Range(0,data.CmdListsCount).Sum(i=>Enumerable.Range(0,data.CmdLists[i].VtxBuffer.Size).Count(j=>data.CmdLists[i].VtxBuffer[j].col==searchColor));
                return(height,highlights);
            }
            foreach(float width in new[]{460f,1000f})
            {
                SearchFrame("introuvable",width);var missing=SearchFrame("introuvable",width);
                foreach(string query in new[]{"efficacite","EFFICACITÉ","effectiveness"})
                {
                    var found=SearchFrame(query,width);
                    Check(found.highlights>0&&missing.highlights==0,"Search highlights French/English names regardless of case or accents: "+query+width);
                    Check(found.height==missing.height,"Search keeps every modifier row visible: "+query+width);
                }
                if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"tablet-search-"+width+".png"),pixels,atlasWidth,atlasHeight);
                Check(SearchFrame("",width).highlights==0,"Clearing search removes the highlight: "+width);
            }
            Check(JsonConvert.SerializeObject(searchProfile)==searchSettings,"Searching never changes ranks, minima or selected rules");
            var craftOptions=typeof(StashUtilityCore).GetMethod("DrawWaystoneCraftOptions",flags);
            string craftSettings=JsonConvert.SerializeObject(plugin.Settings);
            foreach(float width in new[]{460f,1000f})
            {
                io.AddMousePosEvent(-100,-100);ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width,750));
                ImGuiNET.ImGui.Begin("Auto-craft des pierres de téléportation##craft-options",ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                craftOptions.Invoke(plugin,null);
                Check(ImGuiNET.ImGui.GetCursorPosY()<740,"Craft settings fit inside the pane: "+width);
                Check(ImGuiNET.ImGui.GetItemRectMax().X<=16+width,"Craft budget control stays inside compact and wide panes: "+width);
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"waystone-craft-"+width+".png"),pixels,atlasWidth,atlasHeight);
            }
            Check(JsonConvert.SerializeObject(plugin.Settings)==craftSettings,"Viewing craft settings never starts crafting or changes saved settings");
            var craftContents=typeof(StashUtilityCore).GetMethod("DrawWaystoneCraftSelectionContents",flags);
            var craftTabType=typeof(StashUtilityCore).GetNestedType("DetectedStashTab",flags);
            object CraftTab(string name,string folder)
            {
                var tab=Activator.CreateInstance(craftTabType,true);
                craftTabType.GetField("Name").SetValue(tab,name);
                craftTabType.GetField("Folders").SetValue(tab,new List<string>{folder});
                return tab;
            }
            var craftSource=CraftTab("PTP","01 - Mapping");var craftMoney=CraftTab("Currencies","03 - Monétaires");
            var craftChoices=(IList)typeof(StashUtilityCore).GetField("craftTabChoices",flags).GetValue(plugin);
            craftChoices.Add(craftSource);craftChoices.Add(craftMoney);
            foreach(float width in new[]{460f,580f})foreach(bool selected in new[]{false,true})
            {
                typeof(StashUtilityCore).GetField("craftSelectedSource",flags).SetValue(plugin,selected?craftSource:null);
                typeof(StashUtilityCore).GetField("craftSelectedMoney",flags).SetValue(plugin,selected?craftMoney:null);
                typeof(StashUtilityCore).GetField("craftSelectedTier",flags).SetValue(plugin,selected?13:-1);
                typeof(StashUtilityCore).GetField("craftSelectedPage",flags).SetValue(plugin,selected?0:-1);
                typeof(StashUtilityCore).GetField("craftSelectedPageName",flags).SetValue(plugin,selected?"Div - 1":null);
                var sourceFolders=(List<string>)typeof(StashUtilityCore).GetField("craftSourceFolders",flags).GetValue(plugin);
                var moneyFolders=(List<string>)typeof(StashUtilityCore).GetField("craftMoneyFolders",flags).GetValue(plugin);
                sourceFolders.Clear();moneyFolders.Clear();
                if(selected){sourceFolders.Add("01 - Mapping");moneyFolders.Add("03 - Monétaires");}
                io.AddMousePosEvent(-100,-100);ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width,750));
                ImGuiNET.ImGui.Begin("Auto-craft TP - choix des coffres##selection-fixture",ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                Check(!(bool)craftContents.Invoke(plugin,null),"Viewing empty or complete craft selection never confirms a run: "+selected+width);
                Check(ImGuiNET.ImGui.GetCursorPosY()<700,"All craft choices and buttons fit in the selection window: "+selected+width);
                Check(ImGuiNET.ImGui.GetItemRectMax().X<=16+width,"Craft selection controls fit the window: "+selected+width);
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
                if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"waystone-selection-"+width+"-"+selected+".png"),pixels,atlasWidth,atlasHeight);
            }
            Check(!(bool)typeof(StashUtilityCore).GetField("waystoneCraftRunning",flags).GetValue(plugin)&&
                JsonConvert.SerializeObject(plugin.Settings)==craftSettings,"Craft selection sends no game input and preserves saved settings before confirmation");
            var selectionWindow=typeof(StashUtilityCore).GetMethod("DrawWaystoneCraftSelection",flags);
            typeof(StashUtilityCore).GetField("craftSelectionOpen",flags).SetValue(plugin,true);
            foreach(bool focused in new[]{false,true,true})
            {
                io.AddFocusEvent(focused);io.AddMousePosEvent(1200,900);io.AddMouseButtonEvent(0,focused);
                ImGuiNET.ImGui.NewFrame();selectionWindow.Invoke(plugin,null);ImGuiNET.ImGui.Render();
                Check((bool)typeof(StashUtilityCore).GetField("craftSelectionOpen",flags).GetValue(plugin),"Switching screens or clicking outside keeps the selection window open");
                Check((string)typeof(StashUtilityCore).GetField("craftSelectedPageName",flags).GetValue(plugin)=="Div - 1","Focus changes preserve the chosen sub-tab name");
            }
            io.AddMouseButtonEvent(0,false);io.AddFocusEvent(true);
            typeof(StashUtilityCore).GetField("craftSelectionOpen",flags).SetValue(plugin,false);
            var analysisObservation = ModDatabase.TabletMeta.Recognize("Breach Tablet",
                new[] { ModDatabase.TabletMeta.FindSubRank("Breach Tablet","S1").GameModIds[0] });
            analysisObservation.Mods[0].Roll=10000;
            typeof(StashUtilityCore).GetField("lastMetaObservation",flags).SetValue(plugin,analysisObservation);
            typeof(StashUtilityCore).GetField("lastMetaItemName",flags).SetValue(plugin,"Tablette de validation");
            typeof(StashUtilityCore).GetField("lastMetaObservedUtc",flags).SetValue(plugin,DateTime.UtcNow);
            var analysis=typeof(StashUtilityCore).GetMethod("DrawMetaAnalysis",flags);
            string analysisSettings=JsonConvert.SerializeObject(profile);
            for(int frame=0;frame<2;frame++)
            {
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(1100,750));
                ImGuiNET.ImGui.Begin("Analyse de tablette · ExileCenter");
                analysis.Invoke(plugin,new object[]{"Breach Tablet",profile});
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render();
            }
            if(previewDirectory!=null)SaveUiPreview(Path.Combine(previewDirectory,"tablets-analysis.png"),pixels,atlasWidth,atlasHeight);
            Check(JsonConvert.SerializeObject(profile)==analysisSettings,"Analysis renders ranking, selected/reference details, upper bound and filters without changing profiles");
            var helpCenter = Pane(460, "tablets-compact.png");
            Pane(460, "tablets-compact.png", helpCenter); Pane(460, "tablets-compact.png", helpCenter, true); Pane(460, "tablets-help-off.png", helpCenter);
            Check(!plugin.Settings.TabletHelpTooltips, "Actual help button disables tablet tooltips in one click");
            var reload = JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(isolated, "config", "settings.txt")));
            Check(!reload.TabletHelpTooltips && reload.TabletProfiles["Breach Tablet"].ModRanks.Count == profile.ModRanks.Count, "Help preference persists without changing tablet ranks");
            Pane(460, "tablets-help-off.png", helpCenter, true); Pane(460, "tablets-help-on.png", helpCenter);
            Check(plugin.Settings.TabletHelpTooltips, "Actual help button re-enables tablet tooltips in one click");
            Check(JsonConvert.DeserializeObject<StashUtilitySettings>("{}").TabletHelpTooltips, "Existing configurations retain available tooltips");
            var presetBar = typeof(StashUtilityCore).GetMethod("DrawTabletPresets", flags);
            Vector2 PresetFrame(Vector2 mouse, bool down)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(40)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(420, 180));
                ImGuiNET.ImGui.Begin("preset fixture"); presetBar.Invoke(plugin, null);
                var center = (ImGuiNET.ImGui.GetItemRectMin() + ImGuiNET.ImGui.GetItemRectMax()) / 2;
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render(); return center;
            }
            var loadCenter = PresetFrame(new Vector2(-100), false);
            var saveCenter = loadCenter - new Vector2(ImGuiNET.ImGui.GetFrameHeight() + ImGuiNET.ImGui.GetStyle().ItemSpacing.X, 0);
            string choices = JsonConvert.SerializeObject(plugin.Settings.TabletProfiles);
            PresetFrame(saveCenter, false); PresetFrame(saveCenter, true); PresetFrame(saveCenter, false);
            Check(plugin.Settings.TabletPresets[0] != null, "Actual floppy icon saves the selected slot");
            plugin.Settings.TabletProfiles.Clear();
            PresetFrame(loadCenter, false); PresetFrame(loadCenter, true); PresetFrame(loadCenter, false);
            Check(JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == choices, "Actual load icon restores the selected slot");
            var savedPresets = JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(isolated, "config", "settings.txt")));
            Check(savedPresets.TabletPresets.Length == 5 && savedPresets.TabletPresets[0] != null, "Icon-saved presets are persisted on disk");
            var changeMode = typeof(StashUtilityCore).GetMethod("SetTabletMode", flags);
            changeMode.Invoke(plugin, new object[] { "farm", null });
            string automatic = JsonConvert.SerializeObject(plugin.Settings.TabletProfiles);
            string protectedSlots = JsonConvert.SerializeObject(plugin.Settings.TabletPresets);
            PresetFrame(saveCenter, false); PresetFrame(saveCenter, true); PresetFrame(saveCenter, false);
            PresetFrame(loadCenter, false); PresetFrame(loadCenter, true); PresetFrame(loadCenter, false);
            Check(JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == automatic &&
                JsonConvert.SerializeObject(plugin.Settings.TabletPresets) == protectedSlots, "Disabled save and load icons cannot change automatic profiles or personal slots");
            Pane(1200, "tablets-mode-farming.png"); Pane(1200, "tablets-mode-farming.png");
            changeMode.Invoke(plugin, new object[] { "farm_trade", null });
            Pane(1200, "tablets-mode-resale.png");
            changeMode.Invoke(plugin, new object[] { "strategy", "ritual" });
            Pane(460, "tablets-mode-strategy.png"); Pane(460, "tablets-mode-strategy.png");
            changeMode.Invoke(plugin, new object[] { "custom", null });
            Check(JsonConvert.SerializeObject(plugin.Settings.TabletProfiles) == choices, "UI mode changes restore the exact Custom choices");
        }
        finally
        {
            ExileCenter.Core.GHSettings.UiLanguage = previousLanguage;
            Directory.SetCurrentDirectory(previousDirectory); ImGuiNET.ImGui.DestroyContext(context);
        }
    }

    private static void CheckSingleScreenHide()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var settings = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Settings.SettingsWindow", true);
        var theme = typeof(ExileCenter.Ui.ImGuiTheme);
        var visible = settings.GetField("isSettingsWindowVisible", flags);
        var page = settings.GetField("selectedSettingsPage", flags);
        var updates = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.LocalUpdates", true);
        var offer = updates.GetField("offer", flags);
        var previousVisible = visible.GetValue(null); var previousPage = page.GetValue(null); var previousOffer = offer.GetValue(null);
        bool previousSeparate = ExileCenter.Core.GHSettings.SettingsOnSeparateWindow;
        bool previousOpen = ExileCenter.Core.IsSettingsMenuOpen;
        var io = ImGuiNET.ImGui.GetIO(); var previousDisplay = io.DisplaySize; float previousScale = io.FontGlobalScale;
        int saves = 0;
        var saveEvent = (Coroutine.Event)typeof(ExileCenter.CoroutineEvents.ExileCenterEvents).GetField("TimeToSaveAllSettings", flags).GetValue(null);
        IEnumerator<Coroutine.Wait> CountSaves()
        {
            while (true) { yield return new Coroutine.Wait(saveEvent); saves++; }
        }
        var observer = Coroutine.CoroutineHandler.Start(CountSaves());
        try
        {
            ExileCenter.Core.GHSettings.SettingsOnSeparateWindow = false;
            page.SetValue(null, "core:plugins"); offer.SetValue(null, null);
            io.DisplaySize = new Vector2(1920, 1010);
            foreach (float fontScale in new[] { 1f, 1.35f })
            foreach (int width in new[] { 800, 1101, 1280 })
            {
                io.FontGlobalScale = fontScale; visible.SetValue(null, true);
                Vector2 saveCenter = Vector2.Zero, hideCenter = Vector2.Zero;
                for (int frame = 0; frame < 2; frame++)
                {
                    io.AddMousePosEvent(-100, -100); io.AddMouseButtonEvent(0, false);
                    ImGuiNET.ImGui.NewFrame(); theme.GetMethod("PushSettingsStyle", flags).Invoke(null, null);
                    ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width, 760));
                    theme.GetMethod("BeginSettingsWindow", flags).Invoke(null, new object[] { "Single screen###GameOverlaySettings", true, ImGuiNET.ImGuiWindowFlags.MenuBar });
                    settings.GetMethod("DrawManuBar", flags).Invoke(null, new object[] { false });
                    saveCenter = (ImGuiNET.ImGui.GetItemRectMin() + ImGuiNET.ImGui.GetItemRectMax()) / 2;
                    var style = ImGuiNET.ImGui.GetStyle(); float fontSize = ImGuiNET.ImGui.GetFontSize();
                    hideCenter = ImGuiNET.ImGui.GetWindowPos() + new Vector2(width - style.FramePadding.X - 1.5f * fontSize - style.ItemInnerSpacing.X, ImGuiNET.ImGui.GetFrameHeight() / 2);
                    ImGuiNET.ImGui.End(); theme.GetMethod("PopSettingsStyle", flags).Invoke(null, null); ImGuiNET.ImGui.Render();
                }
                int beforeSave = saves;
                for (int frame = 0; frame < 6; frame++)
                {
                    var mouse = frame < 3 ? saveCenter : hideCenter;
                    io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, frame == 1 || frame == 4);
                    ImGuiNET.ImGui.NewFrame();
                    if ((bool)visible.GetValue(null)) settings.GetMethod("DrawWindow", flags).Invoke(null, new object[] { false });
                    ImGuiNET.ImGui.Render();
                    if (frame == 2) Check(saves == beforeSave + 1, "The actual Save button raises one save event before hiding: " + width + "/" + fontScale);
                }
                Check(!(bool)visible.GetValue(null) && !ExileCenter.Core.IsSettingsMenuOpen, "The actual single-screen hide button closes settings and releases the overlays: " + width + "/" + fontScale);
                Check(saves == beforeSave + 2, "Hiding after Save preserves the save-on-hide event: " + width + "/" + fontScale);
                visible.SetValue(null, true); io.AddMousePosEvent(-100, -100); io.AddMouseButtonEvent(0, false);
                ImGuiNET.ImGui.NewFrame(); settings.GetMethod("DrawWindow", flags).Invoke(null, new object[] { false }); ImGuiNET.ImGui.Render();
                Check(ExileCenter.Core.IsSettingsMenuOpen, "Settings reopen cleanly after Save then Hide: " + width + "/" + fontScale);
            }
            Console.WriteLine("VALIDATION single screen: Save -> title-bar Hide -> reopen passed at three widths and two font scales; no OS or game input.");
        }
        finally
        {
            observer.Cancel(); visible.SetValue(null, previousVisible); page.SetValue(null, previousPage); offer.SetValue(null, previousOffer);
            ExileCenter.Core.GHSettings.SettingsOnSeparateWindow = previousSeparate;
            typeof(ExileCenter.Core).GetProperty("IsSettingsMenuOpen").SetValue(null, previousOpen);
            io.DisplaySize = previousDisplay; io.FontGlobalScale = previousScale; io.AddMousePosEvent(-100, -100); io.AddMouseButtonEvent(0, false);
        }
    }

    private static void CheckSettingsCaption()
    {
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var displayType=typeof(ExileCenter.SettingsDisplay);
        var settingsType=typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Settings.SettingsWindow",true);
        var display=RuntimeHelpers.GetUninitializedObject(displayType);
        displayType.GetField("useSettingsTitleBar",hidden).SetValue(display,true);
        displayType.GetField("minimumSize",hidden).SetValue(display,new System.Drawing.Size(840,640));
        uint style=unchecked((uint)Convert.ToInt32(displayType.GetProperty("WindowStyle",hidden).GetValue(display)));
        IntPtr window=CaptionNative.CreateWindowEx(0,"STATIC","ExileCenter hidden caption fixture",style,-1200,-700,1300,800,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        Check(window!=IntPtr.Zero,"A hidden native caption fixture is created without game input or focus");
        try
        {
            long actualStyle=CaptionNative.GetWindowLongPtr(window,-16).ToInt64();
            Check((actualStyle&0x00c00000)==0&&(actualStyle&0x00070000)==0x00070000,"The unified settings window has no second caption and retains resize, minimize and maximize styles");
            var caption=((float Height,float ActionsX))settingsType.GetProperty("CaptionBounds",hidden).GetValue(null);
            var procedure=displayType.GetMethod("WindowProcedure",hidden);
            IntPtr Hit(int x,int y)
            {
                var point=new CaptionNative.Point{X=x,Y=y};CaptionNative.ClientToScreen(window,ref point);
                long packed=(long)(ushort)point.X|((long)(ushort)point.Y<<16);
                return (IntPtr)procedure.Invoke(display,new object[]{window,0x0084u,UIntPtr.Zero,(IntPtr)packed});
            }
            Check(caption.Height>0&&caption.ActionsX>0,"Real toolbar geometry is available for native hit testing");
            Check(Hit(10,(int)(caption.Height/2))==(IntPtr)2,"The information and empty title area drag the native window, including on negative monitor coordinates");
            Check(Hit((int)caption.ActionsX+4,(int)(caption.Height/2))==(IntPtr)1,"Save, Update and window controls remain client clicks rather than dragging");
            Check(Hit(10,(int)caption.Height+20)==(IntPtr)1,"The ornamental header and content are not native drag zones");
            CaptionNative.GetWindowRect(window,out var bounds);
            long borderPoint=(long)(ushort)(bounds.Left+1)|((long)(ushort)((bounds.Top+bounds.Bottom)/2)<<16);
            Check((IntPtr)procedure.Invoke(display,new object[]{window,0x0084u,UIntPtr.Zero,(IntPtr)borderPoint})==(IntPtr)10,"The native left resize edge is preserved");
            var native=displayType.GetNestedType("Native",hidden);
            var limitsType=native.GetNestedType("MinMaxInfo",hidden);
            IntPtr limits=Marshal.AllocHGlobal(Marshal.SizeOf(limitsType));
            try
            {
                Marshal.StructureToPtr(Activator.CreateInstance(limitsType),limits,false);
                procedure.Invoke(display,new object[]{window,0x0024u,UIntPtr.Zero,limits});
                var result=Marshal.PtrToStructure(limits,limitsType);
                (int X,int Y) ReadPoint(string field)
                {
                    var point=limitsType.GetField(field,hidden).GetValue(result);
                    return ((int)point.GetType().GetField("X",hidden).GetValue(point),(int)point.GetType().GetField("Y",hidden).GetValue(point));
                }
                Check(ReadPoint("MinTrackSize")== (840,640),"The unified native window retains its minimum resize bounds");
                var monitor=native.GetMethod("MonitorFromWindow",hidden).Invoke(null,new object[]{window,2u});
                var infoType=native.GetNestedType("MonitorInfo",hidden);var info=Activator.CreateInstance(infoType);
                infoType.GetField("Size",hidden).SetValue(info,Marshal.SizeOf(infoType));
                var monitorArgs=new object[]{monitor,info};
                Check((bool)native.GetMethod("GetMonitorInfo",hidden).Invoke(null,monitorArgs),"The hidden caption fixture resolves its monitor work area");
                var work=infoType.GetField("Work",hidden).GetValue(monitorArgs[1]);
                int Edge(string edge)=>(int)work.GetType().GetField(edge,hidden).GetValue(work);
                Check(ReadPoint("MaxSize")== (Edge("Right")-Edge("Left"),Edge("Bottom")-Edge("Top")),"Maximize uses the monitor work area so the taskbar remains accessible");
            }
            finally { Marshal.FreeHGlobal(limits); }
            displayType.GetField("useSettingsTitleBar",hidden).SetValue(display,false);
            Check(Hit(10,(int)(caption.Height/2))==(IntPtr)1,"Detached plugin windows keep their existing native caption behavior");
            Check((Convert.ToUInt32(displayType.GetProperty("WindowStyle",hidden).GetValue(display))&0x00c00000)!=0,"The public plugin display constructor retains its caption style");
            displayType.GetMethod("RequestWindowState",hidden).Invoke(display,new object[]{6});
            Check((int)displayType.GetField("requestedWindowState",hidden).GetValue(display)==6,"Minimize is queued for the native frame boundary without changing focus during drawing");
            displayType.GetMethod("RequestWindowState",hidden).Invoke(display,new object[]{0});
            Check((int)displayType.GetField("requestedWindowState",hidden).GetValue(display)==0,"Maximize/restore is queued without changing the game or native fixture state");
        }
        finally { CaptionNative.DestroyWindow(window); }

        var io=ImGuiNET.ImGui.GetIO();
        var glyphButton=settingsType.GetMethod("DrawCaptionButton",hidden);
        foreach(int glyph in new[]{0,1,2})
        {
            Vector2 center=Vector2.Zero;bool clicked=false;
            for(int frame=0;frame<4;frame++)
            {
                io.AddMousePosEvent(frame==0?-100:center.X,frame==0?-100:center.Y);io.AddMouseButtonEvent(0,frame==2);
                ImGuiNET.ImGui.NewFrame();ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16));ImGuiNET.ImGui.SetNextWindowSize(new Vector2(240,80));
                ImGuiNET.ImGui.Begin("Caption controls##isolated",ImGuiNET.ImGuiWindowFlags.NoTitleBar|ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                clicked|=(bool)glyphButton.Invoke(null,new object[]{"##glyph",glyph,32f});
                center=(ImGuiNET.ImGui.GetItemRectMin()+ImGuiNET.ImGui.GetItemRectMax())/2;
                ImGuiNET.ImGui.End();ImGuiNET.ImGui.Render();
            }
            Check(clicked,"Each custom caption glyph responds to a real isolated ImGui press/release: "+glyph);
        }
    }

    private static class CaptionNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X,Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left,Top,Right,Bottom; }
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window,ref Point point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    }

    private static void SaveUiPreview(string file, byte[] atlas, int atlasWidth, int atlasHeight)
    {
        var data = ImGuiNET.ImGui.GetDrawData();
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>((int)data.DisplaySize.X, (int)data.DisplaySize.Y, new(22, 20, 17, 255));
        for (int listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
        {
            var list = data.CmdLists[listIndex];
            foreach (int commandIndex in Enumerable.Range(0, list.CmdBuffer.Size))
            {
                var command = list.CmdBuffer[commandIndex];
                if (command.UserCallback != IntPtr.Zero) continue;
                for (int offset = 0; offset < command.ElemCount; offset += 3)
                {
                    int At(int i) => list.IdxBuffer[(int)command.IdxOffset + offset + i] + (int)command.VtxOffset;
                    var a = list.VtxBuffer[At(0)]; var b = list.VtxBuffer[At(1)]; var c = list.VtxBuffer[At(2)];
                    var p = a.pos; var q = b.pos; var r = c.pos;
                    float denominator = (q.Y - r.Y) * (p.X - r.X) + (r.X - q.X) * (p.Y - r.Y);
                    if (Math.Abs(denominator) < .0001f) continue;
                    int minX = (int)Math.Max(0, Math.Max(command.ClipRect.X, Math.Floor(Math.Min(p.X, Math.Min(q.X, r.X)))));
                    int minY = (int)Math.Max(0, Math.Max(command.ClipRect.Y, Math.Floor(Math.Min(p.Y, Math.Min(q.Y, r.Y)))));
                    int maxX = (int)Math.Min(image.Width, Math.Min(command.ClipRect.Z, Math.Ceiling(Math.Max(p.X, Math.Max(q.X, r.X)))));
                    int maxY = (int)Math.Min(image.Height, Math.Min(command.ClipRect.W, Math.Ceiling(Math.Max(p.Y, Math.Max(q.Y, r.Y)))));
                    Vector4 Color(uint packed) => new(packed & 255, (packed >> 8) & 255, (packed >> 16) & 255, (packed >> 24) & 255);
                    var ca = Color(a.col); var cb = Color(b.col); var cc = Color(c.col);
                    for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++)
                    {
                        float u = ((q.Y - r.Y) * (x + .5f - r.X) + (r.X - q.X) * (y + .5f - r.Y)) / denominator;
                        float v = ((r.Y - p.Y) * (x + .5f - r.X) + (p.X - r.X) * (y + .5f - r.Y)) / denominator;
                        float w = 1 - u - v;
                        if (u < 0 || v < 0 || w < 0) continue;
                        var uv = a.uv * u + b.uv * v + c.uv * w;
                        int texel = (Math.Clamp((int)(uv.Y * atlasHeight), 0, atlasHeight - 1) * atlasWidth + Math.Clamp((int)(uv.X * atlasWidth), 0, atlasWidth - 1)) * 4;
                        var color = ca * u + cb * v + cc * w;
                        float alpha = color.W / 255f * atlas[texel + 3] / 255f;
                        if(command.TextureId==new IntPtr(2)&&framePreviewPixels!=null)
                        {
                            int textureX=Math.Clamp((int)(uv.X*framePreviewWidth),0,framePreviewWidth-1);
                            int textureY=Math.Clamp((int)(uv.Y*framePreviewHeight),0,framePreviewHeight-1);
                            var sample=framePreviewPixels[textureY*framePreviewWidth+textureX].ToVector4();
                            color.X*=sample.X;color.Y*=sample.Y;color.Z*=sample.Z;
                            alpha=color.W/255f*sample.W;
                        }
                        var old = image[x, y];
                        image[x, y] = new((byte)(color.X * alpha + old.R * (1 - alpha)), (byte)(color.Y * alpha + old.G * (1 - alpha)), (byte)(color.Z * alpha + old.B * (1 - alpha)), 255);
                    }
                }
            }
        }
        using var stream = File.Create(file);
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        // Closed panels must never enter ImGui or dereference transient game memory.
        // This fixture deliberately has no ImGui context and no attached game process.
        var core = new StashUtilityCore();
        var tabletIdentity = typeof(StashUtilityCore).GetMethod("IsTabletItem", BindingFlags.Static | BindingFlags.NonPublic);
        bool IsTablet(string path) => (bool)tabletIdentity.Invoke(null, new object[] { path });
        foreach (string id in new[] { "BreachAugment", "DeliriumAugment", "RitualAugment", "GenericAugment", "MapBossAugment", "AbyssAugment", "ExpeditionAugment", "IncursionAugment" })
            Check(IsTablet("Metadata/Items/TowerAugment/" + id), "True tablet remains recognized: " + id);
        foreach (string path in new[] { null, "", "Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Metadata/Items/Currency/Tablet", "Metadata/Items/Other/TowerAugment/Fake", "Metadata/Items/TowerAugmentFake/Tablet" })
            Check(!IsTablet(path), "Non-tablet identifier rejected: " + path);
        var occlusions = (IList)typeof(StashUtilityCore).GetField("tabletOcclusions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(core);
        var updateOcclusions = typeof(StashUtilityCore).GetMethod("UpdateTabletOcclusions", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 1000; i++)
        {
            occlusions.Add((Vector2.Zero, Vector2.One));
            updateOcclusions.Invoke(core, new object[] { new IntPtr(i == 0 ? 0 : -1) });
        }
        Check(occlusions.Count == 0, "Closed panels clear stale covers without game memory or ImGui calls");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var callbackType = typeof(StashUtilityCore).GetNestedType("TabletWindowCallback", BindingFlags.NonPublic);
            var enumerate = typeof(StashUtilityCore).GetMethod("TabletEnumWindows", BindingFlags.Static | BindingFlags.NonPublic);
            var callback = Delegate.CreateDelegate(callbackType, typeof(TabletRankingCheck).GetMethod("CountWindow", BindingFlags.Static | BindingFlags.NonPublic));
            nativeWindows = 0;
            for (int i = 0; i < 100; i++) enumerate.Invoke(null, new object[] { callback, IntPtr.Zero });
            Check(nativeWindows >= 100, "Repeated real Windows callbacks marshal correctly");
            callback = Delegate.CreateDelegate(callbackType, typeof(TabletRankingCheck).GetMethod("StopWindows", BindingFlags.Static | BindingFlags.NonPublic));
            nativeWindows = 0;
            enumerate.Invoke(null, new object[] { callback, IntPtr.Zero });
            Check(nativeWindows == 1, "Native callback can safely stop enumeration");
        }
        CheckDynamicQuality();
        string[] grades = { "S", "A", "B", "C", "D", "E", "F" };
        // Independent synthetic scores exercise the existing grid planner below.
        var results = Enumerable.Range(0, 210).ToDictionary(i => i.ToString(),
            i => new TabletClassification("S", 0, "S2A2", Vector4.One, i));

        var analyze = typeof(StashUtilityCore).GetMethod("ClassifyTabletMods", BindingFlags.Static | BindingFlags.NonPublic);
        var match = typeof(StashUtilityCore).GetMethod("MatchesTabletSelection", BindingFlags.Static | BindingFlags.NonPublic);
        bool Matches(Mods mods, TabletProfile profile, string type) => (bool)match.Invoke(null, new object[] { mods, profile, type });
        TabletClassification Analyze(Mods mods, TabletProfile profile, string type) =>
            (TabletClassification)analyze.Invoke(null, new object[] { mods, profile, type });
        foreach (string tabletType in StashUtilitySettings.TabletTypes)
        {
            var available = ModDatabase.AllTabletMods.Where(m => m.TabletTypes.Contains(tabletType)).GroupBy(m => m.Id).Select(g => g.First()).ToArray();
            var definitions = available.Where(m => m.Affix == "prefix").Take(2)
                .Concat(available.Where(m => m.Affix == "suffix").Take(2)).ToArray();
            Check(definitions.Length == 4, "Catalog coverage " + tabletType);
            var profile = new TabletProfile();
            var mods = new Mods(IntPtr.Zero);
            for (int i = 0; i < 4; i++)
            {
                profile.ModRanks[definitions[i].Id] = grades[i + 1];
                profile.GoodModPatterns.Add(definitions[i].Id);
                profile.ModRequiredMinRolls[definitions[i].Id] = definitions[i].MinRoll;
                mods.ExplicitMods.Add((definitions[i].Id, (definitions[i].MinRoll * definitions[i].RollDivisor, definitions[i].MinRoll * definitions[i].RollDivisor)));
            }
            var result = Analyze(mods, profile, tabletType);
            Check(result.Rank == "A" && result.RankIndex == 0, "Best custom grade A without a claimed ordinal " + tabletType);
            Check(!profile.RequireAllRankedMods && Matches(mods, profile, tabletType), "Existing choices default to OU " + tabletType);
            profile.RequireAllRankedMods = true;
            Check(Matches(mods, profile, tabletType), "ET accepts all four selected modifiers " + tabletType);
            var missing = new Mods(IntPtr.Zero);
            missing.ExplicitMods.AddRange(mods.ExplicitMods.Take(3));
            Check(!Matches(missing, profile, tabletType), "ET rejects a missing selected modifier " + tabletType);
            Check(Analyze(missing, profile, tabletType).Rank == "A", "ET rejection preserves the best rank independently of rejection " + tabletType);
            profile.RequireAllRankedMods = false;
            Check(Matches(missing, profile, tabletType), "OU accepts a partial selection " + tabletType);
            foreach (bool all in new[] { false, true })
                Check(!Matches(mods, new TabletProfile { RequireAllRankedMods = all }, tabletType), "An empty selection never accepts a tablet " + all + tabletType);
            Check(!Matches(null, profile, tabletType), "Unreadable modifiers cannot pass " + tabletType);
            string saved = JsonConvert.SerializeObject(profile);
            mods.ExplicitMods.Reverse();
            Check(Analyze(mods, profile, tabletType) == result, "Raw modifier order " + tabletType);
            mods.ImplicitMods.Add(mods.ExplicitMods[0]);
            mods.EnchantMods.Add(mods.ExplicitMods[0]);
            Check(Analyze(mods, profile, tabletType) == result, "Only four explicit affixes determine grade " + tabletType);
            Check(JsonConvert.SerializeObject(profile) == saved, "Ranks and minimum filters untouched " + tabletType);
            var reloaded = JsonConvert.DeserializeObject<TabletProfile>(saved);
            Check(Analyze(mods, reloaded, tabletType) == result, "Save and reload " + tabletType);
            var isolated = new TabletProfile();
            var top = new Mods(IntPtr.Zero);
            top.ExplicitMods.Add((definitions[2].Id, (10000f, 10000f)));
            isolated.ModRanks[definitions[2].Id] = "S";
            Check(Analyze(top, isolated, tabletType).Rank == "S" && Analyze(top, isolated, tabletType).RankIndex == 0, "One S keeps grade S without a fabricated ordinal " + tabletType);
            isolated.ModRanks[definitions[3].Id] = "S";
            Check(Analyze(top, isolated, tabletType).Rank == "S" && Analyze(top, isolated, tabletType).RankIndex == 0, "Selecting another S does not invent a global position " + tabletType);
            top.ExplicitMods.Add((definitions[3].Id, (10000f, 10000f)));
            Check(Analyze(top, isolated, tabletType).Rank == "S" && Analyze(top, isolated, tabletType).RankIndex == 0, "Two S matches retain S without a claimed ordinal " + tabletType);
            var thirdSuffix = available.First(m => m.Affix == "suffix" && m.Id != definitions[2].Id && m.Id != definitions[3].Id);
            isolated.ModRanks[thirdSuffix.Id] = "S";
            Check(Analyze(top, isolated, tabletType).Rank == "S" && Analyze(top, isolated, tabletType).RankIndex == 0, "Selecting an absent S does not invent an ordinal " + tabletType);
            isolated.ModRanks[definitions[0].Id] = "A";
            isolated.ModRanks[definitions[1].Id] = "A";
            top.ExplicitMods.Add((definitions[0].Id, (10000f, 10000f)));
            top.ExplicitMods.Add((definitions[1].Id, (10000f, 10000f)));
            Check(Analyze(top, isolated, tabletType).Rank == "S" && Analyze(top, isolated, tabletType).RankIndex == 0, "Two S and two A retain S " + tabletType);
            var singleA = new Mods(IntPtr.Zero);
            singleA.ExplicitMods.Add((definitions[0].Id, (10000f, 10000f)));
            Check(Analyze(singleA, isolated, tabletType).Rank == "A" && Analyze(singleA, isolated, tabletType).RankIndex == 0, "Partial A retains A without claiming an exact global position " + tabletType);
            isolated.ModRanks.Remove(definitions[1].Id);
            Check(Analyze(singleA, isolated, tabletType).Rank == "A" && Analyze(singleA, isolated, tabletType).RankIndex == 0, "Selection changes are evaluated without restart " + tabletType);
            Check(Analyze(mods, reloaded, tabletType) == result, "Another preset cannot contaminate a restored profile's ranking " + tabletType);
            profile.ModRanks[definitions[0].Id] = "unknown";
            Check(Analyze(mods, profile, tabletType).Rank == "B", "Invalid rank cannot suppress another matching rank " + tabletType);
            profile.ModRanks.Remove(definitions[0].Id);
            Check(Analyze(mods, profile, tabletType).Rank == "A", "Legacy selected modifiers remain readable " + tabletType);
            profile.GoodModPatterns.Remove(definitions[0].Id);
            Check(Analyze(mods, profile, tabletType).Rank == "B", "Unchecked modifier never falls back to a reference rank " + tabletType);
            profile.ModRanks[definitions[0].Id] = "S";
            Check(Analyze(mods, profile, tabletType).Rank == "S", "Rank edit immediately recomputed " + tabletType);
            mods.ExplicitMods.RemoveAt(0);
            Check(Analyze(mods, profile, tabletType).Rank == "S", "Three explicit affixes still keep their best selected rank " + tabletType);
            var variable = definitions.FirstOrDefault(d => d.MinRoll != d.MaxRoll);
            if (variable != null)
            {
                var partial = new Mods(IntPtr.Zero);
                partial.ExplicitMods.Add((variable.Id, (variable.MinRoll * variable.RollDivisor, variable.MinRoll * variable.RollDivisor)));
                partial.ExplicitMods.Add(("UnknownExplicit", (0f, 0f)));
                var choice = new TabletProfile(); choice.ModRanks[variable.Id] = "S";
                choice.ModRequiredMinRolls[variable.Id] = variable.MaxRoll;
                Check(!Analyze(partial, choice, tabletType).IsRanked, "Below-minimum S does not become a matching priority " + tabletType);
                Check(!Matches(partial, choice, tabletType), "OU rejects a below-minimum match " + tabletType);
                choice.RequireAllRankedMods = true;
                Check(!Matches(partial, choice, tabletType), "ET rejects a below-minimum match " + tabletType);
                choice.ModRequiredMinRolls[variable.Id] = variable.MinRoll;
                Check(Matches(partial, choice, tabletType), "ET accepts a single selected modifier at its minimum " + tabletType);
                var second = definitions.First(d => d.Id != variable.Id);
                choice.ModRanks[second.Id] = "A";
                partial.ExplicitMods.Add((second.Id, (10000f, 10000f)));
                choice.ModRequiredMinRolls[variable.Id] = variable.MaxRoll;
                Check(!Matches(partial, choice, tabletType), "ET checks each minimum even if another selected mod qualifies " + tabletType);
                choice.RequireAllRankedMods = false;
                Check(Matches(partial, choice, tabletType), "OU accepts another qualifying mod when one selected minimum fails " + tabletType);
                partial.ExplicitMods.RemoveAt(partial.ExplicitMods.Count - 1);
                choice.ModRanks.Remove(second.Id);
                choice.ModRequiredMinRolls[variable.Id] = variable.MinRoll;
                Check(Analyze(partial, choice, tabletType).Rank == "S", "One qualifying S wins despite unrelated unknown modifiers " + tabletType);
                choice.ModRanks.Remove(variable.Id);
                Check(!Analyze(partial, choice, tabletType).IsRanked, "Unchecking the only ranked modifier removes its priority " + tabletType);
            }
        }

        // Exercise the actual existing grid planner; no input is sent to the game.
        var type = typeof(StashUtilityCore).GetNestedType("SortItem", BindingFlags.NonPublic);
        var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        var reverse = results.Values.OrderByDescending(v => v.SortScore).ToArray();
        for (int i = 0; i < reverse.Length; i++)
        {
            var item = Activator.CreateInstance(type, true);
            void Set(string field, object value) => type.GetField(field).SetValue(item, value);
            Set("Address", new IntPtr(i + 1)); Set("X", i % 12); Set("Y", i / 12); Set("Width", 1); Set("Height", 1);
            Set("Kind", i % 2 == 0 ? "TowerAugment/Breach" : "TowerAugment/Delirium");
            Set("IsTablet", true); Set("TabletSortScore", reverse[i].SortScore); items.Add(item);
        }
        var planner = typeof(StashUtilityCore).GetMethod("BuildSortTargets", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (bool byType in new[] { true, false })
        {
            var targets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 18, new HashSet<int>(), byType });
            Check(targets != null && targets.Count == 210, "Planner keeps all tablets");
            for (int i = 0; i < reverse.Length; i++)
            {
                var target = targets[new IntPtr(i + 1)];
                Check(target.y * 12 + target.x == reverse[i].SortScore, "Real numeric grid order " + reverse[i].RankLabel);
            }
            var protectedTargets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 18, new HashSet<int> { 0 }, byType });
            Check(protectedTargets[new IntPtr(1)] == (0, 0), "Protected tablet stays in place");
            Check(protectedTargets[new IntPtr(210)] == (1, 0), "Best movable tablet follows protected cell");
        }
        var moved = type.GetMethod("At").Invoke(items[0], new object[] { 4, 5 });
        Check((bool)type.GetField("IsTablet").GetValue(moved) && (int)type.GetField("TabletSortScore").GetValue(moved) == reverse[0].SortScore, "Rank retained across planned moves");
        for(int i=0;i<items.Count;i++)
        {
            type.GetField("TabletSortScore").SetValue(items[i],0);
            type.GetField("TabletOrderKey").SetValue(items[i],(items.Count-1-i).ToString("D4"));
        }
        foreach(bool byType in new[]{true,false})
        {
            var targets=(Dictionary<IntPtr,(int x,int y)>)planner.Invoke(null,new object[]{items,12,18,new HashSet<int>(),byType});
            for(int i=0;i<items.Count;i++)
            {
                var target=targets[new IntPtr(i+1)];
                Check(target.y*12+target.x==items.Count-1-i,"Actual grid uses full quality order within one letter");
            }
        }
        moved=type.GetMethod("At").Invoke(items[0],new object[]{4,5});
        Check((string)type.GetField("TabletOrderKey").GetValue(moved)=="0209","Full quality key survives planned moves");
    }
}
