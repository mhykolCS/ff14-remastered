using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.UiOverlay;
using Newtonsoft.Json;
using NativePlayer=FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace CinematicMode;

public sealed class StudioSettings
{
    // Fresh installs never alter the native HUD. The dock is enabled only by explicit choice.
    public bool DockEnabled;
    public bool CombatOnly=true;
    public bool ShowLockedJobs;
    public float X=32,Y=230,Size=52,Gap=5;
    public List<HudImage> Images=new();
    public bool RpToolsEnabled,RpBorderEnabled,RpKeepInsideFrame,IrohChatEnabled,RpSpeechBubblesEnabled,RpFpsEnabled,CombatEmotesEnabled,ChatPresenceEnabled,ChatPresenceDetails;
    public bool RpBubblePreviewRequested;
    public bool ChatPresencePreviewRequested;
    public bool ChatFadeEnabled,ManageDirectChat;
    public float RpButtonSize=44,RpOffsetX=0,RpOffsetY=0,RpBorderOpacity=.72f,RpBorderInset=12;
}
public sealed class HudImage
{
    public bool Enabled;
    public string Addon="ChatLog",Path="",Profile="both";
    public float X,Y,Width=600,Height=300,Opacity=1;
}

/// <summary>Native job buttons and opt-in images. All existing game elements remain native.</summary>
public sealed unsafe class HudStudio : IDisposable
{
    readonly IDalamudPluginInterface pi;
    readonly IFramework framework;
    readonly IAddonLifecycle addons;
    readonly IGameGui gui;
    readonly ICommandManager commands;
    readonly IChatGui chat;
    readonly IPluginLog log;
    readonly IPlayerState player;
    readonly ICondition condition;
    readonly IKeyState keys;
    readonly IDataManager data;
    readonly Func<string> mode;
    readonly Task initialization;
    readonly string path,directory;
    readonly Dictionary<string,(nint Address,ushort Id,ImGuiImageNode Node)> images=new();
    readonly HashSet<string> observed=new(StringComparer.Ordinal);
    readonly HashSet<string> hudNames=new(StringComparer.Ordinal);
    readonly Dictionary<string,object> frameBounds=new(StringComparer.Ordinal);
    readonly bool externalFps=(Environment.GetEnvironmentVariable("DXVK_HUD")??"").Split(',').Any(v=>v is "fps" or "1" or "full");
    StudioSettings settings;
    OverlayController? overlay;
    JobDockNode? dock;
    RpTools? rpTools;
    IrohChatStyle? chatStyle;
    ChatPresence? chatPresence;
    RpSpeechBubbles? speechBubbles;
    ChatFade? chatFade;
    bool open,disposed,cinematic,failed,imagesDirty,combatEmotesOpen;
    Vector2 uiMouse;
    bool uiCapturesMouse;
    string search="",error="";
    long nextStatus,nextSpacing;
    public string Mode => mode();
    public StudioSettings Settings=>settings;
    public Vector2 UiMouse=>uiMouse;
    public bool ToolkitReady=>overlay!=null;
    public string CombatEmotesIcon=>Path.Combine(pi.AssemblyLocation.DirectoryName!,"assets","combat-emotes.png");
    public bool CanShowCombatEmotes=>settings.CombatEmotesEnabled && Mode=="combat" && ShouldShow && !gui.GameUiHidden;
    public bool CombatEmotesVisible=>combatEmotesOpen && CanShowCombatEmotes;
    public Vector2 CombatEmotesAnchor=>dock?.EmotesAnchor??new Vector2(128,740);
    public void CloseCombatEmotes(){combatEmotesOpen=false;nextStatus=0;}
    public void ToggleCombatEmotes(){if(!CanShowCombatEmotes)return;combatEmotesOpen=!combatEmotesOpen;dock?.Close();nextStatus=0;}
    public void OpenSettings()=>open=true;
    public void InitializeChat(IObjectTable objects) {
        speechBubbles=new RpSpeechBubbles(this,objects,gui);
        chatPresence=new ChatPresence(this,objects,chat);
        chatStyle=new IrohChatStyle(chat,this,player,log,speechBubbles,chatPresence);
        chatFade=new ChatFade(this,gui,addons);
    }
    public HudStudio(IDalamudPluginInterface pi,IFramework framework,IAddonLifecycle addons,IGameGui gui,ICommandManager commands,IChatGui chat,IPluginLog log,IPlayerState player,ICondition condition,IKeyState keys,IDataManager data,Func<string> mode)
    {
        this.pi=pi;this.framework=framework;this.addons=addons;this.gui=gui;this.commands=commands;this.chat=chat;this.log=log;this.player=player;this.condition=condition;this.keys=keys;this.data=data;this.mode=mode;
        directory=pi.GetPluginConfigDirectory();path=Path.Combine(directory,"hud-studio.json");
        settings=File.Exists(path)?JsonConvert.DeserializeObject<StudioSettings>(File.ReadAllText(path))??new():new();
        foreach(var h in HudLayoutAddon.GetSpan()) {var name=h.AddonName.ToString();observed.Add(name);hudNames.Add(name);}
        foreach(var n in new[]{"ChatLog","ChatLogPanel_0","ChatLogPanel_1","ChatLogPanel_2","ChatLogPanel_3"}) observed.Add(n);
        initialization=Task.Run(()=>KamiToolKitLibrary.InitializeAsync(pi));
        commands.AddHandler("/hudstudio",new CommandInfo((_,args)=>{
            if(args.Trim()=="reload") {
                try{settings=JsonConvert.DeserializeObject<StudioSettings>(File.ReadAllText(path))??new();imagesDirty=true;}
                catch(Exception ex){chat.PrintError("HUD Studio settings could not be read: "+ex.Message);}
            } else open=!open;
        }){HelpMessage="Native HUD customization, job dock, and optional PNG backgrounds. No native appearance overrides by default."});
        commands.AddHandler("/jobdock",new CommandInfo((_,args)=>{
            if(args.Trim()=="off") settings.DockEnabled=false;
            else if(args.Trim()=="on") settings.DockEnabled=true;
            else {open=true;return;}
            Save();
        }){HelpMessage="Job dock: on, off, or open its settings."});
        commands.AddHandler("/rpdesk",new CommandInfo((_,args)=>{
            if(rpTools==null){chat.PrintError("The RP tools are still loading.");return;}
            if(args.Trim()=="status") {
                File.WriteAllText(Path.Combine(directory,"rp-status.json"),JsonConvert.SerializeObject(new{Mode=Mode,RpMinimal,Border=ShowRpBorder,Tools=rpTools.Status},Formatting.Indented));
                chat.Print($"RP view: {(RpMinimal?"RP minimal":Mode=="rp"?"RP":"inactive")}. Right Ctrl toggles minimal mode.");
            } else rpTools.ShowPad(args.Trim()!="draft");
        }){HelpMessage="RP writing tools: notes, draft, or status. Right Ctrl in RP mode opens RP minimal."});
        commands.AddHandler("/rppresence",new CommandInfo((_,_)=>chatPresence?.Open()){HelpMessage="Open the local current chat presence list."});
        addons.RegisterListener(AddonEvent.PreDraw,Observe);
        addons.RegisterListener(AddonEvent.PreFinalize,FinalizeAddon);
        pi.UiBuilder.Draw+=Draw;
    }
    void Save(){File.WriteAllText(path,JsonConvert.SerializeObject(settings,Formatting.Indented));}
    public void Tick(bool active)
    {
        cinematic=active;
        if(disposed || failed || !initialization.IsCompleted) return;
        if(initialization.IsFaulted){failed=true;error=initialization.Exception?.GetBaseException().Message??"Toolkit initialization failed";log.Error(error);return;}
        try {
            if(overlay==null) {overlay=new OverlayController();dock=new JobDockNode(this,data);overlay.AddNode(dock);rpTools=new RpTools(this,data,chat,player,directory,overlay);}
            if(!CanShowCombatEmotes || Escape)combatEmotesOpen=false;
            rpTools?.Tick();
            chatFade?.Tick();
            chatPresence?.Tick();
            if(settings.ChatPresencePreviewRequested){settings.ChatPresencePreviewRequested=false;Save();chatPresence?.Preview();}
            if(settings.RpBubblePreviewRequested && RpMinimal){settings.RpBubblePreviewRequested=false;Save();speechBubbles?.Preview();}
            if(Environment.TickCount64>=nextSpacing){nextSpacing=Environment.TickCount64+250;InsetHud();}
            if(imagesDirty){RestoreImages();imagesDirty=false;}
            if(Environment.TickCount64>=nextStatus) {
                nextStatus=Environment.TickCount64+(open?1000:10000);
                File.WriteAllText(Path.Combine(directory,"hud-studio-status.json"),JsonConvert.SerializeObject(new{
                    Mode=Mode,settings.DockEnabled,Visible=ShouldShow,ToolkitReady=overlay!=null,DefaultNativeAppearance=settings.Images.All(i=>!i.Enabled),
                    RpMinimal,CombatEmotesVisible,Border=ShowRpBorder,FrameMargin,FrameBounds=frameBounds,ExternalFps=externalFps,UiMouse=uiMouse,UiCapturesMouse=uiCapturesMouse,RpTools=rpTools?.Status,ChatStyle=chatStyle?.Status,
                    ChatFade=chatFade?.Status,ChatPresence=chatPresence?.Status,Category=dock?.Expanded,Images=images.Select(kv=>new{Name=kv.Key,Alpha=kv.Value.Node.Alpha,Width=kv.Value.Node.Width,Height=kv.Value.Node.Height}).ToArray(),KnownElements=observed.Order().ToArray(),Error=error,
                    CurrentJob=NativePlayer.Instance()==null?0:NativePlayer.Instance()->CurrentClassJobId},Formatting.Indented));
            }
        }catch(Exception ex){failed=true;log.Error(ex,"HUD Studio suspended");error=ex.Message;}
    }
    public bool SafeToEquip=>player.IsLoaded && !condition[ConditionFlag.InCombat] && !condition[ConditionFlag.Crafting] && !condition[ConditionFlag.Gathering] && !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51] && !condition[ConditionFlag.WatchingCutscene] && !condition[ConditionFlag.WatchingCutscene78];
    public bool ShouldShow=>settings.DockEnabled && player.IsLoaded && !cinematic && (!settings.CombatOnly || Mode=="combat") && !condition[ConditionFlag.WatchingCutscene] && !condition[ConditionFlag.WatchingCutscene78] && !condition[ConditionFlag.BetweenAreas];
    public bool RpView=>Mode=="rp" && SafeToEquip && !gui.GameUiHidden && (gui.GetAddonByName("GPose").IsNull || !gui.GetAddonByName("GPose").IsVisible);
    public bool RpMinimal=>cinematic && RpView;
    public bool ShowRpBorder=>settings.RpBorderEnabled && RpView;
    public float FrameMargin=>ShowRpBorder && settings.RpKeepInsideFrame?RpFrame.SafeMargin(settings.RpBorderInset):32;
    void InsetHud() {
        frameBounds.Clear();
        if(!ShowRpBorder || !settings.RpKeepInsideFrame)return;
        foreach(var editor in new[]{"HudLayout","ConfigAddon","ConfigHUD"}) {var a=gui.GetAddonByName(editor);if(!a.IsNull && a.IsVisible)return;}
        var device=FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();if(device==null)return;
        var margin=FrameMargin;float width=device->Width,height=device->Height;
        void Inset(string name) {
            var a=gui.GetAddonByName(name);if(a.IsNull || !a.IsVisible)return;
            var addon=(AtkUnitBase*)a.Address;if(addon->RootNode==null)return;
            var w=addon->GetScaledWidth(true);var h=addon->GetScaledHeight(true);
            if(w<=0 || h<=0 || w>width-2*margin || h>height-2*margin)return;
            var x=(short)Math.Clamp(addon->X,margin,width-w-margin);
            var y=(short)Math.Clamp(addon->Y,margin,height-h-margin);
            if(name=="ChatLog" && (x!=addon->X || y!=addon->Y)) {
                for(int i=0;i<4;i++) {var panel=gui.GetAddonByName($"ChatLogPanel_{i}");if(panel.IsNull)continue;var p=(AtkUnitBase*)panel.Address;
                    if(Math.Abs(p->X-addon->X)<4 && Math.Abs(p->Y-addon->Y)<4)p->SetPosition(x,y);}
            }
            if(x!=addon->X || y!=addon->Y)addon->SetPosition(x,y);
            frameBounds[name]=new{X=addon->X,Y=addon->Y,Width=w,Height=h,Margin=margin};
        }
        Inset("ChatLog");
        for(int i=0;i<4;i++)Inset($"ChatLogPanel_{i}");
        foreach(var name in hudNames)Inset(name);
        // The menu and gil display form a group. Clamping both independently
        // would move the counter into the bottom row of menu icons.
        var money=gui.GetAddonByName("_Money");var menu=gui.GetAddonByName("_MainCommand");
        if(!money.IsNull && !menu.IsNull && money.IsVisible && menu.IsVisible) {
            var m=(AtkUnitBase*)money.Address;var n=(AtkUnitBase*)menu.Address;
            var moneyY=(short)(height-margin-m->GetScaledHeight(true));
            m->SetPosition((short)(width-margin-m->GetScaledWidth(true)),moneyY);
            n->SetPosition((short)(width-margin-n->GetScaledWidth(true)),(short)(moneyY-n->GetScaledHeight(true)-16));
            Inset("_Money");Inset("_MainCommand");
        }
        var world=gui.GetAddonByName("_DTR");var map=gui.GetAddonByName("_NaviMap");
        if(!world.IsNull && !map.IsNull && world.IsVisible && map.IsVisible) {
            var w=(AtkUnitBase*)world.Address;var m=(AtkUnitBase*)map.Address;
            w->SetPosition((short)(width-margin-w->GetScaledWidth(true)),(short)margin);
            m->SetPosition((short)(width-margin-m->GetScaledWidth(true)),(short)(margin+w->GetScaledHeight(true)+16));
            Inset("_DTR");Inset("_NaviMap");
        }
    }
    public bool Escape=>keys[VirtualKey.ESCAPE];
    public int GearsetFor(uint job)
    {
        var g=RaptureGearsetModule.Instance();if(g==null) return -1;
        int fallback=-1;
        for(int i=0;i<100;i++) {
            var e=g->GetGearset(i);
            if(e==null || !e->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) || e->ClassJob!=job)continue;
            if(e->NameString.EndsWith("Recommended",StringComparison.Ordinal))return i;
            if(fallback==-1 && !e->NameString.Contains("Original outfit"))fallback=i;
        }
        return fallback;
    }
    public void Equip(uint job)
    {
        if(!SafeToEquip){chat.PrintError("Job changes are unavailable during the current activity.");return;}
        var id=GearsetFor(job); if(id<0){chat.PrintError("No gear set is saved for that job. Add its weapon and save a set in the native Gear Set List.");return;}
        var g=RaptureGearsetModule.Instance();
        if(g==null || !g->IsValidGearset(id))return;
        var result=g->EquipGearset(id);
        File.WriteAllText(Path.Combine(directory,"job-dock-last-switch.json"),JsonConvert.SerializeObject(new{RequestedJob=job,GearsetNumber=id+1,NativeResult=result,Time=DateTimeOffset.Now},Formatting.Indented));
        if(result!=0)chat.PrintError("FFXIV could not equip that gear set. Check its equipment in the native Gear Set List.");
        else dock?.Close();
    }
    void Observe(AddonEvent evt,AddonArgs args)
    {
        observed.Add(args.AddonName);
        if(disposed || overlay==null || cinematic)return;
        var option=settings.Images.FirstOrDefault(i=>i.Enabled && i.Addon==args.AddonName && (i.Profile=="both" || i.Profile==Mode));
        if(option==null) {
            if(images.Remove(args.AddonName,out var old))old.Node.Dispose();
            return;
        }
        var addon=(AtkUnitBase*)args.Addon.Address;
        if(addon==null || addon->RootNode==null || !addon->IsReady)return;
        if(images.TryGetValue(args.AddonName,out var state)) {
            if(state.Address!=args.Addon.Address || state.Id!=args.Addon.Id){images.Remove(args.AddonName);return;}
            state.Node.Position=new(option.X,option.Y);state.Node.Size=new(option.Width,option.Height);state.Node.Alpha=option.Opacity;return;
        }
        try {
            if(!IsValidImage(option.Path))return;
            var node=new ImGuiImageNode{Size=new(option.Width,option.Height),Position=new(option.X,option.Y),FitTexture=true,IsVisible=true,TexturePath=NormalizePath(option.Path)};
            node.AttachNode(addon,NodePosition.AsFirstChild);
            addon->UldManager.UpdateDrawNodeList();
            images[args.AddonName]=(args.Addon.Address,args.Addon.Id,node);
        } catch(Exception ex) {error=ex.Message;option.Enabled=false;Save();log.Error(ex,"Could not load HUD image");}
    }
    static string NormalizePath(string value)=>value.StartsWith('/')?"Z:"+value.Replace('/','\\'):value;
    static bool IsValidImage(string value) {var p=NormalizePath(value);return Path.IsPathRooted(p) && File.Exists(p) && new[]{".png",".jpg",".jpeg",".webp"}.Contains(Path.GetExtension(p).ToLowerInvariant());}
    void FinalizeAddon(AddonEvent evt,AddonArgs args){if(images.Remove(args.AddonName,out var s))s.Node.Dispose();}
    void RestoreImages(){foreach(var s in images.Values)s.Node.Dispose();images.Clear();}
    void Draw()
    {
        uiMouse=ImGui.GetIO().MousePos;uiCapturesMouse=ImGui.GetIO().WantCaptureMouse;
        if(!disposed && ShowRpBorder) {
            RpFrame.Draw(settings.RpBorderOpacity,settings.RpBorderInset,externalFps);
            if(settings.RpFpsEnabled && !externalFps)ImGui.GetBackgroundDrawList().AddText(new(FrameMargin,FrameMargin),ImGui.GetColorU32(IrohChatStyle.Text),$"FPS  {ImGui.GetIO().Framerate:F0}");
        }
        if(!disposed)speechBubbles?.Draw();
        if(!disposed)chatPresence?.Draw();
        if(!open || disposed)return;
        ImGui.SetNextWindowSize(new(760,620),ImGuiCond.FirstUseEver);
        if(!ImGui.Begin("FF14 Remastered###FF14RemasteredStudio",ref open)){ImGui.End();return;}
        if(ShowRpBorder && settings.RpKeepInsideFrame) {
            var pos=ImGui.GetWindowPos();var size=ImGui.GetWindowSize();var screen=ImGui.GetIO().DisplaySize;var margin=FrameMargin;
            ImGui.SetWindowPos(new(Math.Clamp(pos.X,margin,Math.Max(margin,screen.X-size.X-margin)),Math.Clamp(pos.Y,margin,Math.Max(margin,screen.Y-size.Y-margin))));
        }
        ImGui.TextWrapped("Native FFXIV appearance is the default. Existing HUD elements and chat stay native; only options you enable change their appearance.");
        if(ImGui.Button("Customize native HUD elements and chat"))commands.ProcessCommand("/hudu");
        ImGui.SameLine();ImGui.TextDisabled("HUDUnlimited");
        ImGui.TextWrapped("In HUDUnlimited, select an element and one of its nodes. Enable only the properties you want to override. ChatLog and ChatLogPanel_0 through _3 contain the chat windows.");
        ImGui.Separator();
        if(ImGui.CollapsingHeader("RP and RP minimal",ImGuiTreeNodeFlags.DefaultOpen)) {
            bool changed=false;
            ImGui.TextWrapped("RP minimal is the RP display profile with Right Ctrl active. Its four-row palette replaces hotbar 4 visually; the saved hotbar is preserved.");
            changed|=ImGui.Checkbox("Four rows of RP tools in RP minimal",ref settings.RpToolsEnabled);
            changed|=ImGui.Checkbox("Gold frame in RP and RP minimal",ref settings.RpBorderEnabled);
            changed|=ImGui.Checkbox("Keep RP elements inside the frame",ref settings.RpKeepInsideFrame);
            changed|=ImGui.Checkbox("FPS inside the frame",ref settings.RpFpsEnabled);
            if(externalFps)ImGui.TextDisabled("DXVK FPS remains above the frame until the next game launch.");
            changed|=ImGui.Checkbox("Speech bubbles in RP minimal (20 seconds)",ref settings.RpSpeechBubblesEnabled);
            ImGui.TextWrapped("Nearby Say, Yell, Shout and custom emotes. One recent message per speaker; fades during the last three seconds. Text stays in memory only.");
            if(ImGui.Button("Preview speech bubble locally")){settings.RpBubblePreviewRequested=true;Save();}
            changed|=ImGui.SliderFloat("RP button size",ref settings.RpButtonSize,32,60,"%.0f");
            changed|=ImGui.DragFloat("RP palette horizontal offset",ref settings.RpOffsetX,1,-2000,2000,"%.0f");
            changed|=ImGui.DragFloat("RP palette vertical offset",ref settings.RpOffsetY,1,-1200,1200,"%.0f");
            changed|=ImGui.SliderFloat("Frame opacity",ref settings.RpBorderOpacity,.1f,1);
            changed|=ImGui.SliderFloat("Frame inset",ref settings.RpBorderInset,6,48,"%.0f");
            if(ImGui.Button("Open scene notes"))rpTools?.ShowPad(true);
            ImGui.SameLine();if(ImGui.Button("Open RP draft"))rpTools?.ShowPad(false);
            if(changed)Save();
        }
        if(ImGui.CollapsingHeader("Chat colours",ImGuiTreeNodeFlags.DefaultOpen)) {
            if(ImGui.Checkbox("Fade chat history after 20 seconds",ref settings.ChatFadeEnabled))Save();
            ImGui.TextWrapped("Input and tabs remain visible. Typing or hovering reveals history. Empty the input or leave the chat to start a two-second grace period, then a smooth fade. Native history and links are preserved.");
            if(ImGui.Checkbox("Direct Chat only in RP minimal",ref settings.ManageDirectChat))Save();
            if(ImGui.Checkbox("Iroh chat colours",ref settings.IrohChatEnabled))Save();
            if(ImGui.Checkbox("Iroh presence markers beside chat names",ref settings.ChatPresenceEnabled))Save();
            if(ImGui.Checkbox("Include RP, Away and Busy status",ref settings.ChatPresenceDetails))Save();
            ImGui.TextWrapped("Blue dot: online friend. Green star: online. Red star: offline. Grey star: unknown. Message badges capture status at arrival; click one for the current presence list.");
            if(ImGui.Button("Open current chat presence"))chatPresence?.Open();
            ImGui.SameLine();if(ImGui.Button("Preview presence locally"))chatPresence?.Preview();
            ImGui.TextWrapped("Pale message text, stable colours for each speaker, purple actions and gold mentions. Applies to new chat lines; native links and chat controls stay intact.");
            if(ImGui.Button("Show local chat colour sample"))chatStyle?.Preview();
        }
        if(ImGui.CollapsingHeader("Expandable job menu",ImGuiTreeNodeFlags.DefaultOpen)) {
            bool changed=false;
            changed|=ImGui.Checkbox("Show job menu",ref settings.DockEnabled);
            changed|=ImGui.Checkbox("Emotes button below combat job menu",ref settings.CombatEmotesEnabled);
            changed|=ImGui.Checkbox("Only on the combat display profile",ref settings.CombatOnly);
            changed|=ImGui.Checkbox("Show locked jobs",ref settings.ShowLockedJobs);
            changed|=ImGui.SliderFloat("Horizontal position",ref settings.X,0,3000,"%.0f");
            changed|=ImGui.SliderFloat("Vertical position",ref settings.Y,0,1300,"%.0f");
            changed|=ImGui.SliderFloat("Button size",ref settings.Size,32,96,"%.0f");
            changed|=ImGui.SliderFloat("Spacing",ref settings.Gap,0,20,"%.0f");
            ImGui.TextWrapped("Click a category to expand its row; click again to close it. Choose a job to equip its saved set. Dimmed entries need an unlocked job and a usable saved set. Gear swaps are disabled during combat, crafting, gathering, and cutscenes.");
            if(changed){settings.Size=Math.Clamp(settings.Size,32,96);Save();}
        }
        if(ImGui.CollapsingHeader("Custom images and backgrounds")) {
            ImGui.TextWrapped("Optional local PNGs are attached behind the selected native element. Keep transparency in the image to leave the native UI visible. No images are enabled by default. Linux paths such as /home/you/Pictures/panel.png are accepted.");
            if(ImGui.Button("Add image option")){settings.Images.Add(new());Save();}
            ImGui.InputTextWithHint("Element filter","ChatLog, _ActionBar, _PartyList...",ref search,128);
            for(int i=0;i<settings.Images.Count;i++) {
                var item=settings.Images[i];ImGui.PushID(i);bool changed=false;
                ImGui.Separator();
                changed|=ImGui.Checkbox("Enabled",ref item.Enabled);
                if(ImGui.BeginCombo("Attach to",item.Addon)) {
                    foreach(var name in observed.Where(n=>n.Contains(search,StringComparison.OrdinalIgnoreCase)).Order().ToArray())
                        if(ImGui.Selectable(name,name==item.Addon)){item.Addon=name;changed=true;}
                    ImGui.EndCombo();
                }
                changed|=ImGui.InputText("Image path",ref item.Path,1024);
                if(ImGui.BeginCombo("Display profile",item.Profile)) {
                    foreach(var m in new[]{"both","combat","rp"})if(ImGui.Selectable(m,m==item.Profile)){item.Profile=m;changed=true;}
                    ImGui.EndCombo();
                }
                changed|=ImGui.DragFloat("Offset X",ref item.X,1,-4000,4000,"%.0f");
                changed|=ImGui.DragFloat("Offset Y",ref item.Y,1,-4000,4000,"%.0f");
                changed|=ImGui.DragFloat("Width",ref item.Width,1,1,4000,"%.0f");
                changed|=ImGui.DragFloat("Height",ref item.Height,1,1,2400,"%.0f");
                changed|=ImGui.SliderFloat("Opacity",ref item.Opacity,0,1);
                if(item.Enabled && !IsValidImage(item.Path))ImGui.TextColored(new Vector4(1,.6f,.2f,1),"Choose an existing local image file.");
                if(ImGui.Button("Remove image")){settings.Images.RemoveAt(i--);changed=true;}
                if(changed){imagesDirty=true;Save();}
                ImGui.PopID();
            }
        }
        if(ImGui.Button("Remove all optional images")){settings.Images.Clear();imagesDirty=true;Save();}
        if(!string.IsNullOrEmpty(error))ImGui.TextWrapped(error);
        ImGui.End();
    }
    public void Dispose()
    {
        disposed=true;chatFade?.Dispose();chatStyle?.Dispose();chatPresence?.Dispose();speechBubbles?.Dispose();pi.UiBuilder.Draw-=Draw;commands.RemoveHandler("/hudstudio");commands.RemoveHandler("/jobdock");commands.RemoveHandler("/rpdesk");commands.RemoveHandler("/rppresence");
        addons.UnregisterListener(Observe);addons.UnregisterListener(FinalizeAddon);
        framework.RunOnFrameworkThread(()=>{rpTools?.Dispose();RestoreImages();overlay?.Dispose();if(initialization.IsCompletedSuccessfully)KamiToolKitLibrary.Dispose();}).GetAwaiter().GetResult();
    }
}

