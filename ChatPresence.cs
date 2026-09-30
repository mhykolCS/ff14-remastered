using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace CinematicMode;

public enum PresenceKind { Unknown,Online,Offline }
public sealed record PresenceInfo(PresenceKind Kind,bool Friend,string Activity,string Source);

/// <summary>Local chat badges and a live view of presence exposed by the game.</summary>
public sealed unsafe class ChatPresence : IDisposable
{
    const uint LinkId=7301;
    static readonly Vector4 Red=new(1,.43f,.49f,1);
    readonly HudStudio studio;
    readonly IObjectTable objects;
    readonly IChatGui chat;
    readonly DalamudLinkPayload link;
    readonly Dictionary<(string Name,uint World),PresenceInfo> known=new();
    readonly Dictionary<(string Name,uint World),long> speakers=new();
    long nextRefresh,decorated;
    bool open;
    int friendCount,companyCount,nearbyCount;
    public ChatPresence(HudStudio studio,IObjectTable objects,IChatGui chat) {
        this.studio=studio;this.objects=objects;this.chat=chat;
        link=chat.AddChatLinkHandler(LinkId,(_,_)=>Open());
    }
    public void Open(){open=true;nextRefresh=0;}
    public static PresenceInfo FromMask(ulong mask,bool friend,string source) {
        const ulong unavailable=(1UL<<5)|(1UL<<6)|(1UL<<7)|(1UL<<8)|(1UL<<9);
        var kind=mask==0 || (mask&(1UL<<10))!=0?PresenceKind.Offline:(mask&unavailable)!=0?PresenceKind.Unknown:PresenceKind.Online;
        var activity=(mask&(1UL<<17))!=0?"Away":(mask&(1UL<<12))!=0?"Busy":(mask&(1UL<<22))!=0?"RP":"";
        return new(kind,friend,kind==PresenceKind.Online?activity:"",source);
    }
    public static (string Glyph,Vector4 Colour) Marker(PresenceInfo p)=>p.Kind switch {
        PresenceKind.Online when p.Friend=>("●",IrohChatStyle.Blue),
        PresenceKind.Online=>("★",IrohChatStyle.Green),
        PresenceKind.Offline=>("★",Red),
        _=>("★",IrohChatStyle.Muted)
    };
    public void Tick() {
        if(!studio.Settings.ChatPresenceEnabled || Environment.TickCount64<nextRefresh)return;
        nextRefresh=Environment.TickCount64+1000;known.Clear();friendCount=companyCount=nearbyCount=0;
        if(objects.LocalPlayer==null)return;
        Read((InfoProxyCommonList*)InfoProxyFreeCompanyMember.Instance(),false,ref companyCount);
        Read((InfoProxyCommonList*)InfoProxyFriendList.Instance(),true,ref friendCount);
        foreach(var p in objects.OfType<IPlayerCharacter>()) {
            var key=(p.Name.TextValue,p.HomeWorld.RowId);var row=p.OnlineStatus.RowId;
            var state=FromMask(row<64?1UL<<(int)row:1UL<<47,p.StatusFlags.HasFlag(StatusFlags.Friend),"Visible character");
            // A rendered character is online even if its status row is zero.
            known[key]=state with {Kind=PresenceKind.Online};nearbyCount++;
        }
        foreach(var key in speakers.Where(x=>Environment.TickCount64-x.Value>3600000).Select(x=>x.Key).ToArray())speakers.Remove(key);
    }
    void Read(InfoProxyCommonList* proxy,bool friend,ref int count) {
        if(proxy==null || proxy->CharData==null || proxy->EntryCount>200)return;
        for(int i=0;i<proxy->EntryCount;i++) {
            var entry=proxy->CharData+i;
            if(entry->ContentId==0 || entry->HomeWorld==0)continue;
            var name=entry->NameString;if(string.IsNullOrWhiteSpace(name))continue;
            known[(name,entry->HomeWorld)]=FromMask((ulong)entry->State,friend&&!entry->WaitingForFriendListApproval,friend?"Game friends list":"Game Free Company list");count++;
        }
    }
    public PresenceInfo Resolve(string name,uint world,bool arriving=false) {
        var key=(name,world);
        var value=known.GetValueOrDefault(key)??new(PresenceKind.Unknown,false,"","Not available from FFXIV");
        // An incoming message is direct evidence of presence, even if a cached
        // social list has not updated yet. This evidence expires after two minutes.
        if(arriving || (speakers.TryGetValue(key,out var at) && Environment.TickCount64-at<120000))
            if(value.Kind!=PresenceKind.Online)value=value with {Kind=PresenceKind.Online,Activity="",Source="Recent incoming message"};
        return value;
    }
    public void Record(IChatMessage message) {
        if(!studio.Settings.ChatPresenceEnabled || message.LogKind==XivChatType.TellOutgoing)return;
        // Restored history is not evidence that its sender is currently online.
        if(message.Timestamp>0 && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-message.Timestamp)>120)return;
        var sender=message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if(sender==null && message.LogKind is XivChatType.CustomEmote or XivChatType.StandardEmote)
            sender=message.Message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if(sender==null)return;
        speakers[(sender.PlayerName,sender.World.RowId)]=Environment.TickCount64;
        if(speakers.Count>256)speakers.Remove(speakers.MinBy(x=>x.Value).Key);
    }
    public SeString Decorate(SeString source) {
        var result=new List<Payload>();bool added=false;
        foreach(var payload in source.Payloads) {
            if(!added && payload is PlayerPayload p) {
                var state=Resolve(p.PlayerName,p.World.RowId);var (glyph,colour)=Marker(state);
                var badge=glyph+(studio.Settings.ChatPresenceDetails && state.Activity!=""?" "+state.Activity:"");
                result.Add(link);result.AddRange(IrohChatStyle.Color((SeString)badge,colour).Payloads);
                result.Add(RawPayload.LinkTerminator);result.Add(new TextPayload(" "));added=true;decorated++;
            }
            result.Add(payload);
        }
        return new SeString(result);
    }
    public void Preview() {
        var line=new SeString(new TextPayload("[Local presence sample] "));
        foreach(var p in new[]{new PresenceInfo(PresenceKind.Online,false,"RP",""),new PresenceInfo(PresenceKind.Online,true,"",""),new PresenceInfo(PresenceKind.Offline,false,"",""),new PresenceInfo(PresenceKind.Unknown,false,"","")}) {
            var (glyph,colour)=Marker(p);line.Payloads.Add(link);
            line.Payloads.AddRange(IrohChatStyle.Color((SeString)(glyph+" "+(p.Friend?"friend":p.Activity!=""?p.Activity:p.Kind.ToString())+"  "),colour).Payloads);
            line.Payloads.Add(RawPayload.LinkTerminator);
        }
        chat.Print(line);
    }
    public void Draw() {
        if(!open || !studio.Settings.ChatPresenceEnabled)return;
        ImGui.SetNextWindowSize(new(630,440),ImGuiCond.FirstUseEver);
        if(!ImGui.Begin("Chat presence###FF14RemasteredPresence",ref open)){ImGui.End();return;}
        if(studio.ShowRpBorder) {
            var pos=ImGui.GetWindowPos();var size=ImGui.GetWindowSize();var screen=ImGui.GetIO().DisplaySize;var m=studio.FrameMargin;
            ImGui.SetWindowPos(new(Math.Clamp(pos.X,m,Math.Max(m,screen.X-size.X-m)),Math.Clamp(pos.Y,m,Math.Max(m,screen.Y-size.Y-m))));
        }
        ImGui.TextWrapped("Current presence from FFXIV. Blue dots are online friends; green, red and grey stars mean online, offline and unknown.");
        ImGui.TextDisabled("Badges on messages show status at arrival. This list updates every second.");
        ImGui.Separator();
        if(ImGui.BeginTable("presence",3,ImGuiTableFlags.RowBg|ImGuiTableFlags.ScrollY|ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("Character");ImGui.TableSetupColumn("Presence / activity");ImGui.TableSetupColumn("Source");ImGui.TableHeadersRow();
            foreach(var key in speakers.Keys.Concat(known.Where(x=>x.Value.Friend).Select(x=>x.Key)).Distinct().OrderBy(k=>k.Name)) {
                var p=Resolve(key.Name,key.World);var (glyph,colour)=Marker(p);
                ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextColored(IrohChatStyle.NameColor(key.Name),key.Name);
                ImGui.TableNextColumn();ImGui.TextColored(colour,glyph+" "+p.Kind+(p.Activity==""?"":" · "+p.Activity));
                ImGui.TableNextColumn();ImGui.TextUnformatted(p.Source);
            }
            ImGui.EndTable();
        }
        ImGui.End();
    }
    public object Status=>new{Enabled=studio.Settings.ChatPresenceEnabled,Detailed=studio.Settings.ChatPresenceDetails,DecoratedMessages=decorated,FriendsAvailable=friendCount,CompanyAvailable=companyCount,NearbyAvailable=nearbyCount,RecentSpeakers=speakers.Count,WindowOpen=open,MessageBadges="Status at arrival; click for current presence",LocalOnly=true};
    public void Dispose(){chat.RemoveChatLinkHandler(LinkId);known.Clear();speakers.Clear();}
}
