namespace CinematicMode;

public sealed record MinionEntry(uint Id,string Name,uint Icon);

public static class MinionSelection
{
    public const int Rows=3,Columns=12,Capacity=Rows*Columns;
    public static readonly string[] RowNames={"COZY","LEGENDS","CURIOS"};
    // Companion sheet IDs are language independent. Only owned entries are used.
    public static readonly uint[] Recommended={
        110,97,141,139,19,35,498,137,16,138,279,75,
        119,173,193,130,133,224,230,21,179,181,101,461,
        81,66,95,44,33,41,12,28,288,361,52,51
    };
    public static List<uint> Choose(IEnumerable<uint> owned,IEnumerable<uint>? saved=null)
    {
        var available=owned.Where(id=>id>0).ToHashSet();
        return (saved??Recommended).Concat(Recommended).Concat(available.Order())
            .Where(available.Contains).Distinct().Take(Capacity).ToList();
    }
    public static void Replace(List<uint> favourites,int slot,uint id)
    {
        if(slot<0 || slot>=favourites.Count || id==0)return;
        var other=favourites.IndexOf(id);
        if(other>=0) (favourites[slot],favourites[other])=(favourites[other],favourites[slot]);
        else favourites[slot]=id;
    }
}
