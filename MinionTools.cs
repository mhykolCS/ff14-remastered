using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.UiOverlay;
using Newtonsoft.Json;

namespace CinematicMode;

public sealed unsafe class MinionTools
{
    readonly HudStudio studio;
    readonly IPlayerState player;
    readonly IChatGui chat;
    readonly string directory;
    readonly Dictionary<uint,MinionEntry> catalog;
    readonly MinionMenuNode menu;
    readonly Dictionary<uint,bool> usable=new();
    HashSet<uint> owned=new();
    List<uint> favourites=new();
    ulong character;
    long nextCollection,nextAvailability,activationCount;
    string previousMode="",lastAction="";
    bool open,lastAccepted;
    public uint GuideIcon {get;}
    public bool Visible=>open && studio.CanShowMinions;
    public IReadOnlyList<uint> Favourites=>favourites;
    public MinionEntry? At(int index)=>index>=0 && index<favourites.Count && catalog.TryGetValue(favourites[index],out var entry)?entry:null;
    string ProfilePath=>Path.Combine(directory,$"minion-favourites-{character}.json");

    public MinionTools(HudStudio studio,IDataManager data,IPlayerState player,IChatGui chat,string directory,OverlayController overlay)
    {
        this.studio=studio;this.player=player;this.chat=chat;this.directory=directory;
        catalog=data.GetExcelSheet<Lumina.Excel.Sheets.Companion>()!
            .Where(c=>c.RowId>0 && c.Icon>0 && !string.IsNullOrWhiteSpace(c.Singular.ExtractText()))
            .Select(c=>new MinionEntry(c.RowId,CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c.Singular.ExtractText()),c.Icon)).ToDictionary(c=>c.Id);
        GuideIcon=data.GetExcelSheet<Lumina.Excel.Sheets.MainCommand>()!
            .Where(c=>c.Name.ExtractText().Equals("Minion Guide",StringComparison.OrdinalIgnoreCase))
            .Select(c=>(uint)c.Icon).FirstOrDefault(4507u);
        menu=new MinionMenuNode(this,studio);overlay.AddNode(menu);
    }
    public void Tick()
    {
        if(!studio.Settings.MinionsEnabled){Close();return;}
        if(previousMode!=studio.Mode || !studio.CanShowMinions || studio.Escape)Close();
        previousMode=studio.Mode;
        if(!player.IsLoaded)return;
        var now=Environment.TickCount64;
        if(character!=player.ContentId || now>=nextCollection) {
            nextCollection=now+5000;
            var state=UIState.Instance();if(state==null)return;
            var unlocked=catalog.Keys.Where(id=>state->IsCompanionUnlocked(id)).ToHashSet();
            if(character!=player.ContentId || !owned.SetEquals(unlocked)) {
                var changedCharacter=character!=player.ContentId;
                character=player.ContentId;owned=unlocked;
                List<uint>? saved=changedCharacter?null:favourites;var readable=true;
                if(changedCharacter && File.Exists(ProfilePath)) {
                    try{saved=JsonConvert.DeserializeObject<List<uint>>(File.ReadAllText(ProfilePath));}
                    catch(JsonException){readable=false;chat.PrintError("Minion favourites could not be read; using unlocked recommendations for this session. The existing file was preserved.");}
                }
                favourites=MinionSelection.Choose(owned,saved);if(readable)Save();
                File.WriteAllText(Path.Combine(directory,"minion-catalog.json"),JsonConvert.SerializeObject(owned.Order().Select(id=>catalog[id]),Formatting.Indented));
            }
        }
        if(now<nextAvailability)return;
        nextAvailability=now+250;
        foreach(var id in favourites)usable[id]=CanActivate(id);
    }
    void Save(){if(character!=0)File.WriteAllText(ProfilePath,JsonConvert.SerializeObject(favourites,Formatting.Indented));}
    public void Close()=>open=false;
    public void Toggle(){if(!studio.CanShowMinions)return;open=!open;if(open)studio.CloseOtherMenus();}
    public bool Available(uint id)=>usable.GetValueOrDefault(id);
    bool CanActivate(uint id)
    {
        if(!studio.SafeToEquip || !owned.Contains(id))return false;
        var state=UIState.Instance();var manager=ActionManager.Instance();
        return state!=null && state->IsCompanionUnlocked(id) && manager!=null
            && manager->CompanionActionCooldown<=0 && manager->GetActionStatus(ActionType.Companion,id)==0;
    }
    public void Activate(uint id)
    {
        if(!Visible || !CanActivate(id))return;
        activationCount++;lastAction=catalog[id].Name;
        lastAccepted=ActionManager.Instance()->UseAction(ActionType.Companion,id);
        nextAvailability=0;
        if(!lastAccepted)chat.PrintError("That minion cannot be summoned here right now.");
    }
    public void DrawSettings()
    {
        if(!studio.Settings.MinionsEnabled)return;
        ImGui.TextWrapped($"{owned.Count} unlocked minions; {favourites.Count} of 36 favourites. Choosing an existing favourite swaps its two slots. Click a summoned minion again to dismiss it.");
        for(int row=0;row<MinionSelection.Rows;row++) {
            if(!ImGui.TreeNode($"Row {row+1}: {MinionSelection.RowNames[row]}"))continue;
            for(int col=0;col<MinionSelection.Columns;col++) {
                var slot=row*MinionSelection.Columns+col;var entry=At(slot);if(entry==null)continue;
                if(ImGui.BeginCombo($"Slot {col+1}##minion{slot}",entry.Name)) {
                    foreach(var choice in owned.Select(id=>catalog[id]).OrderBy(c=>c.Name)) {
                        if(ImGui.Selectable(choice.Name,choice.Id==entry.Id)){MinionSelection.Replace(favourites,slot,choice.Id);Save();}
                    }
                    ImGui.EndCombo();
                }
            }
            ImGui.TreePop();
        }
        if(ImGui.Button("Use recommended unlocked minions")){favourites=MinionSelection.Choose(owned);Save();}
    }
    public object Status=>new{Visible,Unlocked=owned.Count,Slots=favourites.Count,Rows=MinionSelection.Rows,Columns=MinionSelection.Columns,
        Current=studio.CurrentMinion,Launcher=menu.Position,Panel=menu.PanelPosition,PanelSize=menu.PanelSize,ActivationCount=activationCount,LastAction=lastAction,LastAccepted=lastAccepted};
}

