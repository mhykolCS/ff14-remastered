using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace CinematicMode;

/// <summary>Opt-in local rendering of native chat using Iroh's palette. Never transmits or logs chat.</summary>
public sealed class IrohChatStyle : IDisposable
{
    public static readonly Vector4 Text=Rgb(211,216,232),Muted=Rgb(128,136,160),Cyan=Rgb(111,215,255),
        Blue=Rgb(124,156,255),Green=Rgb(112,220,151),Yellow=Rgb(245,200,100),Magenta=Rgb(210,139,255),Peach=Rgb(255,154,115);
    static readonly Vector4[] Palette=[Cyan,Blue,Green,Yellow,Magenta,Peach];
    static Vector4 Rgb(byte r,byte g,byte b)=>new(r/255f,g/255f,b/255f,1);
    readonly IChatGui chat;
    readonly HudStudio studio;
    readonly IPlayerState player;
    readonly IPluginLog log;
    readonly RpSpeechBubbles bubbles;
    readonly ChatPresence presence;
    long styled;
    public IrohChatStyle(IChatGui chat,HudStudio studio,IPlayerState player,IPluginLog log,RpSpeechBubbles bubbles,ChatPresence presence) {
        this.chat=chat;this.studio=studio;this.player=player;this.log=log;this.bubbles=bubbles;this.presence=presence;
        chat.ChatMessage+=OnChat;
    }
    public static Vector4 NameColor(string name) {
        uint hash=0;foreach(var b in Encoding.UTF8.GetBytes(name))hash=unchecked(hash*31+b);
        return Palette[hash%6];
    }
    static bool Conversation(XivChatType kind)=>kind is XivChatType.Say or XivChatType.Yell or XivChatType.Shout or
        XivChatType.TellIncoming or XivChatType.TellOutgoing or XivChatType.Party or XivChatType.Alliance or
        XivChatType.FreeCompany or XivChatType.NoviceNetwork or XivChatType.CrossParty or XivChatType.PvPTeam or
        XivChatType.CustomEmote or XivChatType.StandardEmote or
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 or
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4 or
        XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8;
    public static SeString Color(SeString value,Vector4 color) {
        // Preserve existing payload objects, including native player/item links.
        var result=SeString.Parse(new Lumina.Text.SeStringBuilder().PushColorRgba(color).ToArray()).Payloads.ToList();
        result.AddRange(value.Payloads);
        result.AddRange(SeString.Parse(new Lumina.Text.SeStringBuilder().PopColor().ToArray()).Payloads);
        return new SeString(result);
    }
    public static SeString ColorNames(SeString source) {
        var result=new List<Payload>();PlayerPayload? name=null;
        foreach(var payload in source.Payloads) {
            if(payload is PlayerPayload p)name=p;
            if(payload is TextPayload text && name!=null && !string.IsNullOrEmpty(text.Text)) {
                result.AddRange(Color(new SeString(text),NameColor(name.PlayerName)).Payloads);name=null;
            }else result.Add(payload);
        }
        return new SeString(result);
    }
    SeString Body(SeString source,XivChatType kind) {
        var accent=kind is XivChatType.CustomEmote or XivChatType.StandardEmote?Magenta:Text;
        var own=player.IsLoaded?player.CharacterName:"";
        var first=own.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
        var result=new List<Payload>();
        foreach(var payload in ColorNames(source).Payloads) {
            if(payload is not TextPayload text || string.IsNullOrEmpty(text.Text) || first.Length<3) {result.Add(payload);continue;}
            var pattern=$"(?<![\\p{{L}}\\p{{N}}])(?:{Regex.Escape(own)}|{Regex.Escape(first)})(?![\\p{{L}}\\p{{N}}])";
            int start=0;
            foreach(Match m in Regex.Matches(text.Text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)) {
                if(m.Index>start)result.Add(new TextPayload(text.Text[start..m.Index]));
                result.AddRange(Color((SeString)m.Value,Yellow).Payloads);start=m.Index+m.Length;
            }
            if(start<text.Text.Length)result.Add(new TextPayload(text.Text[start..]));
        }
        return Color(new SeString(result),accent);
    }
    void OnChat(IHandleableChatMessage message) {
        if(message.IsHandled)return;
        try {
            bubbles.Record(message);
            if(!Conversation(message.LogKind))return;
            presence.Record(message);
            if(studio.Settings.IrohChatEnabled) {
                var sender=message.Sender;
                message.Sender=sender.Payloads.OfType<PlayerPayload>().Any()?ColorNames(sender):Color(sender,NameColor(sender.TextValue));
                message.Message=Body(message.Message,message.LogKind);styled++;
            }
            if(studio.Settings.ChatPresenceEnabled) {
                if(message.Sender.Payloads.OfType<PlayerPayload>().Any())message.Sender=presence.Decorate(message.Sender);
                else if(message.LogKind is XivChatType.CustomEmote or XivChatType.StandardEmote)message.Message=presence.Decorate(message.Message);
            }
        }catch(Exception ex){log.Error(ex,"Could not style a chat line; native chat remains available.");}
    }
    public void Preview() {
        var line=new SeString(new TextPayload("[Local colour sample] "));
        line.Payloads.AddRange(Color((SeString)"Ari: ",Cyan).Payloads);
        line.Payloads.AddRange(Color((SeString)"Pale message text  ",Text).Payloads);
        line.Payloads.AddRange(Color((SeString)"Mira: ",Green).Payloads);
        line.Payloads.AddRange(Color((SeString)"*smiles*  ",Magenta).Payloads);
        line.Payloads.AddRange(Color((SeString)"your name",Yellow).Payloads);
        chat.Print(line);
    }
    public object Status=>new{Enabled=studio.Settings.IrohChatEnabled,StyledMessages=styled,Palette="Iroh",History="New incoming lines",Bubbles=bubbles.Status};
    public void Dispose()=>chat.ChatMessage-=OnChat;
}
