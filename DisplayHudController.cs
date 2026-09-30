using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;

namespace CinematicMode;

public sealed class DisplayHudSettings
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "auto";
    public bool LayoutsInitialized { get; set; }
    public bool InitializeLayouts { get; set; }
    public bool SetUpMonkHotbars { get; set; }
    public bool SetUpTweaks { get; set; }
    public ulong CharacterId { get; set; }
    public int CombatLayout { get; set; } = 2;
    public int RpLayout { get; set; } = 3;
    public bool ExpandChat { get; set; } = true;
    public bool SwitchChatTab { get; set; } = true;
}

/// <summary>Local display profiles. Runs on the game framework thread; never sends player chat.</summary>
public sealed unsafe class DisplayHudController : IDisposable
{
    readonly IDalamudPluginInterface pi;
    readonly IClientState client;
    readonly IPlayerState player;
    readonly IGameGui gui;
    readonly ICommandManager commands;
    readonly IChatGui chat;
    readonly IPluginLog log;
    readonly ICondition condition;
    readonly IDataManager data;
    readonly IGameConfig gameConfig;
    readonly string directory, configPath;
    DisplayHudSettings settings = new();
    DateTime configTime;
    long nextPoll, nextStatus, pendingSince;
    string pending = "", applied = "", contextMode="";
    int appliedWidth, appliedHeight;
    nint hwnd;
    string? error;

    public DisplayHudController(IDalamudPluginInterface pi, IClientState client, IPlayerState player,
        IGameGui gui, ICommandManager commands, IChatGui chat, IPluginLog log, ICondition condition, IDataManager data, IGameConfig gameConfig)
    {
        this.pi=pi; this.client=client; this.player=player; this.gui=gui; this.commands=commands;
        this.chat=chat; this.log=log; this.condition=condition; this.data=data; this.gameConfig=gameConfig;
        directory=pi.GetPluginConfigDirectory(); Directory.CreateDirectory(directory);
        configPath=Path.Combine(directory,"display-hud.json");
        if (!File.Exists(configPath)) SaveSettings();
        ReloadSettings();
        commands.AddHandler("/displayhud",new CommandInfo(OnCommand)
        { HelpMessage="Display HUD: auto, combat, rp, off, status. Ultrawide = combat; laptop = chat/RP. Combat always restores the combat HUD." });
    }

    void SaveSettings()
    {
        File.WriteAllText(configPath,JsonConvert.SerializeObject(settings,Formatting.Indented));
        configTime=File.GetLastWriteTimeUtc(configPath);
    }

    void ReloadSettings()
    {
        var time=File.GetLastWriteTimeUtc(configPath);
        if (time==configTime && nextPoll!=0) return;
        var value=JsonConvert.DeserializeObject<DisplayHudSettings>(File.ReadAllText(configPath));
        if (value==null || value.CombatLayout is <1 or >4 || value.RpLayout is <1 or >4 || value.CombatLayout==value.RpLayout
            || value.Mode is not ("auto" or "combat" or "rp" or "off"))
            throw new InvalidOperationException("Invalid HUD profile configuration.");
        settings=value; configTime=time; applied=""; pending="";
    }

