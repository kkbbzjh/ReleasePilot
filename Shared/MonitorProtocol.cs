using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace ReleasePilot.Monitoring;
public sealed class MonitorProfile
{
    public bool Enabled {get;set;}
    public int IntervalMinutes {get;set;}=60;
    public string Revision {get;set;}="";
    public string ProxyUrl {get;set;}="";
    public List<WatchSubscription> Subscriptions {get;set;}=[];
}
public sealed class WatchSubscription
{
    public string Repo {get;set;}="";
    public string SeenTag {get;set;}="";
    public string InstalledTag {get;set;}="";
    public bool HasUpdate {get;set;}
    public bool IsCurrent {get;set;}
}
public sealed class WatchRelease
{
    public string Tag {get;set;}="";
    public string Url {get;set;}="";
    public string Notes {get;set;}="";
    public bool Notify {get;set;}
    public DateTimeOffset CheckedAt {get;set;}
}
public sealed class MonitorState
{
    public string Revision {get;set;}="";
    public DateTimeOffset LastCheckUtc {get;set;}
    public string Error {get;set;}="";
    public Dictionary<string,WatchRelease> Releases {get;set;}=new(StringComparer.OrdinalIgnoreCase);
}
public sealed class NotificationReceipt
{
    public Dictionary<string,string> Tags {get;set;}=new(StringComparer.OrdinalIgnoreCase);
}
[JsonSerializable(typeof(MonitorProfile))]
[JsonSerializable(typeof(MonitorState))]
[JsonSerializable(typeof(NotificationReceipt))]
public partial class MonitorJson:JsonSerializerContext {}
public static class MonitorFiles
{
    public const string ServiceName="ReleasePilotMonitor";
    public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ReleasePilot","Monitor");
    public static bool ValidRepo(string repo)=>!string.IsNullOrWhiteSpace(repo)&&Regex.IsMatch(repo,@"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}\z") && !repo.EndsWith("/.") && !repo.EndsWith("/..");
    public static bool ValidSid(string sid)=>Regex.IsMatch(sid,@"\AS-1-5-21-(\d+-){2}\d+-\d+\z") || Regex.IsMatch(sid,@"\AS-1-12-1-(\d+-){3}\d+\z");
    public static void RejectLinks(string path)
    {
        for(var current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("服务目录不能使用符号链接或目录联接。");
    }
    public static string Read(string path)
    {
        RejectLinks(path);
        if(new FileInfo(path).Length>4*1024*1024)throw new IOException("服务配置或状态文件过大。");
        return File.ReadAllText(path);
    }
    public static void Write(string path,string content)
    {
        RejectLinks(path);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,content);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static bool NeedsNotification(WatchSubscription subscription,WatchRelease? old,string tag)
    {
        if(string.IsNullOrEmpty(tag)||tag==subscription.InstalledTag||(subscription.IsCurrent&&tag==subscription.SeenTag))return false;
        if(old?.Tag==tag)return old.Notify||(subscription.HasUpdate&&tag==subscription.SeenTag);
        var baseline=old?.Tag??subscription.SeenTag;
        return (!string.IsNullOrEmpty(baseline)&&tag!=baseline)||(subscription.HasUpdate&&tag==subscription.SeenTag);
    }
    public static List<KeyValuePair<string,WatchRelease>> Pending(MonitorProfile profile,MonitorState state,NotificationReceipt receipt)
        =>profile.Enabled?state.Releases.Where(pair=>pair.Value.Notify && profile.Subscriptions.Any(s=>s.Repo.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)&&s.InstalledTag!=pair.Value.Tag)
            && (!receipt.Tags.TryGetValue(pair.Key,out var tag)||tag!=pair.Value.Tag)).ToList():[];
}
