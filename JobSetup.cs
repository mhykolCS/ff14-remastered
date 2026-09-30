using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game;
using Newtonsoft.Json;
using NativePlayer=FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace CinematicMode;

public sealed unsafe class JobSetup : IDisposable
{
    readonly ICommandManager commands;
    readonly IChatGui chat;
    readonly IPluginLog log;
    readonly ICondition condition;
    readonly IDataManager data;
    readonly IPlayerState player;
    readonly string directory;
    readonly Queue<byte> queue=new();
    readonly List<object> results=new();
    bool creating;
    int originalGearset=-1;
    byte? pending;
    long started;
    public bool Busy => pending!=null || queue.Count>0;
    public JobSetup(IDalamudPluginInterface pi,ICommandManager commands,IChatGui chat,IPluginLog log,ICondition condition,IDataManager data,IPlayerState player)
    {
        this.commands=commands;this.chat=chat;this.log=log;this.condition=condition;this.data=data;this.player=player;
        directory=pi.GetPluginConfigDirectory();
        commands.AddHandler("/jobsetup",new CommandInfo(OnCommand){HelpMessage="Job setup: preview recommended owned equipment; applybars applies your prepared keyboard/mouse layouts."});
    }
    bool Safe => player.IsLoaded && !condition[ConditionFlag.InCombat] && !condition[ConditionFlag.Crafting] && !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.WatchingCutscene];
    void OnCommand(string cmd,string args)
    {
        try {
            if(!Safe || Busy) {chat.PrintError("Finish the current activity before setting up jobs.");return;}
            if(args.Trim()=="verify") { Verify(); return; }
            if(args.Trim()=="applybars") { ApplyBars(); return; }
            if(args.Trim() is "preview" or "create") {
                creating=args.Trim()=="create";
                if(creating && File.Exists(Path.Combine(directory,"created-gearsets.json"))) { chat.PrintError("Gear sets were already created; use the native gear set list to update them.");return; }
                if(creating) {
                    var gm=RaptureGearsetModule.Instance();
                    originalGearset=gm->CurrentGearsetIndex;
                    if(!gm->IsValidGearset(originalGearset)) {
                        originalGearset=gm->CreateGearset();
                        if(originalGearset==255) throw new InvalidOperationException("No empty gear set slot for the original outfit backup.");
                        gm->GetGearset(originalGearset)->NameString="Original outfit";
                        gm->SaveFile(true);
                    }
                }
                results.Clear();
                var native=NativePlayer.Instance();
                foreach(var job in data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!)
                    if(job.RowId is >=8 and <=42 && (job.RowId<=18 || job.RowId>=19 && job.RowId!=26 && job.RowId!=29) && native->GetClassJobLevel((int)job.RowId,false)>0)
                        queue.Enqueue((byte)job.RowId);
                chat.Print("Checking native recommended equipment for each unlocked job.");
            }
        } catch(Exception ex) {log.Error(ex,"Job setup failed");chat.PrintError(ex.Message);}
    }
    public void Tick()
    {
        if(!Busy) return;
        try {
            if(!Safe) {queue.Clear(); pending=null;return;}
            var r=RecommendEquipModule.Instance();
            if(r==null) return;
            if(pending==null) {
                if(r->IsUpdating) return;
                pending=queue.Dequeue(); started=Environment.TickCount64;
                if(!r->SetupForClassJob(pending.Value)) throw new InvalidOperationException("Recommended equipment could not start.");
                return;
            }
            if(r->IsUpdating) {
                if(Environment.TickCount64-started>15000) throw new TimeoutException("Native recommended equipment timed out.");
                return;
            }
            var items=new List<object>();
            for(int i=0;i<14;i++) {
                var item=r->RecommendedItems[i].Value;
                if(item==null || item->GetItemId()==0) {items.Add(new{Slot=i,Id=0});continue;}
                var row=data.GetExcelSheet<Lumina.Excel.Sheets.Item>()!.GetRow(item->GetBaseItemId());
                items.Add(new{Slot=i,Id=item->GetItemId(),Name=row.Name.ExtractText(),ILevel=row.LevelItem.RowId,
                    Glamour=item->GetGlamourId(),Stains=new[]{item->GetStain(0),item->GetStain(1)},
                    Materia=Enumerable.Range(0,5).Select(n=>item->GetMateriaId((byte)n)).ToArray(),
                    Grades=Enumerable.Range(0,5).Select(n=>item->GetMateriaGrade((byte)n)).ToArray()});
            }
            int created=-1; string? reason=null;
            if(creating) {
                var main=r->RecommendedItems[0].Value;
                var soul=r->RecommendedItems[13].Value;
                if(main==null || main->GetItemId()==0) reason="No usable weapon found by Recommended Gear";
                else if(pending.Value>=19 && (soul==null || soul->GetItemId()==0)) reason="Required soul crystal missing";
                else {
                    var gm=RaptureGearsetModule.Instance();
                    created=gm->CreateGearset();
                    if(created==255) throw new InvalidOperationException("No empty gear set slot.");
                    var entry=gm->GetGearset(created);
                    entry->ClassJob=pending.Value; entry->GlamourSetLink=0; entry->BannerIndex=0;
                    var abbreviation=data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!.GetRow(pending.Value).Abbreviation.ExtractText();
                    entry->NameString=abbreviation+" Recommended";
                    entry->Flags &= ~RaptureGearsetModule.GearsetFlag.MainHandMissing;
                    uint itemLevelSum=0; uint weaponLevel=0; bool offhand=false;
                    for(int i=0;i<14;i++) {
                        entry->Items[i]=default;
                        var item=r->RecommendedItems[i].Value;
                        if(item==null || item->GetItemId()==0) continue;
                        ref var target=ref entry->Items[i];
                        target.ItemId=item->GetItemId();target.GlamourId=item->GetGlamourId();
                        target.Stain0Id=item->GetStain(0); target.Stain1Id=item->GetStain(1);
                        for(byte m=0;m<5;m++){target.Materia[m]=item->GetMateriaId(m);target.MateriaGrades[m]=item->GetMateriaGrade(m);}
                        var level=data.GetExcelSheet<Lumina.Excel.Sheets.Item>()!.GetRow(item->GetBaseItemId()).LevelItem.RowId;
                        if(i!=5 && i!=13) itemLevelSum+=level;
                        if(i==0) weaponLevel=level;
                        if(i==1) offhand=true;
                    }
                    if(!offhand) itemLevelSum+=weaponLevel;
                    entry->ItemLevel=(short)(itemLevelSum/12);
                    gm->CurrentGearsetIndex=originalGearset;
                    gm->SaveFile(true);
                    if(!gm->IsValidGearset(created)) throw new InvalidOperationException("Created gear set failed native validation.");
                }
            }
            results.Add(new{Job=pending.Value,NativeJob=r->ClassJob,Level=r->Level,Created=created,Reason=reason,Items=items});
            pending=null;r->Clear();
            File.WriteAllText(Path.Combine(directory,creating?"created-gearsets.json":"recommended-gear-preview.json"),JsonConvert.SerializeObject(results,Formatting.Indented));
            if(queue.Count==0) {JobCatalog.Export(data,directory);chat.Print($"Recommended equipment {(creating?"saved":"checked")} for {results.Count} jobs; see the job menu for availability.");}
        } catch(Exception ex) {queue.Clear();pending=null;log.Error(ex,"Job equipment preview failed");chat.PrintError(ex.Message);}
    }
    sealed class BarPlan { public uint Job {get;set;} public string Abbreviation=""; public List<SlotPlan> Slots=new(); }
    sealed class SlotPlan { public uint Bar {get;set;} public uint Slot {get;set;} public uint Id {get;set;} public string Type="Empty",Name="",Key=""; }
    void ApplyBars() {
        var h=RaptureHotbarModule.Instance();
        if(h==null || !h->ModuleReady || h->PvPHotbarsActive) throw new InvalidOperationException("PvE hotbars are not ready.");
        var plans=JsonConvert.DeserializeObject<List<BarPlan>>(File.ReadAllText(Path.Combine(directory,"job-bars.json")))!;
        if(plans.Count!=43 || plans.Select(p=>p.Job).Distinct().Count()!=43) throw new InvalidOperationException("Incomplete job plan.");
        foreach(var plan in plans) {
            if(plan.Job is <1 or >43 || plan.Slots.Count!=36 || plan.Slots.Select(s=>(s.Bar,s.Slot)).Distinct().Count()!=36)
                throw new InvalidOperationException("Invalid bar plan.");
            foreach(var s in plan.Slots) {
                if(s.Bar>2 || s.Slot>11 || !Enum.TryParse<RaptureHotbarModule.HotbarSlotType>(s.Type,out _)) throw new InvalidOperationException("Invalid slot.");
                if(s.Id!=0 && s.Type is "Action" or "CraftAction" && (s.Id<100000 ? !data.GetExcelSheet<Lumina.Excel.Sheets.Action>()!.HasRow(s.Id) : !data.GetExcelSheet<Lumina.Excel.Sheets.CraftAction>()!.HasRow(s.Id))) throw new InvalidOperationException("Unknown action.");
                if(s.Type=="GeneralAction" && data.GetExcelSheet<Lumina.Excel.Sheets.GeneralAction>()!.GetRow(s.Id).Name.ExtractText()!=s.Name) throw new InvalidOperationException("General action name mismatch: "+s.Name);
            }
        }
        var before=new List<object>();
        foreach(var plan in plans) foreach(var s in plan.Slots) {
            var b=h->SavedHotbars[(int)plan.Job].Hotbars[(int)s.Bar].Slots[(int)s.Slot];
            before.Add(new{plan.Job,s.Bar,s.Slot,Type=b.CommandType.ToString(),Id=b.CommandId});
        }
        var backup=Path.Combine(directory,"all-job-bars-before.json");
        if(!File.Exists(backup)) File.WriteAllText(backup,JsonConvert.SerializeObject(before,Formatting.Indented));
        var blu=plans.Single(p=>p.Job==36);
        var spellSlots=blu.Slots.Where(s=>s.Type!="GeneralAction" && s.Id is not (7560 or 7561 or 7562 or 7559)).Take(24).ToArray();
        var active=Enumerable.Range(0,24).Select(i=>ActionManager.Instance()->GetActiveBlueMageActionInSlot(i)).Where(i=>i!=0).ToArray();
        for(int i=0;i<spellSlots.Length;i++) {
            var s=spellSlots[i]; s.Id=i<active.Length?active[i]:0; s.Type=s.Id==0?"Empty":"Action";
            s.Name=s.Id==0?"":data.GetExcelSheet<Lumina.Excel.Sheets.Action>()!.GetRow(s.Id).Name.ExtractText();
        }
        foreach(var plan in plans) foreach(var s in plan.Slots) {
            RaptureHotbarModule.HotbarSlot slot=default;
            slot.CommandType=Enum.Parse<RaptureHotbarModule.HotbarSlotType>(s.Type); slot.CommandId=s.Id;
            h->WriteSavedSlot(plan.Job,s.Bar,s.Slot,&slot,true,false);
        }
        for(uint b=0;b<3;b++) h->LoadSavedHotbar(h->ActiveHotbarClassJobId,b);
        h->SaveFile(true);
        var verified=0;
        foreach(var plan in plans) foreach(var s in plan.Slots) {
            var b=h->SavedHotbars[(int)plan.Job].Hotbars[(int)s.Bar].Slots[(int)s.Slot];
            if(b.CommandId!=s.Id || b.CommandType.ToString()!=s.Type) throw new InvalidOperationException("Bar readback mismatch: "+plan.Abbreviation);
            verified++;
        }
        File.WriteAllText(Path.Combine(directory,"all-job-bars-applied.json"),JsonConvert.SerializeObject(new{Character=player.ContentId,VerifiedSlots=verified,Plans=plans},Formatting.Indented));
        chat.Print($"Verified {verified} slots across {plans.Count} job/class layouts. Plain / Shift / Ctrl use the same G502 keys.");
    }
    void Verify() {
        var h=RaptureHotbarModule.Instance();var gm=RaptureGearsetModule.Instance();
        var applied=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(directory,"all-job-bars-applied.json")));
        var plans=applied["Plans"]!.ToObject<List<BarPlan>>()!;
        var mismatches=new List<object>();int checkedSlots=0;
        foreach(var plan in plans) foreach(var s in plan.Slots) {
            var actual=h->SavedHotbars[(int)plan.Job].Hotbars[(int)s.Bar].Slots[(int)s.Slot];checkedSlots++;
            if(actual.CommandId!=s.Id || actual.CommandType.ToString()!=s.Type) mismatches.Add(new{Job=plan.Abbreviation,s.Bar,s.Slot,Expected=s.Id,Actual=actual.CommandId});
        }
        var sets=new List<object>();
        for(int i=0;i<100;i++) {var e=gm->GetGearset(i);if(e!=null && e->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))sets.Add(new{Number=i+1,ClassJob=e->ClassJob,Name=e->NameString,Valid=gm->IsValidGearset(i),Glamour=e->GlamourSetLink,Weapon=e->Items[0].ItemId});}
        File.WriteAllText(Path.Combine(directory,"final-job-verification.json"),JsonConvert.SerializeObject(new{Time=DateTimeOffset.Now,Character=player.ContentId,CheckedSlots=checkedSlots,Mismatches=mismatches,CurrentJob=NativePlayer.Instance()->CurrentClassJobId,CurrentGearset=gm->CurrentGearsetIndex+1,PvpIndices=Enumerable.Range(19,24).Select(j=>new{Job=j,Index=h->GetPvPSavedHotbarIndexForClassJobId((uint)j)}).ToArray(),Sets=sets},Formatting.Indented));
        chat.Print($"Job verification: {checkedSlots} slots checked, {mismatches.Count} differences, {sets.Count} valid saved sets inspected.");
    }
    public void Dispose(){commands.RemoveHandler("/jobsetup");}
}
