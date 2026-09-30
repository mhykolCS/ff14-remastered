using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Newtonsoft.Json;
using NativePlayer=FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace CinematicMode;

public static unsafe class JobCatalog
{
    public static void Export(IDataManager data,string directory)
    {
        var module=RaptureGearsetModule.Instance();
        var player=NativePlayer.Instance();
        if(module==null || player==null) return;
        var gearsets=new List<object>();
        for(int i=0;i<100;i++)
        {
            var e=module->GetGearset(i);
            if(e==null || !e->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) continue;
            gearsets.Add(new{Index=i,Number=i+1,Name=e->NameString,ClassJob=e->ClassJob,GlamourPlate=e->GlamourSetLink,
                Icon=module->GetClassJobIconForGearset(i),Flags=e->Flags.ToString()});
        }
        var jobs=data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!.Where(j=>j.RowId>0).Select(j=>new{
            Id=j.RowId,Name=j.Name.ExtractText(),Abbreviation=j.Abbreviation.ExtractText(),j.Role,
            Level=player->GetClassJobLevel((int)j.RowId,false)}).ToArray();
        var actions=data.GetExcelSheet<Lumina.Excel.Sheets.Action>()!.Where(a=>!a.IsPvP && a.ClassJobCategory.RowId!=0).Select(a=>new{
            Id=a.RowId,Name=a.Name.ExtractText(),a.ClassJobLevel,a.IsRoleAction,ClassJob=a.ClassJob.RowId,
            Category=a.ClassJobCategory.RowId,a.IsPlayerAction,a.Icon,Jobs=data.GetExcelSheet<Lumina.Excel.Sheets.ClassJobCategory>()!.GetRow(a.ClassJobCategory.RowId).GetType().GetProperties().Where(p=>p.PropertyType==typeof(bool) && (bool)p.GetValue(a.ClassJobCategory.Value)!).Select(p=>p.Name).ToArray()}).ToArray();
        var crafts=data.GetExcelSheet<Lumina.Excel.Sheets.CraftAction>()!.Select(a=>new{
            Id=a.RowId,Name=a.Name.ExtractText(),ClassJob=a.ClassJob.RowId,a.Icon}).ToArray();
        File.WriteAllText(Path.Combine(directory,"job-catalog.json"),JsonConvert.SerializeObject(new{
            CurrentGearset=module->CurrentGearsetIndex,ExpacJobHotbarsCreated=RaptureHotbarModule.Instance()->ExpacJobHotbarsCreated.ToArray(),Jobs=jobs,Gearsets=gearsets,Actions=actions,CraftActions=crafts},Formatting.Indented));
    }
}
