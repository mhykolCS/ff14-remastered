using System.Text;

namespace CinematicMode;

public sealed class RpWritingState
{
    public string Draft="", Notes="", Channel="/s";
    public bool Ooc;
}

public static class RpWriting
{
    // Leave room below the native chat limit for the selected channel and markers.
    public const int MaxBytes=450;
    public static List<string> Chunks(string text,string channel,bool ooc)
    {
        if(channel is not ("/s" or "/em" or "/p")) throw new ArgumentException("Choose Say, Emote, or Party.");
        var remaining=text.Replace('\r',' ').Replace('\n',' ').Trim();
        var result=new List<string>();
        if(remaining.Length==0)return result;
        var prefix=channel+" "+(ooc?"(( ":"");
        var suffix=ooc?" ))":"";
        // Reserve a continuation marker even for the final chunk.
        var limit=MaxBytes-Encoding.UTF8.GetByteCount(prefix+suffix+" [999/999]");
        while(remaining.Length>0) {
            int end=0,bytes=0,lastSpace=-1;
            foreach(var rune in remaining.EnumerateRunes()) {
                if(bytes+rune.Utf8SequenceLength>limit)break;
                if(Rune.IsWhiteSpace(rune))lastSpace=end;
                end+=rune.Utf16SequenceLength;bytes+=rune.Utf8SequenceLength;
            }
            if(end<remaining.Length && lastSpace>0)end=lastSpace;
            if(end==0)throw new InvalidOperationException("Unable to split this draft.");
            result.Add(prefix+remaining[..end].TrimEnd()+suffix);
            remaining=remaining[end..].TrimStart();
        }
        if(result.Count>1)for(int i=0;i<result.Count;i++)result[i]+=$" [{i+1}/{result.Count}]";
        return result;
    }
}
