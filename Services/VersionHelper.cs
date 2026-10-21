using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleasePilot.Services;
public static class VersionHelper
{
    public static string ReadExeVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
        try
        {
            var info=FileVersionInfo.GetVersionInfo(path);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)?info.ProductVersion:info.FileVersion??"";
        }
        catch { return ""; }
    }
    public static string Compare(string local, string remote)
    {
        if (string.IsNullOrWhiteSpace(local)) return "未关联本地版本";
        if (!TryParse(local,out var lv,out var lp) || !TryParse(remote,out var rv,out var rp)) return "需要确认";
        var comparison=lv.CompareTo(rv);
        if(comparison==0)comparison=ComparePrerelease(lp,rp);
        return comparison<0?"发现更新":comparison>0?"本地版本较新":"已是最新";
    }

    private static bool TryParse(string input,out Version version,out string prerelease)
    {
        version=new Version(0,0,0,0);prerelease="";
        // .NET ProductVersion may contain +<commit>; build metadata is not version precedence.
        var match=Regex.Match(input.Trim(),@"^[vV]?(\d+(?:\.\d+){1,3})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
        if(!match.Success || !Version.TryParse(match.Groups[1].Value,out var parsed))return false;
        version=new Version(parsed.Major,parsed.Minor,Math.Max(parsed.Build,0),Math.Max(parsed.Revision,0));
        prerelease=match.Groups[2].Value;
        // Numeric prerelease identifiers follow SemVer and cannot contain leading zeroes.
        return !prerelease.Split('.').Any(part=>part.Length>1 && part[0]=='0' && part.All(char.IsAsciiDigit));
    }

    private static int ComparePrerelease(string local,string remote)
    {
        if(local==remote)return 0;
        if(local.Length==0)return 1;
        if(remote.Length==0)return -1;
        var left=local.Split('.');var right=remote.Split('.');
        for(var i=0;i<Math.Min(left.Length,right.Length);i++)
        {
            var ln=left[i].All(char.IsAsciiDigit);var rn=right[i].All(char.IsAsciiDigit);
            var comparison=ln && rn
                ? (left[i].Length==right[i].Length?string.CompareOrdinal(left[i],right[i]):left[i].Length.CompareTo(right[i].Length))
                : ln!=rn?(ln?-1:1):string.CompareOrdinal(left[i],right[i]);
            if(comparison!=0)return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
}
