using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CinematicMode;

/// <summary>A scalable, click-through ornamental frame drawn only in RP views.</summary>
public static class RpFrame
{
    public static float SafeMargin(float inset)=>Math.Clamp(inset,6,48)+84;
    public static void Draw(float opacity,float inset,bool externalFps=false)
    {
        var size=ImGui.GetIO().DisplaySize;
        if(size.X<640 || size.Y<480)return;
        var d=ImGui.GetBackgroundDrawList();
        float a=Math.Clamp(opacity,0,1),m=Math.Clamp(inset,6,48);
        uint dark=ImGui.GetColorU32(new Vector4(.025f,.03f,.045f,a*.7f));
        uint gold=ImGui.GetColorU32(new Vector4(.72f,.59f,.36f,a));
        uint light=ImGui.GetColorU32(new Vector4(.96f,.84f,.57f,a*.75f));
        uint soft=ImGui.GetColorU32(new Vector4(.72f,.59f,.36f,a*.32f));
        // DXVK's fixed FPS readout is outside native HUD control. Reserve a header
        // strip until the next game launch picks up the disabled DXVK overlay.
        var lo=new Vector2(m,externalFps?Math.Max(40,m):m);var hi=size-new Vector2(m);
        d.AddRect(lo+new Vector2(1),hi+new Vector2(1),dark,3,ImDrawFlags.None,4);
        d.AddRect(lo,hi,gold,3,ImDrawFlags.None,1.25f);
        d.AddRect(lo+new Vector2(5),hi-new Vector2(5),soft,2,ImDrawFlags.None,1);
        void Corner(Vector2 origin,float sx,float sy) {
            Vector2 P(float x,float y)=>origin+new Vector2(x*sx,y*sy);
            d.AddLine(P(0,74),P(0,0),light,2);
            d.AddLine(P(0,0),P(74,0),light,2);
            d.AddBezierCubic(P(8,70),P(8,30),P(30,8),P(70,8),gold,1.5f,22);
            d.AddBezierCubic(P(12,48),P(34,42),P(42,34),P(48,12),soft,1,18);
            d.AddBezierCubic(P(12,30),P(32,44),P(42,14),P(22,16),light,1.2f,22);
            d.AddLine(P(12,12),P(22,5),gold,1);
            d.AddLine(P(22,5),P(32,12),gold,1);
            d.AddLine(P(32,12),P(22,19),gold,1);
            d.AddLine(P(22,19),P(12,12),gold,1);
            d.AddCircleFilled(P(22,12),2.2f,light,12);
            d.AddLine(P(84,0),P(104,0),light,2);
            d.AddLine(P(0,84),P(0,104),light,2);
        }
        Corner(lo,1,1);Corner(new(hi.X,lo.Y),-1,1);Corner(hi,-1,-1);Corner(new(lo.X,hi.Y),1,-1);
        var c=new Vector2(size.X/2,lo.Y);
        d.AddQuadFilled(c+new Vector2(-7,0),c+new Vector2(0,-5),c+new Vector2(7,0),c+new Vector2(0,5),gold);
        d.AddLine(c+new Vector2(-54,0),c+new Vector2(-18,0),light,2);
        d.AddLine(c+new Vector2(18,0),c+new Vector2(54,0),light,2);
    }
}
