namespace CinematicMode;

/// <summary>Profile choices depend on the physical monitor, not a tiled window's aspect ratio.</summary>
public static class DisplayHudPolicy
{
    public static string SelectMode(string mode,int monitorWidth,int monitorHeight,bool inCombat)
    {
        if (mode=="off") return "off";
        if (inCombat) return "combat";
        if (mode is "combat" or "rp") return mode;
        return monitorHeight>0 && (double)monitorWidth/monitorHeight>=2.0 ? "combat" : "rp";
    }

    public static bool HasSettled(long now,long pendingSince,bool inCombat) => inCombat || now-pendingSince>=1000;
}
