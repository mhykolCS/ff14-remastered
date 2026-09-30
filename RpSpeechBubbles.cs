using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace CinematicMode;

/// <summary>Short-lived, client-only captions projected from character positions.</summary>
public sealed unsafe class RpSpeechBubbles : IDisposable
{
    sealed record Speech(string Name,uint World,string Text,long At,bool Action);
    readonly HudStudio studio;
    readonly IObjectTable objects;
    readonly IGameGui gui;
    readonly Dictionary<(string,uint),Speech> recent=new();
    readonly Dictionary<(string,uint),(IPlayerCharacter Player,ulong Id)> speakers=new();
    long nextRoster;
    int drawn;
    long received;
    long previewAt;
    object? previewGeometry;
    public RpSpeechBubbles(HudStudio studio,IObjectTable objects,IGameGui gui){
        this.studio=studio;this.objects=objects;this.gui=gui;
    }
    public static float Opacity(long age)=>age<0 || age>=20000?0:Math.Clamp((20000-age)/3000f,0,1);
    public void Record(IChatMessage message) {
        if(!studio.Settings.RpSpeechBubblesEnabled || message.LogKind is not (XivChatType.Say or XivChatType.Yell or XivChatType.Shout or XivChatType.CustomEmote))return;
        var sender=message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if(sender==null && message.LogKind==XivChatType.CustomEmote)sender=message.Message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if(sender==null)return;
        var text=message.Message.TextValue.Replace('\n',' ').Replace('\r',' ').Trim();
        if(text.Length==0)return;
        var key=(sender.PlayerName,sender.World.RowId);
        recent[key]=new(sender.PlayerName,sender.World.RowId,text,Environment.TickCount64,message.LogKind==XivChatType.CustomEmote);
        received++;nextRoster=0;
        if(recent.Count>64)recent.Remove(recent.MinBy(x=>x.Value.At).Key);
    }
    public void Preview() {
        var p=objects.LocalPlayer;if(p==null)return;
        var key=(Name:p.Name.TextValue,World:p.HomeWorld.RowId);
        recent[key]=new(key.Name,key.World,"Local preview — speech bubbles follow each character and fade after 20 seconds.",Environment.TickCount64,false);
        previewAt=Environment.TickCount64;
        nextRoster=0;
    }
    public void Clear(){recent.Clear();speakers.Clear();drawn=0;}
    public void Dispose(){Clear();}
    public object Status=>new{Enabled=studio.Settings.RpSpeechBubblesEnabled,Received=received,Active=recent.Count,Drawn=drawn,LifetimeSeconds=20,FadeSeconds=3,PreviewAgeMs=previewAt==0?-1:Environment.TickCount64-previewAt,PreviewGeometry=previewGeometry};
    public void Draw() {
        drawn=0;
        long now=Environment.TickCount64;
        foreach(var key in recent.Where(x=>now-x.Value.At>=20000).Select(x=>x.Key).ToArray())recent.Remove(key);
        if(!studio.RpMinimal || !studio.Settings.RpSpeechBubblesEnabled || recent.Count==0)return;
        if(now>=nextRoster) {
            nextRoster=now+250;speakers.Clear();
            foreach(var p in objects.OfType<IPlayerCharacter>()) {
                var key=(p.Name.TextValue,p.HomeWorld.RowId);
                if(recent.ContainsKey(key))speakers[key]=(p,p.GameObjectId);
            }
        }
        var size=ImGui.GetIO().DisplaySize;var margin=studio.FrameMargin;
        var font=ImGui.GetFont();float fontSize=20,wrap=360;
        var occupied=new List<(Vector2 Min,Vector2 Max)>();
        var chat=gui.GetAddonByName("ChatLog");
        if(!chat.IsNull && chat.IsVisible) {
            var a=(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)chat.Address;
            occupied.Add((new(a->X-12,a->Y-12),new(a->X+a->GetScaledWidth(true)+12,a->Y+a->GetScaledHeight(true)+12)));
        }
        if(studio.Settings.RpToolsEnabled) {
            var button=Math.Clamp(studio.Settings.RpButtonSize,32,60);var palette=new Vector2(100+12*(button+8),4*(button+20)+28);
            var origin=new Vector2(Math.Clamp(size.X-palette.X-margin+studio.Settings.RpOffsetX,margin,Math.Max(margin,size.X-palette.X-margin)),Math.Clamp(size.Y-palette.Y-margin+studio.Settings.RpOffsetY,margin,Math.Max(margin,size.Y-palette.Y-margin)));
            occupied.Add((origin-new Vector2(12),origin+palette+new Vector2(12)));
        }
        var d=ImGui.GetBackgroundDrawList();
        foreach(var pair in recent.OrderByDescending(x=>x.Value.At)) {
            if(drawn>=16)break;
            if(!speakers.TryGetValue(pair.Key,out var match) || match.Player.GameObjectId!=match.Id)continue;
            var local=objects.LocalPlayer;if(local==null || Vector3.DistanceSquared(local.Position,match.Player.Position)>3600)continue;
            var native=(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)match.Player.Address;
            // The native nameplate offset is updated for model height and pose.
            // GetHeight() can be zero even on a fully visible player model.
            var offset=(Vector3)native->NameplateOffset;
            if(!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z) || offset.LengthSquared()<.01f)
                offset=new Vector3(0,Math.Max(1,native->Height*2),0);
            var head=match.Player.Position+offset;
            if(!gui.WorldToScreen(head,out var anchor) || anchor.X<0 || anchor.X>size.X || anchor.Y<0 || anchor.Y>size.Y)continue;
            if(match.Id==local.GameObjectId && previewAt!=0 && now-previewAt<20000) {
                previewGeometry=new{Source="Native nameplate offset",Offset=offset,Anchor=anchor};
            }
            var speech=pair.Value;var alpha=Opacity(now-speech.At);if(alpha<=0)continue;
            var scale=fontSize/ImGui.GetFontSize();
            var measured=ImGui.CalcTextSize(speech.Text,false,wrap/scale)*scale;
            var box=new Vector2(Math.Max(170,Math.Min(wrap,measured.X))+28,measured.Y+fontSize+32);
            var pos=new Vector2(Math.Clamp(anchor.X-box.X/2,margin,Math.Max(margin,size.X-box.X-margin)),Math.Clamp(anchor.Y-box.Y-90,margin,Math.Max(margin,size.Y-box.Y-margin)));
            bool Hit(Vector2 p,(Vector2 Min,Vector2 Max) r)=>p.X<r.Max.X && p.X+box.X>r.Min.X && p.Y<r.Max.Y && p.Y+box.Y>r.Min.Y;
            for(int tries=0;tries<12;tries++) {
                var overlap=occupied.FindIndex(r=>Hit(pos,r));if(overlap<0)break;
                var r=occupied[overlap];var above=r.Min.Y-box.Y-14;
                if(above>=margin)pos.Y=above;
                else pos.X=Math.Clamp(r.Max.X+14,margin,Math.Max(margin,size.X-box.X-margin));
            }
            if(occupied.Any(r=>Hit(pos,r)))continue;
            uint C(Vector4 c,float mult=1)=>ImGui.GetColorU32(new Vector4(c.X,c.Y,c.Z,c.W*alpha*mult));
            DrawLostCoast(d,pos,box,anchor,alpha);
            d.AddText(font,fontSize,pos+new Vector2(14,10),C(new(.36f,.28f,.10f,1)),speech.Name);
            d.AddText(font,fontSize,pos+new Vector2(14,fontSize+18),C(speech.Action?new Vector4(.36f,.19f,.39f,1):new Vector4(.08f,.10f,.12f,1)),speech.Text,wrap);
            occupied.Add((pos-new Vector2(8),pos+box+new Vector2(8)));drawn++;
        }
    }
    static void DrawLostCoast(ImDrawListPtr d,Vector2 pos,Vector2 box,Vector2 anchor,float alpha) {
        uint C(Vector4 c)=>ImGui.GetColorU32(new Vector4(c.X,c.Y,c.Z,c.W*alpha));
        var gold=C(new(1,.71f,.17f,1));var face=C(new(.87f,.93f,.95f,1));
        float l=MathF.Round(pos.X),t=MathF.Round(pos.Y),r=l+MathF.Round(box.X),b=t+MathF.Round(box.Y),round=18;
        var tailX=Math.Clamp(MathF.Round(anchor.X),l+38,r-38);
        var tip=new Vector2(MathF.Round(anchor.X)-8,Math.Max(b+22,MathF.Round(anchor.Y)-44));
        var tailL=new Vector2(tailX-15,b);var tailR=new Vector2(tailX+16,b);
        d.AddTriangleFilled(tailL,tailR,tip,face);
        d.AddRectFilled(new(l,t),new(r,b),face,round);
        // One stroke only. No overlapping bevels, duplicate closing vertices or
        // layered fills along the rim. Explicit arc subdivisions stay smooth.
        d.PathLineTo(tailR);d.PathLineTo(tip);d.PathLineTo(tailL);
        d.PathArcTo(new(l+round,b-round),round,MathF.PI/2,MathF.PI,12);
        d.PathArcTo(new(l+round,t+round),round,MathF.PI,MathF.PI*1.5f,12);
        d.PathArcTo(new(r-round,t+round),round,MathF.PI*1.5f,MathF.PI*2,12);
        d.PathArcTo(new(r-round,b-round),round,0,MathF.PI/2,12);
        d.PathStroke(gold,ImDrawFlags.Closed,3);
    }
}
