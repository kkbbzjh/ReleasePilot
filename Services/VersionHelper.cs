using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleasePilot.Services;
public static class VersionHelper
{
    public static string ReadExeVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
        try { return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? FileVersionInfo.GetVersionInfo(path).FileVersion ?? ""; }
        catch { return ""; }
    }
    public static string Compare(string local, string remote)
    {
        if (string.IsNullOrWhiteSpace(local)) return "未关联本地版本";
        var l=Regex.Match(local.Trim(), @"^v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);
        var r=Regex.Match(remote.Trim(), @"^v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);
        if (!l.Success || !r.Success || !Version.TryParse(l.Groups[1].Value,out var lv) || !Version.TryParse(r.Groups[1].Value,out var rv)) return "需要确认";
        return lv.CompareTo(rv)<0?"发现更新":lv.CompareTo(rv)>0?"本地版本较新":"已是最新";
    }
}
