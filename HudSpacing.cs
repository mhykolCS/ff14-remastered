using System.Numerics;

namespace CinematicMode;

public static class HudSpacing
{
    public static Vector2 AboveChat(Vector2 origin,Vector2 size,Vector2 chat,Vector2 chatSize,float margin)
    {
        if(origin.X>=chat.X+chatSize.X || origin.X+size.X<=chat.X || origin.Y>=chat.Y+chatSize.Y || origin.Y+size.Y+12<=chat.Y)return origin;
        var top=chat.Y-size.Y-12;
        return top>=margin?new(origin.X,top):origin;
    }
    public static Dictionary<string,Vector2> Place(Vector2 viewport,float margin,float chatRight,IReadOnlyDictionary<string,Vector2> sizes)
    {
        var result=new Dictionary<string,Vector2>();
        string[] stack={"_Exp","_ParameterWidget","_ActionBar03","_ActionBar","_ActionBar01","_ActionBar02"};
        var available=stack.Where(sizes.ContainsKey).ToArray();if(available.Length==0)return result;
        var widest=available.Max(n=>sizes[n].X);
        var left=Math.Clamp(Math.Max((viewport.X-widest)/2,chatRight),margin,Math.Max(margin,viewport.X-margin-widest));
        var center=left+widest/2;var bottom=viewport.Y-margin;
        foreach(var name in available) {
            var size=sizes[name];bottom-=size.Y;
            if(bottom<margin || size.X>viewport.X-2*margin)return new();
            result[name]=new(center-size.X/2,bottom);bottom-=12;
        }
        string[] status={"_StatusCustom0","_StatusCustom1"};
        var buffs=status.Where(sizes.ContainsKey).ToArray();
        if(buffs.Length==0)return result;
        var total=buffs.Sum(n=>sizes[n].X)+12*(buffs.Length-1);
        var height=buffs.Max(n=>sizes[n].Y);
        if(bottom-height<margin || total>viewport.X-2*margin)return result;
        var x=Math.Clamp(center-total/2,margin,viewport.X-margin-total);
        foreach(var name in buffs){result[name]=new(x,bottom-sizes[name].Y);x+=sizes[name].X+12;}
        return result;
    }
}
