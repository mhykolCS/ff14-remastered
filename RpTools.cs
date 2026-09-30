using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.UiOverlay;
using Newtonsoft.Json;

namespace CinematicMode;

public sealed record RpButton(string Label,string Description,uint Icon,uint Emote,string Command,string Tool);

public sealed unsafe class RpTools : IDisposable
{
    readonly HudStudio studio;
    readonly IDataManager data;
    readonly IChatGui chat;
    readonly IPlayerState player;
    readonly string directory;
    readonly RpPaletteNode palette;
    RpScenePad? pad;
    RpWritingState writing=new();
    ulong character;
    long saveAt;
    bool wasVisible;
    long activationCount;
    string lastAction="";
    public List<List<RpButton>> Rows {get;}=new();
    public string LastResult {get;private set;}="Quiet emotes · Hold Mouse4 to look · Writing tools keep drafts local";
    public RpWritingState Writing=>writing;
    public float FrameMargin=>studio.FrameMargin;
    public bool Visible=>(studio.RpMinimal && studio.Settings.RpToolsEnabled) || studio.CombatEmotesVisible;
    public RpTools(HudStudio studio,IDataManager data,IChatGui chat,IPlayerState player,string directory,OverlayController overlay)
    {
        this.studio=studio;this.data=data;this.chat=chat;this.player=player;this.directory=directory;
        var emotes=data.GetExcelSheet<Lumina.Excel.Sheets.Emote>()!.Where(e=>e.RowId>0 && e.Icon>0 && e.TextCommand.RowId>0)
            .Select(e=>new {Id=e.RowId,Name=e.Name.ExtractText(),Icon=(uint)e.Icon,Command=e.TextCommand.Value.Command.ExtractText(),
                Unlocked=UIState.Instance()!=null && UIState.Instance()->IsEmoteUnlocked((ushort)e.RowId)}).ToArray();
        File.WriteAllText(Path.Combine(directory,"rp-emotes.json"),JsonConvert.SerializeObject(emotes,Formatting.Indented));
        var used=new HashSet<uint>();
        var selections=new[]{
            new[]{"/wave","/bow","/greet","/goodbye","/welcome","/yes","/no","/thanks","/sorry","/clap","/salute","/thumbsup"},
            new[]{"/smile","/laugh","/chuckle","/grin","/surprised","/disturbed","/shrug","/think","/doubt","/sigh","/frown","/angry"},
            new[]{"/lounge","/groundsit","/changepose","/doze","/stretch","/yawn","/lookout","/read","/lean","/dance","/cheer","/pray"}
        };
        var fallbacks=new[]{"/happy","/point","/comfort","/psych","/rally","/disappointed","/blowkiss","/handover","/me","/examineself","/joy","/panic","/pet","/blush","/huh"};
        foreach(var choices in selections) {
            var row=new List<RpButton>();
            foreach(var command in choices) {
                var e=emotes.FirstOrDefault(e=>e.Unlocked && e.Command==command && !used.Contains(e.Id));
                if(e==null)continue;
                used.Add(e.Id);row.Add(new(e.Name,$"{e.Name} — animation only",e.Icon,e.Id,e.Command,""));
            }
            foreach(var command in fallbacks.Concat(emotes.Where(e=>e.Unlocked).Select(e=>e.Command))) {
                if(row.Count==12)break;
                var e=emotes.FirstOrDefault(e=>e.Unlocked && e.Command==command && !used.Contains(e.Id));
                if(e==null || string.IsNullOrWhiteSpace(e.Command))continue;
                used.Add(e.Id);row.Add(new(e.Name,$"{e.Name} — animation only",e.Icon,e.Id,e.Command,""));
            }
            Rows.Add(row);
        }
        uint Icon(string name,uint fallback) {
            var row=data.GetExcelSheet<Lumina.Excel.Sheets.MainCommand>()!
                .FirstOrDefault(m=>m.Name.ExtractText().Contains(name,StringComparison.OrdinalIgnoreCase));
            if(row.RowId==0)return fallback;
            RaptureHotbarModule.HotbarSlot slot=default;
            var icon=slot.GetIconIdForSlot(RaptureHotbarModule.HotbarSlotType.MainCommand,row.RowId);
            return icon>0?(uint)icon:fallback;
        }
        Rows.Add(new(){
            new("Notes","Persistent private scene notes",Icon("Journal",61502),0,"","notes"),
            new("Say","Draft a Say post; copy when ready",Icon("Social",61509),0,"","say"),
            new("Emote","Draft a custom /em post",Icon("Emote",61503),0,"","emote"),
            new("Party","Draft a Party post",Icon("Party",61509),0,"","party"),
            new("OOC","Draft with (( out-of-character )) brackets",Icon("Social",61509),0,"","ooc"),
            new("D6","Roll a private six-sided die",65002,0,"","d6"),
            new("D20","Roll a private twenty-sided die",65002,0,"","d20"),
            new("D100","Roll a private percentile die",65002,0,"","d100"),
            new("Spark","A private prompt to spark your next scene",Icon("Journal",61502),0,"","spark"),
            new("Emotes","Open the game's complete emote collection",Icon("Emote",61503),0,"/emotelist","command"),
            new("Walk","Toggle walking for your scene",Icon("Character",61501),0,"/walk","command"),
            new("Gpose","Open Group Pose",Icon("Group Pose",61503),0,"/gpose","command")
        });
        palette=new RpPaletteNode(this,studio);overlay.AddNode(palette);
        File.WriteAllText(Path.Combine(directory,"rp-palette.json"),JsonConvert.SerializeObject(Rows,Formatting.Indented));
    }
    public void Tick() {
        var id=player.IsLoaded?player.ContentId:0;
        if(id!=character) {
            Flush();pad?.Close();character=id;writing=new();
            var path=WritingPath;
            if(id!=0 && File.Exists(path))writing=JsonConvert.DeserializeObject<RpWritingState>(File.ReadAllText(path))??new();
        }
        if(saveAt!=0 && Environment.TickCount64>=saveAt)Flush();
        if(wasVisible && !Visible)pad?.Close();
        wasVisible=Visible;
    }
    string WritingPath=>Path.Combine(directory,$"rp-writing-{character}.json");
    public void Changed(){saveAt=Environment.TickCount64+500;}
    public void Flush(){if(character!=0 && saveAt!=0)File.WriteAllText(WritingPath,JsonConvert.SerializeObject(writing,Formatting.Indented));saveAt=0;}
    public bool Available(RpButton b)=>b.Emote==0 || (UIState.Instance()!=null && UIState.Instance()->IsEmoteUnlocked((ushort)b.Emote));
    public void Activate(RpButton button) {
        if(!Visible || !studio.SafeToEquip)return;
        if(!Available(button)){chat.PrintError("This emote is not unlocked for this character.");return;}
        activationCount++;lastAction=button.Label;
        if(button.Emote!=0) {
            // Motion-only is the native command's quiet form. Nothing is posted to chat.
            var noArgument=button.Command is "/sit" or "/lounge" or "/groundsit" or "/changepose";
            Execute(button.Command+(noArgument?"":" motion"));return;
        }
        switch(button.Tool) {
            case "notes": ShowPad(true);break;
            case "say": writing.Channel="/s";ShowPad(false);break;
            case "emote": writing.Channel="/em";ShowPad(false);break;
            case "party": writing.Channel="/p";ShowPad(false);break;
            case "ooc": writing.Ooc=true;ShowPad(false);break;
            case "d6": Roll(6);break;
            case "d20": Roll(20);break;
            case "d100": Roll(100);break;
            case "spark": {
                string[] prompts={"What small detail does your character notice that everyone else misses?","An old acquaintance recognizes something you are carrying.","The weather changes, and with it the mood of the conversation.","A familiar sound brings back a memory your character rarely shares.","Someone asks a simple question with an unexpectedly difficult answer.","Your character has a small favor to ask, but hesitates."};
                LastResult=prompts[RandomNumberGenerator.GetInt32(prompts.Length)];chat.Print("[RP spark — private] "+LastResult);break;
            }
            case "command": Execute(button.Command);break;
        }
    }
    void Roll(int sides){var value=RandomNumberGenerator.GetInt32(1,sides+1);LastResult=$"Private D{sides}: {value}";chat.Print("[RP dice — private] "+$"D{sides} = {value}");}
    static void Execute(string command) {
        if(!command.StartsWith('/') || command.Contains('\n') || command.Contains('\r'))return;
        var module=UIModule.Instance();if(module==null)return;
        var text=new Utf8String(command);
        try{module->ProcessChatBoxEntry(&text,0,false);}finally{text.Dtor();}
    }
    public void ShowPad(bool notes) {
        if(pad==null)pad=new RpScenePad(this){InternalName="FF14RemasteredScenePad",Title="RP Writing",Subtitle="Notes and drafts",Size=new(720,480)};
        pad.NotesMode=notes;pad.Open();pad.LoadText();Changed();
    }
    public object Status=>new{Visible,Context=studio.RpMinimal?"RP minimal":studio.CombatEmotesVisible?"Combat menu":"Closed",Rows=Rows.Select(r=>r.Count).ToArray(),Position=palette.Position,Size=palette.Size,ActivationCount=activationCount,LastAction=lastAction,LastResult,Character=character,WritingOpen=pad?.IsOpen??false};
    public void Dispose(){Flush();pad?.Dispose();}
}

