using CinematicMode;

var cases = new[] {
    ("auto",3440,1440,false,"combat","ultrawide monitor"),
    ("auto",2400,1600,false,"rp","laptop monitor"),
    ("auto",2400,1600,true,"combat","combat on laptop"),
    ("rp",3440,1440,false,"rp","manual RP on ultrawide"),
    ("combat",2400,1600,false,"combat","manual combat on laptop"),
    ("rp",2400,1600,true,"combat","combat overrides RP"),
    ("off",3440,1440,true,"off","off disables automatic behavior"),
    ("auto",0,0,false,"rp","no division by zero"),
};
foreach (var (mode,w,h,combat,expected,label) in cases) {
    var actual=DisplayHudPolicy.SelectMode(mode,w,h,combat);
    if(actual!=expected) throw new Exception($"{label}: expected {expected}, got {actual}");
    Console.WriteLine($"PASS {label}");
}
if(DisplayHudPolicy.HasSettled(999,0,false)) throw new Exception("Resize should wait for stability.");
if(!DisplayHudPolicy.HasSettled(1000,0,false)) throw new Exception("Stable display should apply.");
if(!DisplayHudPolicy.HasSettled(0,0,true)) throw new Exception("Combat must not wait for debounce.");
Console.WriteLine("PASS display debounce and immediate combat fallback");
Console.WriteLine("11 policy checks passed. Live combat itself is not simulated.");
