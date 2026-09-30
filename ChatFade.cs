using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace CinematicMode;

/// <summary>Fades native transcript text without editing the game's chat log or input.</summary>
public sealed unsafe class ChatFade : IDisposable
{
    readonly HudStudio studio;
    readonly IGameGui gui;
    readonly IAddonLifecycle lifecycle;
    readonly Dictionary<string,Panel> panels=new();
    readonly ChatFadePolicy policy=new();
    bool typing,hovering,failed;
    string error="";
    sealed class Row(byte[] text,long arrived,TextNode node,float height)
    {
        public readonly byte[] Text=text;
        public readonly long Arrived=arrived;
        public readonly TextNode Node=node;
        public readonly float Height=height;
    }
    sealed class Panel(nint address,ushort id)
    {
        public readonly nint Address=address;
        public readonly ushort Id=id;
        public byte[] Source=[];
        public readonly List<Row> Rows=new();
        public readonly Dictionary<nint,(byte Alpha,byte Effective)> Alphas=new();
        public uint TotalLines;
        public ushort Width,Height;
        public byte Font,Spacing;
        public float Opacity;
        public int VisibleMessages;
        public byte Alignment;
        public ushort NativeHeight;
        public Dictionary<nint,bool> Visibility=new();
    }
    public ChatFade(HudStudio studio,IGameGui gui,IAddonLifecycle lifecycle)
    {
        this.studio=studio;this.gui=gui;this.lifecycle=lifecycle;
        lifecycle.RegisterListener(AddonEvent.PreUpdate,BeforeUpdate);
        lifecycle.RegisterListener(AddonEvent.PreDraw,OnDraw);
        lifecycle.RegisterListener(AddonEvent.PreFinalize,BeforeFinalize);
    }
    public void Tick()
    {
        if(!studio.Settings.ChatFadeEnabled || failed) {Clear();return;}
        typing=false;hovering=false;
        var a=gui.GetAddonByName("ChatLog");
        if(!a.IsNull && a.IsVisible) {
            var c=(AddonChatLog*)a.Address;
            typing=c->TextInput!=null && c->TextInput->IsActive && c->TextInput->EvaluatedString.Length>0;
            hovering=Contains((AtkUnitBase*)c,studio.UiMouse);
        }
        for(int i=0;i<4;i++) {
            var p=gui.GetAddonByName($"ChatLogPanel_{i}");
            if(!p.IsNull && p.IsVisible) {
                var c=(AddonChatLogPanel*)p.Address;
                hovering|=Contains((AtkUnitBase*)c,studio.UiMouse) || c->LogViewer.IsSelectingText || c->LogViewer.IsContextMenuShown;
            }
        }
        policy.Update(Environment.TickCount64,typing,hovering);
    }
    static bool Contains(AtkUnitBase* addon,Vector2 pos)=>pos.X>=addon->X && pos.Y>=addon->Y &&
        pos.X<addon->X+addon->GetScaledWidth(true) && pos.Y<addon->Y+addon->GetScaledHeight(true);
    bool Alive(string name,Panel panel)
    {
        var a=gui.GetAddonByName(name);
        return !a.IsNull && a.Address==panel.Address && a.Id==panel.Id;
    }
    static void Restore(Panel panel)
    {
        foreach(var (ptr,alpha) in panel.Alphas){((AtkResNode*)ptr)->SetAlpha(alpha.Alpha);((AtkResNode*)ptr)->Alpha_2=alpha.Effective;}
        panel.Alphas.Clear();
        foreach(var (ptr,visible) in panel.Visibility)((AtkResNode*)ptr)->ToggleVisibility(visible);
        panel.Visibility.Clear();
    }
    void BeforeUpdate(AddonEvent e,AddonArgs args)
    {
        if(panels.TryGetValue(args.AddonName,out var panel) && panel.Address==args.Addon.Address && panel.Id==args.Addon.Id)Restore(panel);
    }
    void BeforeFinalize(AddonEvent e,AddonArgs args)
    {
        if(panels.Remove(args.AddonName,out var p) && p.Address==args.Addon.Address && p.Id==args.Addon.Id)Destroy(p);
    }
    static void Destroy(Panel p)
    {
        Restore(p);
        foreach(var row in p.Rows)row.Node.Dispose();
        p.Rows.Clear();p.Source=[];
    }
    void Clear()
    {
        foreach(var (name,p) in panels)if(Alive(name,p))Destroy(p);
        panels.Clear();
    }
    static void Alpha(Panel p,AtkResNode* node,float alpha)
    {
        if(node==null)return;
        if(!p.Alphas.TryGetValue((nint)node,out var original))p.Alphas[(nint)node]=original=(node->Color.A,node->Alpha_2);
        node->SetAlpha((byte)Math.Clamp(MathF.Round(original.Alpha*alpha),0,255));
        node->Alpha_2=(byte)Math.Clamp(MathF.Round(original.Effective*alpha),0,255);
    }
    void OnDraw(AddonEvent e,AddonArgs args)
    {
        if(failed || !studio.Settings.ChatFadeEnabled || !studio.ToolkitReady || !args.AddonName.StartsWith("ChatLogPanel_",StringComparison.Ordinal))return;
        var native=(AddonChatLogPanel*)args.Addon.Address;
        var text=native->ChatText;
        if(text==null || native->ChatComponent==null || text->ParentNode==null)return;
        try {
            if(!panels.TryGetValue(args.AddonName,out var panel))panels[args.AddonName]=panel=new(args.Addon.Address,args.Addon.Id);
            if(panel.Address!=args.Addon.Address || panel.Id!=args.Addon.Id)return;
            var now=Environment.TickCount64;
            var source=text->NodeText.AsSpan();
            // The view is bounded; never clone an unbounded log or malformed text buffer.
            if(source.Length>65536 || text->Width==0 || text->LineSpacing==0)return;
            if(!source.SequenceEqual(panel.Source) || panel.Width!=text->Width || panel.Height!=text->Height || panel.Font!=text->FontSize || panel.Spacing!=text->LineSpacing)
                Rebuild(panel,native,source,now);
            panel.Opacity=policy.HistoryAlpha;panel.VisibleMessages=0;
            bool nativeHistory=policy.HistoryAlpha>=.999f;
            float y=Math.Max(0,text->Height-panel.Rows.Sum(r=>r.Height));
            foreach(var row in panel.Rows) {
                var alpha=policy.Alpha(now,row.Arrived);
                row.Node.Position=new(text->X,text->Y+y);
                row.Node.Node->ScaleX=text->ScaleX;row.Node.Node->ScaleY=text->ScaleY;
                row.Node.IsVisible=!nativeHistory && y<text->Height;
                row.Node.Alpha=alpha;
                if(alpha>0)panel.VisibleMessages++;
                panel.Opacity=Math.Max(panel.Opacity,alpha);
                y+=row.Height;
            }
            // During interaction the original node handles links, selection and scrolling.
            // Between interactions, native-font copies let each message fade independently.
            if(!panel.Visibility.ContainsKey((nint)text))panel.Visibility[(nint)text]=text->IsVisible();
            text->ToggleVisibility(nativeHistory);
            var uld=&native->ChatComponent->UldManager;
            for(int i=0;i<uld->NodeListCount;i++) {
                var n=uld->NodeList[i];
                if(n==null || n==(AtkResNode*)text)continue;
                // Only the original background, scroll bar and resize handle. Input/tabs
                // belong to ChatLog and are never changed, nor is the panel hit target.
                if(n->NodeId is 2 or 4 or 5)Alpha(panel,n,panel.Opacity);
            }
        }catch(Exception ex) {
            error=ex.GetType().Name+": "+ex.Message;failed=true;Clear();
        }
    }
    void Rebuild(Panel panel,AddonChatLogPanel* native,ReadOnlySpan<byte> source,long now)
    {
        var original=native->ChatText;
        ushort width=0,heightNative=0;original->GetTextDrawSize(&width,&heightNative);panel.NativeHeight=heightNative;panel.Alignment=original->AlignmentFontType;
        var pieces=SplitMessages(source.ToArray());
        if(pieces.Count>128)throw new InvalidOperationException("Chat view exceeds the supported message count; native display restored.");
        var previous=panel.Rows.ToArray();
        var mapping=ChatFadeSequence.Match(previous.Select(x=>x.Text).ToArray(),pieces.Select(x=>x.Raw).ToArray());
        bool initial=panel.Source.Length==0;
        bool incoming=native->LogViewer.TotalLineCount>panel.TotalLines && native->LogViewer.IsScrolledBottom;
        var lastMatched=Array.FindLastIndex(mapping,index=>index>=0);
        var rebuilt=new List<Row>();
        float y=0;
        try {
            for(int i=0;i<pieces.Count;i++) {
                var arrived=mapping[i]>=0?previous[mapping[i]].Arrived:
                    initial || (incoming && i>lastMatched)?now:now-ChatFadePolicy.MessageHoldMs-ChatFadePolicy.FadeInMs-ChatFadePolicy.FadeOutMs;
                var node=new TextNode();
                // Copy native typography before setting text, preserving all SeString colours.
                node.Node->FontSize=original->FontSize;node.Node->LineSpacing=original->LineSpacing;
                node.Node->CharSpacing=original->CharSpacing;node.Node->AlignmentFontType=original->AlignmentFontType;
                node.Node->TextColor=original->TextColor;node.Node->EdgeColor=original->EdgeColor;
                node.Node->BackgroundColor=original->BackgroundColor;node.Node->SheetType=original->SheetType;
                node.Node->TextFlags=(original->TextFlags|TextFlags.WordWrap)&~(TextFlags.LinkData|TextFlags.AutoAdjustNodeSize);
                node.Size=new(original->Width,original->Height);
                node.String=new Lumina.Text.ReadOnly.ReadOnlySeString(pieces[i].Styled);
                var height=Math.Max(original->LineSpacing,node.GetTextDrawSize(false).Y);
                node.Size=new(original->Width,Math.Min(height,Math.Max(0,original->Height-y)));
                node.Position=new(original->X,original->Y+y);node.IsVisible=false;
                node.AttachNode(original,NodePosition.AfterTarget);
                rebuilt.Add(new(pieces[i].Raw,arrived,node,height));y+=height;
            }
        }catch {
            foreach(var row in rebuilt)row.Node.Dispose();throw;
        }
        foreach(var row in previous)row.Node.Dispose();
        panel.Rows.Clear();panel.Rows.AddRange(rebuilt);panel.Source=source.ToArray();
        panel.TotalLines=native->LogViewer.TotalLineCount;
        panel.Width=original->Width;panel.Height=original->Height;panel.Font=original->FontSize;panel.Spacing=original->LineSpacing;
    }
    public sealed record MessagePart(byte[] Raw,byte[] Styled);
    public static List<MessagePart> SplitMessages(byte[] source)
    {
        var result=new List<MessagePart>();var current=new List<Payload>();var formatting=new List<Payload>();
        var prefix=new List<Payload>();
        void Finish() {
            // CR separates messages; NewLine payloads are native soft wraps within one message.
            if(current.Count>0)result.Add(new(new SeString(current).Encode(),new SeString(prefix.Concat(current).ToList()).Encode()));
            current.Clear();prefix=new(formatting);
        }
        foreach(var payload in SeString.Parse(source).Payloads) {
            if(payload is TextPayload text && text.Text?.Contains('\r')==true) {
                var chunks=text.Text.Split('\r');
                for(int i=0;i<chunks.Length;i++) {
                    if(chunks[i].Length>0)current.Add(new TextPayload(chunks[i]));
                    if(i<chunks.Length-1)Finish();
                }
                continue;
            }
            current.Add(payload);
            var raw=payload.Encode();
            if(raw.Length>2 && raw[0]==2 && raw[1] is 0x13 or 0x14 or 0x15 or 0x48 or 0x49)formatting.Add(payload);
        }
        if(current.Any(x=>x is TextPayload t && !string.IsNullOrEmpty(t.Text)))Finish();
        return result;
    }
    public object Status=>new{Enabled=studio.Settings.ChatFadeEnabled,Typing=typing,Hovering=hovering,policy.HistoryAlpha,Failed=failed,Error=error,
        Panels=panels.ToDictionary(x=>x.Key,x=>new{x.Value.Opacity,x.Value.VisibleMessages,Messages=x.Value.Rows.Count,ContentHeight=x.Value.Rows.Sum(r=>r.Height),x.Value.Width,x.Value.Height,x.Value.Alignment,x.Value.NativeHeight})};
    public void Dispose()
    {
        lifecycle.UnregisterListener(BeforeUpdate);lifecycle.UnregisterListener(OnDraw);lifecycle.UnregisterListener(BeforeFinalize);Clear();
    }
}
