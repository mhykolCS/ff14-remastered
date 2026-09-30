using Dalamud.Configuration;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]

namespace CinematicMode;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Mouse4CameraLook { get; set; }
    // Saved before any change so a crash or reload cannot lose the original camera settings.
    public Dictionary<ulong, Dictionary<string, uint>> Recovery { get; set; } = new();
}

public sealed unsafe class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly IFramework framework;
    private readonly IKeyState keys;
    private readonly IClientState client;
    private readonly IPlayerState player;
    private readonly IGameConfig gameConfig;
    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addons;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly DisplayHudController displayHud;
    private readonly IDataManager data;
    private readonly JobSetup jobSetup;
    private readonly HudStudio studio;
    private readonly MouseLookController mouseLook = new();
    private bool catalogExported;
    private readonly Dictionary<string, object> observed = new();
    private sealed record HiddenState(string Name, ushort Id, bool Visible);
    private readonly Dictionary<nint, HiddenState> hidden = new();
    private bool active;
    private bool pressed;
    private bool chord;
    private ulong activeCharacter;
    private ulong initializedCharacter;
    private readonly HashSet<VirtualKey> initiallyHeld = new();

    private static readonly UiControlOption[] ControlOptions =
    [UiControlOption.KeyboardCameraInterpolationType, UiControlOption.KeyboardCameraVerticalInterpolation];
    private static readonly UiConfigOption[] UiOptions =
    [UiConfigOption.FPSCameraInterpolationType, UiConfigOption.FPSCameraVerticalInterpolation,
        UiConfigOption.LegacyCameraCorrectionFix];

    private static readonly HashSet<string> HiddenAddons = new(StringComparer.Ordinal)
    {
        "ScenarioTree", "_ScenarioTree", "_NaviMap", "_DTR", "_ToDoList", "_MainCommand", "_MainCommandData",
        "_BagWidget", "_Money", "_Exp", "_ParameterWidget", "_CastBar", "_LimitBreak",
        "_PartyList", "_AllianceList1", "_AllianceList2", "_EnemyList", "_EnemyInfo",
        "_TargetInfo", "_TargetInfoMainTarget", "_TargetInfoCastBar", "_TargetInfoBuffDebuff",
        "_FocusTargetInfo", "_Status", "_StatusCustom0", "_StatusCustom1", "_StatusCustom2",
        "_StatusCustom3", "_StatusCustom4", "_ActionBar", "_ActionCross", "_ActionDoubleCrossL",
        "_ActionDoubleCrossR", "_ActionContents", "_ActionDetail", "_ItemDetail", "_Notification",
        "_NotificationIcMvp", "_NotificationIc01", "_NotificationIc02", "_NotificationIc03",
        "_NotificationIc04", "_NotificationIc05", "_NotificationIc06", "_NotificationIc07",
        "RecommendList", "RecommendList2", "AchievementInfo", "AchievementNear", "AchievementNotification",
        "FellowshipNotification", "ContentsInfoDetail", "_ContentInfo", "_AreaMap",
        "_ContentGauge", "NamePlate", "CastBarEnemy", "ScreenLog",
    };

    public Plugin(IDalamudPluginInterface pi, IFramework framework, IKeyState keys,
        IClientState client, IPlayerState player, IGameConfig gameConfig, IGameGui gameGui, IAddonLifecycle addons,
        ICommandManager commands, IChatGui chat, IPluginLog log, ICondition condition, IDataManager data,IObjectTable objects)
    {
        this.pi = pi; this.framework = framework; this.keys = keys; this.client = client;
        this.player = player; this.gameConfig = gameConfig; this.gameGui = gameGui; this.addons = addons;
        this.commands = commands; this.chat = chat; this.log = log;
        this.data=data;
        config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        jobSetup = new JobSetup(pi,commands,chat,log,condition,data,player);
        displayHud = new DisplayHudController(pi, client, player, gameGui, commands, chat, log, condition, data, gameConfig);
        studio=new HudStudio(pi,framework,addons,gameGui,commands,chat,log,player,condition,keys,data,()=>displayHud.CurrentMode);
        studio.InitializeChat(objects);
        commands.AddHandler("/ff14remastered",new CommandInfo((_,_)=>studio.OpenSettings()){HelpMessage="Open FF14 Remastered customization settings."});
        pi.UiBuilder.OpenConfigUi+=studio.OpenSettings;
        commands.AddHandler("/hudclarity", new CommandInfo((_, args) => {
            var original = new Dictionary<string,uint>();
            foreach(var key in new[]{"GraphicsRezoUpscaleType","GraphicsRezoScale","DynamicRezoType","AntiAliasing_DX11","DepthOfField_DX11"})
                if(gameConfig.System.TryGetUInt(key,out var value)) original[key]=value;
            var backup=Path.Combine(pi.GetPluginConfigDirectory(),"graphics-before-clarity.json");
            if(!File.Exists(backup)) File.WriteAllText(backup,JsonConvert.SerializeObject(original,Formatting.Indented));
            gameConfig.Set(SystemConfigOption.GraphicsRezoUpscaleType,0u);
            gameConfig.Set(SystemConfigOption.GraphicsRezoScale,100u);
            gameConfig.Set(SystemConfigOption.DynamicRezoType,0u);
            var after=new Dictionary<string,uint>();
            foreach(var key in original.Keys) if(gameConfig.System.TryGetUInt(key,out var value)) after[key]=value;
            File.WriteAllText(Path.Combine(pi.GetPluginConfigDirectory(),"graphics-after-clarity.json"),JsonConvert.SerializeObject(after,Formatting.Indented));
            chat.Print("HUD clarity: native 100% rendering enabled; dynamic resolution disabled.");
        }) { HelpMessage="Use native 100% rendering to correct resolution blur." });
        commands.AddHandler("/cinematic", new CommandInfo(OnCommand)
        { HelpMessage = "Right Ctrl toggles RP minimal in RP mode, or cinematic mode otherwise. Options: on, off, status." });
        commands.AddHandler("/rpminimal",new CommandInfo((cmd,args)=>{
            if(displayHud.CurrentMode!="rp"){chat.Print("RP minimal is available in the RP display profile.");return;}
            OnCommand(cmd,args);
        }){HelpMessage="Toggle RP minimal while the RP display profile is active. Options: on, off, status."});
        commands.AddHandler("/mousecamera", new CommandInfo((_, args) => {
            switch(args.Trim().ToLowerInvariant()) {
                case "on": config.Mouse4CameraLook=true; break;
                case "off": config.Mouse4CameraLook=false; mouseLook.Tick(false,false); break;
                case "status": break;
                default: chat.Print("Use /mousecamera on, off, or status. Hold Mouse4 and move the mouse to look around."); return;
            }
            pi.SavePluginConfig(config);
            WriteStatus();
            chat.Print($"Mouse4 camera look: {(config.Mouse4CameraLook ? "ON" : "OFF")} for all jobs.");
        }) { HelpMessage="Hold Mouse4 to look around on any job. Options: on, off, status." });
        addons.RegisterListener(AddonEvent.PreDraw, OnPreDraw);
        addons.RegisterListener(AddonEvent.PreUpdate, OnPreUpdate);
        addons.RegisterListener(AddonEvent.PostUpdate, OnPostUpdate);
        addons.RegisterListener(AddonEvent.PreFinalize, OnPreFinalize);
        framework.Update += OnUpdate;
        client.Logout += OnLogout;
        log.Information("FF14 Remastered loaded; tap Right Ctrl to toggle.");
    }

    private void OnPreUpdate(AddonEvent type, AddonArgs args)
    {
        if (hidden.Remove(args.Addon.Address, out var state) && args.Addon.Id == state.Id)
            ((AtkUnitBase*)args.Addon.Address)->IsVisible = state.Visible;
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (!active || !ShouldHide(args.AddonName)) return;
        var addon = args.Addon;
        hidden[addon.Address] = new HiddenState(args.AddonName, addon.Id, addon.IsVisible);
        // Also remove mouse hit targets: suppressing drawing alone leaves invisible buttons clickable.
        ((AtkUnitBase*)addon.Address)->IsVisible = false;
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args) => hidden.Remove(args.Addon.Address);

    private void RestoreHud()
    {
        foreach (var (address, state) in hidden)
        {
            var addon = gameGui.GetAddonByName(state.Name);
            if (!addon.IsNull && addon.Address == address && addon.Id == state.Id)
                ((AtkUnitBase*)addon.Address)->IsVisible = state.Visible;
        }
        hidden.Clear();
    }

    private bool ShouldHide(string name) => (name!="NamePlate" || displayHud.CurrentMode!="rp") && (HiddenAddons.Contains(name)
        || name.StartsWith("_ActionBar", StringComparison.Ordinal)
        || name.StartsWith("_JobHud", StringComparison.Ordinal)
        || name.StartsWith("JobHud", StringComparison.Ordinal)
        || name.StartsWith("_Notification", StringComparison.Ordinal));

    private void OnPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = args.Addon;
        // Keep a small inspection inventory; no text, chat messages, or other player information.
        if (!observed.ContainsKey(args.AddonName))
            observed[args.AddonName] = new { addon.X, addon.Y,
                HiddenByCinematic = ShouldHide(args.AddonName) };
        if (active && ShouldHide(args.AddonName))
            args.PreventOriginal(); // No HUD layout or visibility setting is changed.
    }

    private void OnUpdate(IFramework _)
    {
        try
        {
            mouseLook.Tick(config.Mouse4CameraLook,
                client.IsLoggedIn && player.IsLoaded && GameFramework.Instance()!=null && !GameFramework.Instance()->WindowInactive);
            if (!client.IsLoggedIn || !player.IsLoaded)
            {
                pressed = false;
                initializedCharacter = 0;
                return;
            }
            var leavingRpMinimal=active && displayHud.CurrentMode=="rp";
            displayHud.Tick(active);
            if(leavingRpMinimal && displayHud.CurrentMode!="rp")Disable();
            SyncDirectChat();
            jobSetup.Tick();
            studio.Tick(active);
            if(!catalogExported) { catalogExported=true; if(!File.Exists(Path.Combine(pi.GetPluginConfigDirectory(),"job-catalog.json"))) JobCatalog.Export(data,pi.GetPluginConfigDirectory()); }
            var contentId = player.ContentId;
            if (initializedCharacter != contentId)
            {
                if (config.Recovery.TryGetValue(contentId, out var saved))
                    Restore(contentId, saved);
                initializedCharacter = contentId;
                WriteStatus();
            }
            var focused = GameFramework.Instance() != null && !GameFramework.Instance()->WindowInactive;
            if (!focused) { pressed = false; return; }
            // Direct Chat can filter the game's normal key state while editing.
            // Read the physical modifier so Right Ctrl still exits RP minimal.
            var down = MouseLookController.PhysicalKeyDown((int)VirtualKey.RCONTROL);
            if (down && !pressed)
            {
                pressed = true;
                chord = false;
                initiallyHeld.Clear();
                foreach (var key in keys.GetValidVirtualKeys())
                    if (MouseLookController.PhysicalKeyDown((int)key)) initiallyHeld.Add(key);
            }
            if (down)
            {
                // Right Ctrl still works in shortcuts; only a standalone tap toggles.
                foreach (var key in keys.GetValidVirtualKeys())
                    if (key != VirtualKey.CONTROL && MouseLookController.PhysicalKeyDown((int)key) && !initiallyHeld.Contains(key))
                        chord = true;
            }
            if (!down && pressed)
            {
                pressed = false;
                if (!chord) Toggle();
            }
        }
        catch (Exception ex)
        {
            mouseLook.Dispose();
            log.Error(ex, "Cinematic update failed; restoring original settings.");
            active = false;
            SyncDirectChat();
            RestoreHud();
            if (config.Recovery.TryGetValue(activeCharacter, out var saved))
            {
                try { Restore(activeCharacter, saved); }
                catch (Exception restoreError) { log.Error(restoreError, "Camera recovery remains saved for next load."); }
            }
        }
    }

    private Dictionary<string, uint> ReadCamera()
    {
        var result = new Dictionary<string, uint>();
        foreach (var option in ControlOptions)
        {
            if (!gameConfig.TryGet(option, out uint value))
                throw new InvalidOperationException($"Unable to read {option}; settings left untouched.");
            result["Control:" + option] = value;
        }
        foreach (var option in UiOptions)
        {
            if (!gameConfig.TryGet(option, out uint value))
                throw new InvalidOperationException($"Unable to read {option}; settings left untouched.");
            result["Ui:" + option] = value;
        }
        return result;
    }

    private void Toggle()
    {
        if (active) { Disable(); return; }
        if (!client.IsLoggedIn || !player.IsLoaded) return;
        activeCharacter = player.ContentId;
        if (config.Recovery.TryGetValue(activeCharacter, out var pending)) Restore(activeCharacter, pending);
        var saved = ReadCamera();
        config.Recovery[activeCharacter] = saved;
        pi.SavePluginConfig(config);
        try
        {
            // In-game dropdown: 0 = only when moving, 1 = always, 2 = never.
            gameConfig.Set(UiControlOption.KeyboardCameraInterpolationType, 2u);
            gameConfig.Set(UiControlOption.KeyboardCameraVerticalInterpolation, 0u);
            gameConfig.Set(UiConfigOption.FPSCameraInterpolationType, 2u);
            gameConfig.Set(UiConfigOption.FPSCameraVerticalInterpolation, 0u);
            gameConfig.Set(UiConfigOption.LegacyCameraCorrectionFix, 1u);
            active = true;
            SyncDirectChat();
            WriteStatus();
            log.Information("FF14 Remastered cinematic mode enabled.");
        }
        catch
        {
            active = false;
            SyncDirectChat();
            Restore(activeCharacter, saved);
            throw;
        }
    }

    private void Restore(ulong contentId, Dictionary<string, uint> saved)
    {
        foreach (var (key, value) in saved)
        {
            if (key.StartsWith("Control:", StringComparison.Ordinal))
                gameConfig.Set(Enum.Parse<UiControlOption>(key[8..]), value);
            else if (key.StartsWith("Ui:", StringComparison.Ordinal))
                gameConfig.Set(Enum.Parse<UiConfigOption>(key[3..]), value);
        }
        config.Recovery.Remove(contentId);
        pi.SavePluginConfig(config);
    }

    private void Disable()
    {
        active = false;
        SyncDirectChat();
        RestoreHud();
        if (config.Recovery.TryGetValue(activeCharacter, out var saved)) Restore(activeCharacter, saved);
        WriteStatus();
        log.Information("FF14 Remastered cinematic mode off; original camera settings restored.");
    }

    private void OnLogout(int type, int code)
    {
        displayHud.ResetSession();
        if (active) Disable();
        else SyncDirectChat();
        initializedCharacter = 0;
        pressed = false;
    }

    private void OnCommand(string command, string args)
    {
        try
        {
            switch (args.Trim().ToLowerInvariant())
            {
                case "off": Disable(); break;
                case "on": if (!active) Toggle(); break;
                case "status": WriteStatus(); chat.Print($"View: {ModeLabel}. Tap Right Ctrl to toggle minimal mode."); break;
                default: Toggle(); break;
            }
        }
        catch (Exception ex) { log.Error(ex, "Cinematic command failed."); chat.PrintError("FF14 Remastered could not change settings; check /xllog."); }
    }

    private string ModeLabel => displayHud.CurrentMode=="rp" ? (active?"RP minimal":"RP") : (active?"Cinematic":"Combat");
    private string lastReportedMode="";

    private void SyncDirectChat()
    {
        if(!studio.Settings.ManageDirectChat)return;
        uint desired=active && displayHud.CurrentMode=="rp" ? 1u : 0u;
        if(gameConfig.TryGet(UiControlOption.DirectChat,out uint current) && current!=desired)
            gameConfig.Set(UiControlOption.DirectChat,desired);
        if(lastReportedMode!=ModeLabel){lastReportedMode=ModeLabel;WriteStatus();}
    }

    private void WriteStatus()
    {
        var directory = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(directory);
        var camera = CameraManager.Instance()->GetActiveCamera();
        gameConfig.TryGet(UiControlOption.DirectChat, out uint directChat);
        File.WriteAllText(Path.Combine(directory, "status.json"), JsonConvert.SerializeObject(new
        {
            Active = active, Mode=ModeLabel, Updated = DateTimeOffset.Now, Camera = ReadCamera(),
            SavedCamera = config.Recovery.GetValueOrDefault(activeCharacter),
            Controls = new { DirectChat = directChat },
            MouseLook = new { Enabled=config.Mouse4CameraLook, mouseLook.Holding, mouseLook.LookStarts, mouseLook.LastPress },
            View = camera == null ? null : new { Distance = camera->Distance, Yaw = camera->DirH, Pitch = camera->DirV },
            Addons = observed,
        }, Formatting.Indented));
    }

    public void Dispose()
    {
        mouseLook.Dispose();
        pi.UiBuilder.OpenConfigUi-=studio.OpenSettings;
        commands.RemoveHandler("/ff14remastered");
        studio.Dispose();
        jobSetup.Dispose();
        displayHud.Dispose();
        framework.Update -= OnUpdate;
        client.Logout -= OnLogout;
        addons.UnregisterListener(AddonEvent.PreDraw, OnPreDraw);
        addons.UnregisterListener(AddonEvent.PreUpdate, OnPreUpdate);
        addons.UnregisterListener(AddonEvent.PostUpdate, OnPostUpdate);
        addons.UnregisterListener(AddonEvent.PreFinalize, OnPreFinalize);
        commands.RemoveHandler("/cinematic");
        commands.RemoveHandler("/rpminimal");
        commands.RemoveHandler("/hudclarity");
        commands.RemoveHandler("/mousecamera");
        if (active) Disable();
        else SyncDirectChat();
    }
}