public sealed class RpPaletteNode : OverlayNode
{
    readonly RpTools tools;
    readonly HudStudio studio;
    readonly WindowBackgroundTextureNode background;
    readonly List<TextNode> rowLabels=new();
    readonly List<(int Row,int Column,RpButton Data,IconButtonNode Button,TextNode Label)> buttons=new();
    public override OverlayLayer OverlayLayer=>OverlayLayer.BehindUserInterface;
    // The palette has its own visibility policy in minimal and combat modes.
    public override bool HideWithNativeUi=>false;
    public RpPaletteNode(RpTools tools,HudStudio studio) {
        this.tools=tools;this.studio=studio;
        background=new WindowBackgroundTextureNode(false){IsVisible=true,Alpha=.8f,Offsets=new Vector4(64,32,32,32),PartsRenderType=19};background.AttachNode(this);
        string[] names={"SOCIAL","EXPRESSIONS","SCENE","TOOLS"};
        for(int r=0;r<4;r++) {
            var label=Text(names[r],11);label.AttachNode(this);rowLabels.Add(label);
            for(int c=0;c<tools.Rows[r].Count;c++) {
                var item=tools.Rows[r][c];var button=new IconButtonNode{IconId=item.Icon,TextTooltip=item.Description,OnClick=()=>tools.Activate(item),IsVisible=true};
                button.AttachNode(this);var name=Text(ShortLabel(item.Label),10);name.AlignmentType=AlignmentType.Center;name.AttachNode(this);
                buttons.Add((r,c,item,button,name));
            }
        }
    }
    static TextNode Text(string s,int size)=>new(){String=s,FontSize=(byte)size,IsVisible=true};
    static string ShortLabel(string name)=>name switch {
        "Sit on Ground"=>"Ground","Change Pose"=>"Pose","Disappointed"=>"Upset","Blow Kiss"=>"Kiss","Hand Over"=>"Give",
        "Surprised"=>"Surprise","Thumbs Up"=>"Thumbs","Thank You"=>"Thanks",
        _=>name.Length>8?name[..7]+"…":name
    };
    protected override unsafe void OnUpdate() {
        IsVisible=tools.Visible;if(!IsVisible)return;
        var s=studio.Settings;var size=Math.Clamp(s.RpButtonSize,32,60);var step=size+8;var rowHeight=size+20;
        Size=new(100+12*step,4*rowHeight+28);
        var device=FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        float width=device==null?2388:device->Width,height=device==null?1532:device->Height;
        var margin=studio.FrameMargin;
        var origin=studio.CombatEmotesVisible?studio.CombatEmotesAnchor:new Vector2(width-Size.X-margin+s.RpOffsetX,height-Size.Y-margin+s.RpOffsetY);
        Position=new(Math.Clamp(origin.X,margin,Math.Max(margin,width-Size.X-margin)),Math.Clamp(origin.Y,margin,Math.Max(margin,height-Size.Y-margin)));
        background.Size=Size;
        for(int r=0;r<4;r++){rowLabels[r].Position=new(12,15+r*rowHeight+size/2-6);rowLabels[r].Size=new(84,20);}
        foreach(var (row,column,data,button,label) in buttons) {
            button.Position=new(94+column*step,14+row*rowHeight);button.Size=new(size);
            button.IsEnabled=studio.SafeToEquip && tools.Available(data);
            label.Position=button.Position+new Vector2(-4,size+1);label.Size=new(size+8,16);
        }
    }
}

