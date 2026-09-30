using CinematicMode;
using System.Text;

int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
Check(RpWriting.Chunks("","/s",false).Count==0,"Empty drafts");
Check(RpWriting.Chunks("She smiles.","/em",false).Single()=="/em She smiles.","Emote header");
Check(RpWriting.Chunks("A pause.","/p",true).Single()=="/p (( A pause. ))","Party OOC wrapper");
foreach(var source in new[]{new string('x',5000),string.Join(' ',Enumerable.Repeat("A quiet evening in Kugane.",100)),string.Concat(Enumerable.Repeat("月明かり🌙🪷",250))}) {
 var chunks=RpWriting.Chunks(source,"/s",false);
 Check(chunks.Count>1,"Long draft splits");
 Check(chunks.All(c=>Encoding.UTF8.GetByteCount(c)<=RpWriting.MaxBytes),"UTF8 byte limit includes markers");
 Check(chunks.Select((c,i)=>c.EndsWith($"[{i+1}/{chunks.Count}]")).All(x=>x),"Sequential continuation markers");
 var bodies=chunks.Select(c=>c[3..c.LastIndexOf(" [",StringComparison.Ordinal)]).ToArray();
 var restored=source.Contains(' ')?string.Join(' ',bodies):string.Concat(bodies);
 Check(restored==source,"Text survives splitting, including surrogate pairs");
 Check(chunks.All(c=>!c.Contains('\uFFFD')),"No damaged Unicode");
}
Check(!RpWriting.Chunks("one\ntwo\rthree","/em",false).Single().Contains('\n'),"Multiline text becomes one chat line");
try{RpWriting.Chunks("text","/shout",false);throw new Exception("Unexpected channel accepted");}catch(ArgumentException){checks++;}
Console.WriteLine($"{checks} writing checks passed.");
