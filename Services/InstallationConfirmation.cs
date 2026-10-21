using System.IO;
using System.Security.Cryptography;
using ReleasePilot.Models;

namespace ReleasePilot.Services;

public static class InstallationConfirmation
{
    public static void Confirm(Subscription subscription)
    {
        if(string.IsNullOrWhiteSpace(subscription.LatestVersion))throw new InvalidOperationException("请先成功检查最新 Release。");
        if(subscription.UpdateKind=="archive")
        {
            if(!Directory.Exists(subscription.UpdateTarget))throw new IOException("请先关联存在的解压目录。");
            subscription.ConfirmedTag=subscription.LatestVersion;subscription.ConfirmedSha256="";return;
        }
        // Bind the user's decision to the actual bytes, not just a path or version string.
        var hash=Fingerprint(subscription.ExePath);
        subscription.ConfirmedTag=subscription.LatestVersion;
        subscription.ConfirmedSha256=hash;
    }

    public static void Clear(Subscription subscription)
    {
        subscription.ConfirmedTag="";subscription.ConfirmedSha256="";
    }

    public static string GetStatus(Subscription subscription)
    {
        if(subscription.UpdateKind=="archive")
        {
            if(!Directory.Exists(subscription.UpdateTarget))return "目标目录不存在";
            if(string.IsNullOrWhiteSpace(subscription.ConfirmedTag))return "未记录安装版本";
            return CompareRecorded(subscription.ConfirmedTag,subscription.LatestVersion);
        }
        if(!string.IsNullOrWhiteSpace(subscription.ConfirmedTag) && !string.IsNullOrWhiteSpace(subscription.ConfirmedSha256))
        {
            try
            {
                if(Fingerprint(subscription.ExePath)==subscription.ConfirmedSha256)
                {
                    return CompareRecorded(subscription.ConfirmedTag,subscription.LatestVersion);
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            return "本地文件已变化，请确认";
        }
        return VersionHelper.Compare(subscription.LocalVersion,subscription.LatestVersion);
    }

    private static string CompareRecorded(string installed,string latest)
    {
        if(installed==latest)return "已是最新（已确认）";
        var status=VersionHelper.Compare(installed,latest);
        return status=="需要确认"?"发现新发布":status;
    }

    private static string Fingerprint(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || !File.Exists(path))throw new IOException("请先关联存在的本地 EXE，再确认安装版本。");
        using var stream=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