public sealed unsafe class RpScenePad : NativeAddon
{
    readonly RpTools tools;
    TextInputNode? input;
    TextNode? info;
    TextButtonNode? oocButton;
    int copied;
    bool loading;
    public bool NotesMode;
    public RpScenePad(RpTools tools){this.tools=tools;}
    protected override void OnSetup(AtkUnitBase* addon,Span<AtkValue> values) {
        string[] labels={"Notes","Say","Emote","Party"};
        for(int i=0;i<labels.Length;i++) {
            int index=i;var button=new TextButtonNode{String=labels[i],Size=new(112,28),Position=new(22+i*118,48),IsVisible=true,OnClick=()=>{
                SaveText();NotesMode=index==0;if(index>0)tools.Writing.Channel=new[]{"/s","/em","/p"}[index-1];LoadText();
            }};button.AttachNode(RootNode);
        }
        oocButton=new TextButtonNode{String="OOC: off",Size=new(160,28),Position=new(500,48),IsVisible=true,OnClick=()=>{tools.Writing.Ooc=!tools.Writing.Ooc;tools.Changed();copied=0;RefreshInfo();}};oocButton.AttachNode(RootNode);
        input=new TextInputNode{Position=new(22,90),Size=new(674,276),MaxCharacters=8192,IsVisible=true,ShowLimitText=true,
            Flags=TextInputFlags.AllowUpperCase|TextInputFlags.AllowLowerCase|TextInputFlags.AllowNumberInput|TextInputFlags.AllowSymbolInput|TextInputFlags.EnableIme|TextInputFlags.WordWrap|TextInputFlags.MultiLine,
            PlaceholderString="Write here. Your notes and draft are saved locally.",OnInputReceived=_=>{if(!loading){SaveText();copied=0;RefreshInfo();}}};
        input.AttachNode(RootNode);
        info=new TextNode{Position=new(24,377),Size=new(670,28),FontSize=13,IsVisible=true};info.AttachNode(RootNode);
        var copy=new TextButtonNode{String="Copy next chunk",Position=new(24,416),Size=new(180,30),IsVisible=true,OnClick=Copy};copy.AttachNode(RootNode);
        var help=new TextNode{String="Paste into chat and press Enter when ready.",Position=new(224,420),Size=new(450,24),FontSize=13,IsVisible=true};help.AttachNode(RootNode);
        LoadText();
    }
    public void LoadText() {
        if(input==null)return;
        loading=true;input.String=NotesMode?tools.Writing.Notes:tools.Writing.Draft;loading=false;copied=0;
        Title=NotesMode?"Scene Notes":"RP Draft";RefreshInfo();
    }
    void SaveText(){if(input==null)return;var value=input.String.ExtractText();if(NotesMode)tools.Writing.Notes=value;else tools.Writing.Draft=value;tools.Changed();}
    void RefreshInfo(){if(info==null)return;var chunks=RpWriting.Chunks(NotesMode?tools.Writing.Notes:tools.Writing.Draft,tools.Writing.Channel,tools.Writing.Ooc);info.String=NotesMode?"Private notes · saved locally":$"{tools.Writing.Channel} · {chunks.Count} chat chunk(s) · {copied} copied";if(oocButton!=null)oocButton.String=tools.Writing.Ooc?"OOC: on":"OOC: off";}
    void Copy() {
        SaveText();
        if(NotesMode){ImGui.SetClipboardText(tools.Writing.Notes);if(info!=null)info.String="Notes copied. Nothing was sent to chat.";return;}
        var chunks=RpWriting.Chunks(tools.Writing.Draft,tools.Writing.Channel,tools.Writing.Ooc);if(chunks.Count==0)return;
        if(copied>=chunks.Count)copied=0;ImGui.SetClipboardText(chunks[copied++]);RefreshInfo();
    }
    protected override void OnUpdate(AtkUnitBase* addon) {
        var device=FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if(device==null)return;
        var margin=tools.FrameMargin;
        var x=(short)Math.Clamp(addon->X,margin,Math.Max(margin,device->Width-addon->GetScaledWidth(true)-margin));
        var y=(short)Math.Clamp(addon->Y,margin,Math.Max(margin,device->Height-addon->GetScaledHeight(true)-margin));
        if(x!=addon->X || y!=addon->Y)addon->SetPosition(x,y);
    }
    protected override void OnFinalize(AtkUnitBase* addon){SaveText();tools.Flush();input=null;info=null;oocButton=null;}
}