public sealed unsafe class JobDockNode : OverlayNode
{
    readonly HudStudio studio;
    readonly List<IconButtonNode> categories=new();
    readonly List<TextNode> labels=new();
    readonly List<(uint Job,int Category,IconButtonNode Button,TextNode Label)> jobs=new();
    readonly ImGuiIconButtonNode emotes;
    readonly TextNode emotesLabel;
    static readonly (string Name,string Short,uint Icon,uint[] Jobs)[] Groups={
        ("Tanks","TANK",62119,new uint[]{19,21,32,37}),
        ("Healers","HEAL",62124,new uint[]{24,28,33,40}),
        ("Melee DPS","MELEE",62120,new uint[]{20,22,30,34,39,41}),
        ("Ranged DPS","RANGED",62123,new uint[]{23,31,38}),
        ("Magic DPS and limited jobs","MAGIC",62125,new uint[]{25,27,35,42,36,43}),
        ("Crafters","CRAFT",62108,new uint[]{8,9,10,11,12,13,14,15}),
        ("Gatherers","GATHER",62116,new uint[]{16,17,18})
    };
    public int Expanded {get;private set;}=-1;
    public Vector2 EmotesAnchor=>Position+emotes.Position+new Vector2(emotes.Width+12,0);
    public override OverlayLayer OverlayLayer=>OverlayLayer.BehindUserInterface;
    public JobDockNode(HudStudio studio,IDataManager data)
    {
        this.studio=studio;Size=new(650,480);
        for(int c=0;c<Groups.Length;c++) {
            int index=c;var group=Groups[c];
            var category=new IconButtonNode{Size=new(52),IconId=group.Icon,TextTooltip=group.Name,OnClick=()=>{studio.CloseCombatEmotes();Expanded=Expanded==index?-1:index;},IsVisible=true};
            category.AttachNode(this);categories.Add(category);
            var label=MakeLabel(group.Short);label.AttachNode(this);labels.Add(label);
            foreach(var job in group.Jobs) {
                uint id=job; var row=data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!.GetRow(job);
                var button=new IconButtonNode{Size=new(52),IconId=62100+job,TextTooltip=row.Name.ExtractText(),OnClick=()=>studio.Equip(id),IsVisible=false};
                button.AttachNode(this);
                var jl=MakeLabel(row.Abbreviation.ExtractText());jl.IsVisible=false;jl.AttachNode(this);
                jobs.Add((job,c,button,jl));
            }
        }
        emotes=new ImGuiIconButtonNode{TexturePath=studio.CombatEmotesIcon,Size=new(52),TextTooltip="Open or close all four rows of RP emotes and tools",OnClick=studio.ToggleCombatEmotes,IsVisible=false};
        emotes.AttachNode(this);
        emotesLabel=MakeLabel("EMOTES");emotesLabel.AttachNode(this);
    }
    static TextNode MakeLabel(string value)=>new(){String=value,FontSize=11,AlignmentType=AlignmentType.Center,Size=new(60,16),IsVisible=true};
    public void Close()=>Expanded=-1;
    protected override void OnUpdate()
    {
        IsVisible=studio.ShouldShow;
        if(!IsVisible || studio.Escape)Expanded=-1;
        var s=studio.Settings;var step=s.Size+s.Gap+16;
        // Clamp to the current client size so moving displays cannot strand the dock off-screen.
        var root=AtkStage.Instance();
        var viewport=FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        var width=viewport==null?3440:viewport->Width;var height=viewport==null?1440:viewport->Height;
        Size=new(Math.Max(650,s.Size*9+80),step*8+12);
        Position=new(Math.Clamp(s.X,0,Math.Max(0,width-s.Size*9-80)),Math.Clamp(s.Y,0,Math.Max(0,height-Size.Y-20)));
        emotes.Position=new(0,step*7+12);emotes.Size=new(s.Size);emotes.IsVisible=studio.CanShowCombatEmotes;emotes.IsChecked=studio.CombatEmotesVisible;
        emotesLabel.Position=emotes.Position+new Vector2(-4,s.Size);emotesLabel.Size=new(s.Size+8,16);emotesLabel.IsVisible=emotes.IsVisible;
        for(int c=0;c<categories.Count;c++) {
            categories[c].Position=new(0,c*step);categories[c].Size=new(s.Size);categories[c].IsChecked=c==Expanded;
            labels[c].Position=new(-4,c*step+s.Size);labels[c].Size=new(s.Size+8,16);
        }
        int visible=0; var p=NativePlayer.Instance();
        foreach(var (job,category,button,label) in jobs) {
            bool unlocked=p!=null && p->GetClassJobLevel((int)job,false)>0;
            bool show=category==Expanded && (s.ShowLockedJobs || unlocked);
            button.IsVisible=show;label.IsVisible=show;
            if(!show)continue;
            button.Position=new((++visible)*(s.Size+s.Gap),category*step);button.Size=new(s.Size);
            label.Position=button.Position+new Vector2(-4,s.Size);label.Size=new(s.Size+8,16);
            var set=studio.GearsetFor(job);
            button.IsEnabled=unlocked && set>=0 && studio.SafeToEquip;
            label.Alpha=button.IsEnabled?1:.5f;
        }
    }
}