public sealed class MinionMenuNode : OverlayNode
{
    readonly MinionTools tools;
    readonly HudStudio studio;
    readonly IconButtonNode launcher;
    readonly TextNode launcherLabel;
    readonly ResNode panel;
    readonly WindowBackgroundTextureNode background;
    readonly List<TextNode> rowLabels=new();
    readonly List<(IconButtonNode Button,TextNode Label)> buttons=new();
    readonly uint[] displayedIds=new uint[MinionSelection.Capacity];
    readonly bool[] displayedSummoned=new bool[MinionSelection.Capacity];
    public Vector2 PanelPosition=>Position+panel.Position;
    public Vector2 PanelSize=>panel.Size;
    public override OverlayLayer OverlayLayer=>OverlayLayer.BehindUserInterface;
    public override bool HideWithNativeUi=>false;
    public MinionMenuNode(MinionTools tools,HudStudio studio)
    {
        this.tools=tools;this.studio=studio;
        launcher=new IconButtonNode{IconId=tools.GuideIcon,TextTooltip="Open or close three rows of minion favourites",OnClick=tools.Toggle,IsVisible=true};launcher.AttachNode(this);
        launcherLabel=Text("MINIONS",11);launcherLabel.AlignmentType=AlignmentType.Center;launcherLabel.AttachNode(this);
        panel=new ResNode{IsVisible=false};panel.AttachNode(this);
        background=new WindowBackgroundTextureNode(false){IsVisible=true,Alpha=.8f,Offsets=new Vector4(64,32,32,32),PartsRenderType=19};background.AttachNode(panel);
        for(int row=0;row<MinionSelection.Rows;row++) {
            var label=Text(MinionSelection.RowNames[row],11);label.AttachNode(panel);rowLabels.Add(label);
            for(int col=0;col<MinionSelection.Columns;col++) {
                var index=row*MinionSelection.Columns+col;
                var button=new IconButtonNode{OnClick=()=>{var item=tools.At(index);if(item!=null)tools.Activate(item.Id);},IsVisible=false};button.AttachNode(panel);
                var name=Text("",10);name.AlignmentType=AlignmentType.Center;name.AttachNode(panel);buttons.Add((button,name));
            }
        }
    }
    static TextNode Text(string s,int size)=>new(){String=s,FontSize=(byte)size,IsVisible=true};
    static string ShortName(string name) {
        if(name.StartsWith("Wind-up ",StringComparison.OrdinalIgnoreCase))name=name[8..];
        return name.Length>8?name[..7]+"…":name;
    }
    protected override unsafe void OnUpdate()
    {
        IsVisible=studio.CanShowMinions;if(!IsVisible)return;
        var s=studio.Settings;var launcherSize=Math.Clamp(s.Size,32,96);
        var device=FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        float width=device==null?2388:device->Width,height=device==null?1532:device->Height,margin=studio.FrameMargin;
        var origin=studio.Mode=="combat"?studio.MinionsAnchor:new Vector2(margin,Math.Max(210,s.Y));
        Position=new(Math.Clamp(origin.X,margin,Math.Max(margin,width-launcherSize-margin)),Math.Clamp(origin.Y,margin,Math.Max(margin,height-launcherSize-20-margin)));
        launcher.Size=new(launcherSize);launcher.IsChecked=tools.Visible;
        launcherLabel.Position=new(-4,launcherSize);launcherLabel.Size=new(launcherSize+8,16);
        var size=Math.Clamp(s.RpButtonSize,32,60);var step=size+8;var rowHeight=size+20;
        panel.IsVisible=tools.Visible;panel.Size=new(100+12*step,3*rowHeight+28);background.Size=panel.Size;
        var panelX=Math.Clamp(Position.X+launcherSize+12,margin,Math.Max(margin,width-panel.Width-margin));
        var panelY=Math.Clamp(Position.Y,margin,Math.Max(margin,height-panel.Height-margin));
        panel.Position=studio.ClearChat(new(panelX,panelY),panel.Size)-Position;
        Size=new(Math.Max(launcherSize,panel.Position.X+panel.Width),Math.Max(launcherSize+20,panel.Position.Y+panel.Height));
        if(!tools.Visible)return;
        for(int row=0;row<3;row++){rowLabels[row].Position=new(12,15+row*rowHeight+size/2-6);rowLabels[row].Size=new(80,20);}
        var current=studio.CurrentMinion;
        for(int i=0;i<buttons.Count;i++) {
            var (button,label)=buttons[i];var item=tools.At(i);button.IsVisible=label.IsVisible=item!=null;if(item==null)continue;
            var summoned=current==item.Id;
            if(displayedIds[i]!=item.Id || displayedSummoned[i]!=summoned) {
                button.IconId=item.Icon;label.String=ShortName(item.Name);
                button.TextTooltip=item.Name+(summoned?" — summoned; click to dismiss":" — click to summon");
                label.TextColor=summoned?new Vector4(1,.83f,.35f,1):new Vector4(.9f,.88f,.82f,1);
                displayedIds[i]=item.Id;displayedSummoned[i]=summoned;
            }
            button.Position=new(94+i%12*step,14+i/12*rowHeight);button.Size=new(size);
            button.IsEnabled=tools.Available(item.Id);button.IsChecked=summoned;
            label.Position=button.Position+new Vector2(-4,size+1);label.Size=new(size+8,16);
        }
    }
}
