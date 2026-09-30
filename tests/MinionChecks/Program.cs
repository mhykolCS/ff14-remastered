using System.Numerics;
using CinematicMode;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
var recommended=MinionSelection.Recommended;
Check(recommended.Distinct().Count()==36,"three full rows of unique recommendations");
Check(MinionSelection.Choose(recommended).SequenceEqual(recommended),"curated order retained when all are owned");
var sparse=MinionSelection.Choose(new uint[]{0,97,97,110,999});
Check(sparse.Count==3 && sparse.Contains(999) && !sparse.Contains(0),"small collections use only real unique unlocked entries");
Check(MinionSelection.Choose(Array.Empty<uint>()).Count==0,"empty collection remains empty");
var saved=MinionSelection.Choose(recommended,new uint[]{0,999,97,97,110});
Check(saved.Count==36 && saved.Take(2).SequenceEqual(new uint[]{97,110}),"saved order survives invalid and duplicate IDs");
var many=MinionSelection.Choose(Enumerable.Range(1,600).Select(i=>(uint)i));
Check(many.Count==36 && many.Distinct().Count()==36,"large collection stays within panel capacity");
var swapped=new List<uint>{110,97,141};
MinionSelection.Replace(swapped,0,141);
Check(swapped.SequenceEqual(new uint[]{141,97,110}),"choosing another favourite swaps slots");
MinionSelection.Replace(swapped,1,498);
Check(swapped.SequenceEqual(new uint[]{141,498,110}),"new favourite replaces only the chosen slot");
MinionSelection.Replace(swapped,8,35);MinionSelection.Replace(swapped,0,0);
Check(swapped.SequenceEqual(new uint[]{141,498,110}),"invalid edits leave favourites intact");

var sizes=new Dictionary<string,Vector2>{
    ["_ActionBar"]=new(748.8f,86.4f),["_ActionBar01"]=new(748.8f,86.4f),["_ActionBar02"]=new(748.8f,86.4f),
    ["_ActionBar03"]=new(624,72),["_ParameterWidget"]=new(512,44),["_Exp"]=new(486,44),
    ["_StatusCustom0"]=new(275,90.2f),["_StatusCustom1"]=new(300,98.4f)
};
foreach(var (viewport,margin,chat) in new[]{(new Vector2(2388,1532),96f,1032f),(new Vector2(3428,1372),32f,0f)}) {
    var positions=HudSpacing.Place(viewport,margin,chat,sizes);
    Check(positions.Count==sizes.Count,"all visible group elements are placed");
    foreach(var (name,p) in positions) {
        var size=sizes[name];
        Check(p.X>=margin && p.Y>=margin && p.X+size.X<=viewport.X-margin+.01f && p.Y+size.Y<=viewport.Y-margin+.01f,"group stays inside frame");
        Check(p.X>=chat,"group clears expanded chat");
    }
    var names=positions.Keys.ToArray();
    for(int i=0;i<names.Length;i++)for(int j=i+1;j<names.Length;j++) {
        var a=positions[names[i]];var b=positions[names[j]];var sa=sizes[names[i]];var sb=sizes[names[j]];
        Check(a.X+sa.X<=b.X || b.X+sb.X<=a.X || a.Y+sa.Y<=b.Y || b.Y+sb.Y<=a.Y,"native group rectangles do not overlap");
    }
}
Check(HudSpacing.Place(new(600,400),32,0,sizes).Count==0,"undersized viewport does not apply a partial layout");
Check(HudSpacing.Place(new(2388,1532),96,1032,new Dictionary<string,Vector2>{{"_ActionBar03",new(624,72)}}).Count==1,"hidden native bars are not introduced");
Check(HudSpacing.AboveChat(new(96,826),new(724,220),new(30,1032),new(620,300),32)==new Vector2(96,800),"combat minion panel clears chat history with padding");
Check(HudSpacing.AboveChat(new(96,753),new(724,284),new(30,1032),new(620,300),32)==new Vector2(96,736),"combat emotes clear chat history too");
Check(HudSpacing.AboveChat(new(1568,1152),new(724,284),new(96,756),new(920,680),96)==new Vector2(1568,1152),"RP emotes alongside chat retain their position");
Console.WriteLine($"{checks} minion selection and HUD spacing checks passed.");
