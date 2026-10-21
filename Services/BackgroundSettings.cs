using System.IO;
using System.Text.Json;
namespace ReleasePilot.Services;
public sealed class BackgroundSettings
{
    public bool Enabled {get;set;}
    public bool StartAtLogin {get;set;} // Legacy v0.3.0 setting; no longer starts the desktop.
    public string ProxyUrl {get;set;}="";
    public int IntervalMinutes {get;set;}=60;
    public Dictionary<string,string> NotifiedTags {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public static string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReleasePilot");
    public static string SettingsPath=>Path.Combine(DataDirectory,"background.json");
    public static BackgroundSettings Load()
    {
        try
        {
            var settings=JsonSerializer.Deserialize<BackgroundSettings>(File.ReadAllText(SettingsPath))??new();
            settings.IntervalMinutes=Math.Clamp(settings.IntervalMinutes,5,1440);
            settings.NotifiedTags=new(settings.NotifiedTags??new(),StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary=SettingsPath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,SettingsPath,true);
    }
}