    public void Tick(bool cinematic)
    {
        var now=Environment.TickCount64;
        if (now<nextPoll) return;
        nextPoll=now+250;
        try
        {
            ReloadSettings();
            if (!client.IsLoggedIn || !player.IsLoaded) return;
            var config=AddonConfig.Instance();
            if (config==null || !config->IsLoaded || config->ActiveDataSet==null) return;
            var display=ReadDisplay();
            // Mode-dependent overlays and Direct Chat must follow the display even
            // while the native HUD is temporarily hidden by minimal mode.
            if(settings.Enabled && settings.Mode!="off")
                contextMode=DisplayHudPolicy.SelectMode(settings.Mode,display.MonitorWidth,display.MonitorHeight,condition[ConditionFlag.InCombat]);
            else contextMode=applied;
            if (now>=nextStatus)
            {
                DumpStatus(display,cinematic); nextStatus=now+30000;
            }
            if (settings.CharacterId!=0 && settings.CharacterId!=player.ContentId) return;
            if (cinematic || EditorOpen() || condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78]) return;
            if (settings.InitializeLayouts) InitializeLayouts();
            if (settings.SetUpMonkHotbars && !condition[ConditionFlag.InCombat]) SetUpMonkHotbars();
            if (settings.SetUpTweaks) SetUpTweaks();
            if (!settings.Enabled || !settings.LayoutsInitialized || settings.Mode=="off" || display.ClientWidth<640 || display.ClientHeight<480) return;
            var inCombat=condition[ConditionFlag.InCombat];
            var desired=DisplayHudPolicy.SelectMode(settings.Mode,display.MonitorWidth,display.MonitorHeight,inCombat);
            var key=$"{desired}:{display.ClientWidth}x{display.ClientHeight}";
            if (pending!=key) { pending=key; pendingSince=now; }
            if ((applied==desired && appliedWidth==display.ClientWidth && appliedHeight==display.ClientHeight) || !DisplayHudPolicy.HasSettled(now,pendingSince,inCombat)) return;
            var index=(uint)((desired=="combat"?settings.CombatLayout:settings.RpLayout)-1);
            config->ChangeHudLayout(index);
            if (settings.ExpandChat) ApplyChat(desired,display);
            applied=desired; appliedWidth=display.ClientWidth; appliedHeight=display.ClientHeight; error=null;
            log.Information($"Display HUD -> {desired}; HUD {index+1}; monitor {display.MonitorWidth}x{display.MonitorHeight}.");
            DumpStatus(display,cinematic);
        }
        catch(Exception ex)
        {
            error=ex.Message; log.Error(ex,"Display HUD suspended after an error.");
            settings.Enabled=false; settings.InitializeLayouts=false; settings.SetUpMonkHotbars=false; settings.SetUpTweaks=false; SaveSettings();
        }
    }

    public string CurrentMode=>string.IsNullOrEmpty(contextMode)?applied:contextMode;

    public void ResetSession() { applied=""; pending=""; contextMode=""; hwnd=0; }

    bool EditorOpen() => new[]{"ConfigAddon","HudLayout","ConfigHUD","ConfigKeybind","ConfigCharacter","ConfigLog","ConfigLogFilter"}.Any(n=>
    { var a=gui.GetAddonByName(n); return !a.IsNull && a.IsVisible; });

    void InitializeLayouts()
    {
        var config=AddonConfig.Instance();
        var ds=config->ActiveDataSet;
        var combat=settings.CombatLayout-1; var rp=settings.RpLayout-1;
        if(FindEntry(combat,"_ActionBar")==null) throw new InvalidOperationException("Combat layout has not been saved yet.");
        var backup=Path.Combine(directory,"layouts-before-initialization.json");
        if(!File.Exists(backup)) File.Copy(Path.Combine(directory,"display-hud-status.json"),backup);
        // ChangeHudLayout saves the outgoing live HUD. Switch away before editing
        // stored profiles, otherwise that save would replace the corrected entries.
        if(ds->CurrentHudLayout==(uint)combat || ds->CurrentHudLayout==(uint)rp)
            config->ChangeHudLayout((uint)Enumerable.Range(0,4).First(i=>i!=combat && i!=rp));

        // Correct the central combat group using native HUD anchors; preserve other elements.
        Place(combat,"_ActionBar",50,92.7f,7,1.2f,true);
        Place(combat,"_ActionBar01",50,87.9f,7,1.2f,true);
        Place(combat,"_ActionBar02",50,83.1f,7,1.2f,true);
        Place(combat,"_ActionBar03",81,97,7,1,true);
        Place(combat,"_PartyList",28,36,0,1,true);
        Place(combat,"_EnemyList",62,48,0,1.1f,true);
        Place(combat,"_FocusTargetInfo",62,39,0,1.1f,true);
        Place(combat,"_TargetInfoMainTarget",50,17.5f,1,1.2f,true);
        Place(combat,"_TargetInfoBuffDebuff",50,24,1,1,true);
        Place(combat,"_TargetInfoCastBar",50,56.5f,4,1.6f,true);
        Place(combat,"_CastBar",50,59.5f,4,1.2f,true);
        Place(combat,"_LimitBreak",50,52.3f,4,1,true);
        Place(combat,"JobHudMNK0",45.7f,65,4,1.1f,true);
        Place(combat,"JobHudMNK1",55.5f,64,4,1.1f,true);
        Place(combat,"_StatusCustom0",45,75,7,1.1f,true);
        Place(combat,"_StatusCustom1",56,75,7,1.2f,true);
        Place(combat,"_StatusCustom3",50,69,4,1.2f,true);
        foreach(var n in new[]{"_ActionCross","_ActionDoubleCrossL","_ActionDoubleCrossR"}) Visibility(combat,n,false);

        // Native layouts share the same addon hashes; clone all 112 entries, then simplify RP.
        ds->HudLayoutConfigEntries.Slice(combat*112,112).CopyTo(ds->HudLayoutConfigEntries.Slice(rp*112,112));
        foreach(var hud in HudLayoutAddon.GetSpan())
        {
            var n=hud.AddonName.ToString();
            if(n.StartsWith("JobHud",StringComparison.Ordinal) || n.StartsWith("_ActionBar",StringComparison.Ordinal)
                || n.StartsWith("_Status",StringComparison.Ordinal)) Visibility(rp,n,false);
        }
        foreach(var n in new[]{"_CastBar","_TargetInfoCastBar","_TargetInfoBuffDebuff","_FocusTargetInfo","_PartyList",
            "_AllianceList1","_AllianceList2","_EnemyList","_ParameterWidget","_Exp","_LimitBreak","_ContentGauge",
            "_ActionContents","_ToDoList","ScenarioTree","_BagWidget"}) Visibility(rp,n,false);
        Place(rp,"_ActionBar03",72,97,7,1,true);
        Place(rp,"_TargetInfoMainTarget",66,10,1,1,true);
        settings.CharacterId=player.ContentId;
        settings.LayoutsInitialized=true; settings.InitializeLayouts=false;
        config->SaveFile(true);
        settings.Enabled=true; SaveSettings(); applied="";
        log.Information("Combat HUD 2 and chat/RP HUD 3 saved.");
    }

    static uint NameHash(string name)
    {
        uint crc=0xffffffff;
        foreach(var b in System.Text.Encoding.UTF8.GetBytes(name+"_a"))
        {
            crc^=b;
            for(int bit=0;bit<8;bit++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);
        }
        return ~crc;
    }

    static AddonConfigEntry* FindEntry(int layout,string name)
    {
        if(layout is <0 or >3) throw new ArgumentOutOfRangeException(nameof(layout));
        var ds=AddonConfig.Instance()->ActiveDataSet;
        var hash=NameHash(name);
        for(int i=0;i<112;i++)
            if(ds->HudLayoutConfigEntries[layout*112+i].AddonNameHash==hash)
                return (AddonConfigEntry*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref ds->HudLayoutConfigEntries[layout*112+i]);
        return null;
    }

    static void Place(int layout,string name,float x,float y,byte anchor,float scale,bool visible)
    {
        var e=FindEntry(layout,name); if(e==null) return;
        e->X=x; e->Y=y; e->ByteValue1=anchor; e->Scale=scale;
        e->ByteValue2=(byte)(visible?1:0);
    }
    static void Visibility(int layout,string name,bool visible)
    {
        var e=FindEntry(layout,name); if(e!=null) e->ByteValue2=(byte)(visible?1:0);
    }

    void SetUpMonkHotbars()
    {
        var bars=RaptureHotbarModule.Instance();
        if(bars==null || !bars->ModuleReady || bars->ActiveHotbarClassJobId!=20 || bars->PvPHotbarsActive)
            throw new InvalidOperationException("Monk PvE must be active for the one-time hotbar setup.");
        if(Enumerable.Range(0,3).Any(i=>bars->IsHotbarShared((uint)i)))
            throw new InvalidOperationException("Combat bars 1-3 must not be shared.");
        if(!bars->IsHotbarShared(3)) throw new InvalidOperationException("Utility bar 4 must be shared.");
        string?[][] rows=[
            ["Bootshine","True Strike","Snap Punch","Arm of the Destroyer","Dragon Kick","Twin Snakes","Demolish","Perfect Balance","Four-point Fury","Rockbreaker","Steeled Meditation","Thunderclap"],
            ["Riddle of Fire","Brotherhood","Riddle of Wind","Inspirited Meditation","Second Wind","Bloodbath","Feint","True North","Mantra","Arm's Length","Riddle of Earth","Sprint"],
            ["Form Shift","Masterful Blitz","Six-sided Star","Limit Break","Duty Action I","Duty Action II","Teleport","Return","Leg Sweep",null,null,"Mount Roulette"],
            ["Sprint","Mount Roulette","Teleport","Return","Sit","Doze","Change Pose","Wave","Bow","Smile","Emotes","Character"]];
        var actions=data.GetExcelSheet<Lumina.Excel.Sheets.Action>()!;
        File.WriteAllText(Path.Combine(directory,"action-candidates.json"),JsonConvert.SerializeObject(
            actions.Where(a=>!a.IsPvP && rows.SelectMany(r=>r).Contains(a.Name.ExtractText())).Select(a=>new{
                a.RowId,Name=a.Name.ExtractText(),a.IsRoleAction,a.ClassJobLevel,
                ClassJob=a.ClassJob.RowId,ClassJobCategory=a.ClassJobCategory.RowId,ActionCategory=a.ActionCategory.RowId
            }),Formatting.Indented));
        var general=data.GetExcelSheet<Lumina.Excel.Sheets.GeneralAction>()!;
        var emotes=data.GetExcelSheet<Lumina.Excel.Sheets.Emote>()!;
        var menus=data.GetExcelSheet<Lumina.Excel.Sheets.MainCommand>()!;
        var plan=new List<(uint Bar,uint Slot,RaptureHotbarModule.HotbarSlotType Type,uint Id,string? Name)>();
        for(int bar=0;bar<4;bar++) for(int slot=0;slot<12;slot++)
        {
            var name=rows[bar][slot]; var type=RaptureHotbarModule.HotbarSlotType.Empty; uint id=0;
            if(name!=null)
            {
                if(bar==3 && slot>=4 && slot<=9)
                {type=RaptureHotbarModule.HotbarSlotType.Emote; id=emotes.FirstOrDefault(a=>a.Name.ExtractText()==name).RowId;}
                else if(bar==3 && slot>=10)
                {type=RaptureHotbarModule.HotbarSlotType.MainCommand; id=menus.FirstOrDefault(a=>a.Name.ExtractText()==name).RowId;}
                else if(new[]{"Sprint","Limit Break","Duty Action I","Duty Action II","Teleport","Return","Mount Roulette"}.Contains(name))
                {type=RaptureHotbarModule.HotbarSlotType.GeneralAction; id=general.FirstOrDefault(a=>a.Name.ExtractText()==name).RowId;}
                else
                {
                    type=RaptureHotbarModule.HotbarSlotType.Action;
                    // Retired skills and NPC skills reuse player-facing names. Require a current
                    // player action and reject ambiguity before modifying any slot.
                    var matches=actions.Where(a=>!a.IsPvP && a.ClassJobLevel>0 && a.ClassJobCategory.RowId!=0
                        && (a.ClassJob.RowId is 2 or 20 || a.IsRoleAction) && a.Name.ExtractText()==name).ToArray();
                    if(matches.Length!=1) throw new InvalidOperationException($"Expected one current player action for {name}; found {matches.Length}.");
                    id=matches[0].RowId;
                }
                if(id==0) throw new InvalidOperationException($"Could not resolve action: {name}. No hotbars were changed.");
            }
            plan.Add(((uint)bar,(uint)slot,type,id,name));
        }
        var backup=Path.Combine(directory,"hotbars-before-setup.json");
        if(!File.Exists(backup)) File.Copy(Path.Combine(directory,"display-hud-status.json"),backup);
        foreach(var p in plan) bars->SetAndSaveSlot(p.Bar,p.Slot,p.Type,p.Id,false,false);
        bars->SaveFile(true);
        File.WriteAllText(Path.Combine(directory,"installed-hotbar-plan.json"),JsonConvert.SerializeObject(plan.Select(p=>new{Bar=p.Bar+1,Slot=p.Slot+1,Type=p.Type.ToString(),p.Id,p.Name}),Formatting.Indented));
        settings.SetUpMonkHotbars=false; SaveSettings();
        log.Information("Monk combat bars and shared RP utility bar saved.");
    }

    void SetUpTweaks()
    {
        foreach(var name in new[]{"UiAdjustments@TargetCastbarCountdown","TooltipTweaks@HideTooltipsInCombat","UiAdjustments@AutoFocusMarketboardSearch","UiAdjustments@AutoFocusRecipeSearch","ImprovedSentMessageHistory","UiAdjustments@LootWindowDuplicateUniqueItemIndicator"})
            if(!commands.ProcessCommand("/tweaks enable "+name)) throw new InvalidOperationException("Simple Tweaks is not available.");
        settings.SetUpTweaks=false; SaveSettings();
    }

    void ApplyChat(string mode, DisplayInfo display)
    {
        var width=(ushort)Math.Min(mode=="rp"?920:620,display.ClientWidth*.42);
        var height=(ushort)(mode=="rp"?Math.Min(680,display.ClientHeight*.48):300);
        var x=(short)30; var y=(short)(display.ClientHeight-height-40);
        var root=gui.GetAddonByName("ChatLog");
        if (root.IsNull) return;
        var addon=(AtkUnitBase*)root.Address;
        var chatLog=(AddonChatLog*)root.Address;
        if(settings.SwitchChatTab && chatLog->TabCount>=4 && chatLog->TabNames[3].ToString()=="RP")
            chatLog->ChangeTab(mode=="rp"?3:0);
        gameConfig.Set(UiConfigOption.LogFontSize,mode=="rp"?18u:14u);
        gameConfig.Set(UiConfigOption.LogFontSizeLog4,mode=="rp"?18u:14u);
        gameConfig.Set(UiConfigOption.LogFontSizeForm,mode=="rp"?16u:14u);
        addon->SetSizeFromConfig(width,height); addon->SetPosition(x,y);
        for (var i=0;i<4;i++)
        {
            var panel=gui.GetAddonByName($"ChatLogPanel_{i}");
            if (panel.IsNull) continue;
            var p=(AtkUnitBase*)panel.Address;
            p->SetSizeFromConfig(width,height-45); p->SetPosition(x,y);
        }
    }

    void OnCommand(string _,string argument)
    {
        var mode=argument.Trim().ToLowerInvariant();
        if(mode=="status")
        {
            chat.Print($"Display HUD: {settings.Mode}, currently {applied}. HUD {settings.CombatLayout} combat / {settings.RpLayout} RP."); return;
        }
        if(mode is not ("auto" or "combat" or "rp" or "off"))
        { chat.Print("/displayhud auto | combat | rp | off | status"); return; }
        settings.Mode=mode; settings.Enabled=mode!="off"; applied=""; pending=""; SaveSettings();
        chat.Print($"Display HUD: {mode}.");
    }

    void DumpStatus(DisplayInfo display,bool cinematic)
    {
        var config=AddonConfig.Instance();
        var ds=config->ActiveDataSet;
        var entries=new List<object>();
        foreach(var hud in HudLayoutAddon.GetSpan())
        {
            var name=hud.AddonName.ToString();
            var entry=config->GetConfigEntryByAddonName(name);
            if(entry==null) continue;
            var layouts=new List<object>();
            for(int layout=0;layout<4;layout++)
                for(int i=0;i<112;i++)
                {
                    var e=ds->HudLayoutConfigEntries[layout*112+i];
                    if(e.AddonNameHash==NameHash(name))
                        layouts.Add(new{Layout=layout+1,Index=i,e.X,e.Y,e.Scale,e.Width,e.Height,e.ElementFlags,e.ByteValue1,e.ByteValue2,e.ByteValue3,e.Alpha,e.HasValue,e.IsOpen});
                }
            var a=gui.GetAddonByName(name);
            entries.Add(new{Name=name,HudRowId=hud.HudRowId,Hash=entry->AddonNameHash,Layouts=layouts,Live=a.IsNull?null:new{a.X,a.Y,a.IsVisible}});
        }
        var bars=RaptureHotbarModule.Instance();
        var slots=new List<object>();
        if(bars!=null && bars->ModuleReady)
            for(int bar=0;bar<4;bar++)
                for(int slot=0;slot<12;slot++)
                {
                    var s=bars->Hotbars[bar].Slots[slot];
                    slots.Add(new{Bar=bar+1,Slot=slot+1,Type=s.CommandType.ToString(),Id=s.CommandId});
                }
        var raw=new List<object>();
        for(int i=0;i<448;i++)
        {
            var e=ds->HudLayoutConfigEntries[i];
            raw.Add(new{Layout=i/112+1,Index=i%112,e.AddonNameHash,e.X,e.Y,e.Scale,e.Width,e.Height,e.ElementFlags,e.ByteValue1,e.ByteValue2,e.ByteValue3,e.Alpha,e.HasValue,e.IsOpen});
        }
        var c=gui.GetAddonByName("ChatLog");
        var chatLog=c.IsNull?null:(AddonChatLog*)c.Address;
        var chatInfo=chatLog==null?null:new{Tab=chatLog->TabIndex+1,TabCount=chatLog->TabCount,
            X=chatLog->X,Y=chatLog->Y,Width=chatLog->RootNode==null?0:(int)chatLog->RootNode->Width,
            Height=chatLog->RootNode==null?0:(int)chatLog->RootNode->Height,
            Names=Enumerable.Range(0,4).Select(i=>chatLog->TabNames[i].ToString()).ToArray()};
        var path=Path.Combine(directory,"display-hud-status.json");
        File.WriteAllText(path+".tmp",JsonConvert.SerializeObject(new{
            Updated=DateTimeOffset.UtcNow,settings.Enabled,settings.Mode,Applied=applied,Pending=pending,Display=display,
            CurrentHud=ds->CurrentHudLayout+1,CharacterId=player.ContentId,InCombat=condition[ConditionFlag.InCombat],
            Cinematic=cinematic,Error=error,Chat=chatInfo,Hotbars=slots,Entries=entries,RawLayouts=raw},Formatting.Indented));
        File.Move(path+".tmp",path,true);
    }

    public sealed record DisplayInfo(int MonitorWidth,int MonitorHeight,int ClientWidth,int ClientHeight,int WindowX,int WindowY);
    DisplayInfo ReadDisplay()
    {
        if(hwnd==0 || !IsWindow(hwnd))
        {
            var pid=(uint)Environment.ProcessId;
            EnumWindows((h,_)=>
            {
                GetWindowThreadProcessId(h,out var p);
                if(p!=pid || !IsWindowVisible(h) || !GetClientRect(h,out var r) || r.Right<640 || r.Bottom<480) return true;
                hwnd=h; return false;
            },0);
        }
        if(hwnd==0 || !GetClientRect(hwnd,out var clientRect) || !GetWindowRect(hwnd,out var rect)) return new(0,0,0,0,0,0);
        var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfoW(MonitorFromWindow(hwnd,2),ref info)) return new(0,0,0,0,0,0);
        return new(info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top,
            clientRect.Right,clientRect.Bottom,rect.Left,rect.Top);
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public uint Size;public Rect Monitor,Work;public uint Flags;}
    delegate bool EnumWindowCallback(nint hwnd,nint arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowCallback callback,nint arg);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);

    public void Dispose() => commands.RemoveHandler("/displayhud");
}
