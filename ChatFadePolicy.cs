namespace CinematicMode;

/// <summary>Monotonic, content-free timing for the native chat view.</summary>
public sealed class ChatFadePolicy
{
    public const long MessageHoldMs=20_000, GraceMs=2_000, FadeOutMs=1_000, FadeInMs=150;
    long last=-1,releaseAt=long.MinValue;
    bool interacting;
    public float HistoryAlpha {get;private set;}
    public void Update(long now,bool typing,bool hovering)
    {
        var dt=last<0?0:Math.Clamp(now-last,0,250);last=now;
        var active=typing||hovering;
        if(interacting && !active)releaseAt=now;
        interacting=active;
        if(active)HistoryAlpha=Math.Min(1,HistoryAlpha+dt/(float)FadeInMs);
        else if(releaseAt!=long.MinValue && now-releaseAt<=GraceMs)HistoryAlpha=1;
        else HistoryAlpha=Math.Max(0,HistoryAlpha-dt/(float)FadeOutMs);
    }
    public static float MessageAlpha(long age)
    {
        if(age<0)return 0;
        if(age<FadeInMs)return age/(float)FadeInMs;
        if(age<=MessageHoldMs+FadeInMs)return 1;
        return Math.Clamp(1-(age-MessageHoldMs-FadeInMs)/(float)FadeOutMs,0,1);
    }
    public float Alpha(long now,long arrived)=>Math.Max(HistoryAlpha,MessageAlpha(now-arrived));
}
