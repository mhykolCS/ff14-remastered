namespace CinematicMode;

/// <summary>Stable ordered matching preserves timers when the native transcript scrolls.</summary>
public static class ChatFadeSequence
{
    public static int[] Match(IReadOnlyList<byte[]> previous,IReadOnlyList<byte[]> current)
    {
        var lengths=new int[previous.Count+1,current.Count+1];
        for(int i=previous.Count-1;i>=0;i--)
            for(int j=current.Count-1;j>=0;j--)
                lengths[i,j]=previous[i].AsSpan().SequenceEqual(current[j])?lengths[i+1,j+1]+1:Math.Max(lengths[i+1,j],lengths[i,j+1]);
        var result=Enumerable.Repeat(-1,current.Count).ToArray();
        for(int i=0,j=0;i<previous.Count && j<current.Count;) {
            if(previous[i].AsSpan().SequenceEqual(current[j])) {result[j++]=i++;}
            else if(lengths[i+1,j]>=lengths[i,j+1])i++;
            else j++;
        }
        return result;
    }
}
