using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ReleasePilot.Monitoring;
namespace ReleasePilot.Services;
public static class WindowsMonitorManager
{
    public static string UserSid=>WindowsIdentity.GetCurrent().User?.Value??throw new IOException("无法识别当前 Windows 用户。");
    public static string ProfileDirectory=>Path.Combine(MonitorFiles.Root,UserSid);
    public static string WorkerPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ReleasePilot Service","ReleasePilot.Monitor.exe");
    public static string TaskName=>"ReleasePilot Notifications-"+UserSid;
    public static string Status()
    {
        try{using var service=new ServiceController(MonitorFiles.ServiceName);return service.Status==ServiceControllerStatus.Running?"运行中（Windows 系统服务）":$"已安装 · {service.Status}";}
        catch(InvalidOperationException){return "尚未安装服务";}
        catch(System.ComponentModel.Win32Exception ex){return "无法读取服务状态："+ex.Message;}
    }
    public static bool Installed=>File.Exists(WorkerPath)&&Directory.Exists(ProfileDirectory);
    public static MonitorProfile? ReadProfile()
    {
        try{return JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(ProfileDirectory,"config.json")),MonitorJson.Default.MonitorProfile);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public static void Export(Database database,BackgroundSettings settings)
    {
        if(!Installed)return;
        var previous=ReadProfile();
        var profile=new MonitorProfile{Enabled=settings.Enabled,IntervalMinutes=settings.IntervalMinutes,ProxyUrl=settings.ProxyUrl,
            Subscriptions=database.GetAll().Select(s=>new WatchSubscription{Repo=s.Repo,SeenTag=s.LatestVersion,InstalledTag=s.ConfirmedTag,HasUpdate=s.Status is "发现更新" or "发现新发布",IsCurrent=s.Status is "已是最新" or "已是最新（已确认）" or "本地版本较新"}).ToList()};
        var content=JsonSerializer.Serialize(profile,MonitorJson.Default.MonitorProfile);
        if(previous is not null){previous.Revision="";if(content==JsonSerializer.Serialize(previous,MonitorJson.Default.MonitorProfile))return;}
        profile.Revision=Guid.NewGuid().ToString("N");
        MonitorFiles.Write(Path.Combine(ProfileDirectory,"config.json"),JsonSerializer.Serialize(profile,MonitorJson.Default.MonitorProfile));
    }
    public static void ImportLatest(Database database)
    {
        try
        {
            var state=JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(ProfileDirectory,"state.json")),MonitorJson.Default.MonitorState);
            if(state is null)return;
            foreach(var subscription in database.GetAll())
            {
                var pair=state.Releases.FirstOrDefault(x=>x.Key.Equals(subscription.Repo,StringComparison.OrdinalIgnoreCase));
                if(pair.Value is not {} release || release.Tag==subscription.LatestVersion)continue;
                if(DateTime.TryParse(subscription.CheckedAt,out var checkedAt)&&release.CheckedAt.LocalDateTime<checkedAt)continue;
                subscription.LatestVersion=release.Tag;subscription.ReleaseNotes=release.Notes;subscription.ReleaseUrl=release.Url;
                subscription.LocalVersion=subscription.UpdateKind=="archive"?subscription.ConfirmedTag:VersionHelper.ReadExeVersion(subscription.ExePath);
                subscription.Status=InstallationConfirmation.GetStatus(subscription);subscription.CheckedAt=release.CheckedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                database.Save(subscription);
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){}
    }
    public static string CheckSummary()
    {
        try
        {
            var state=JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(ProfileDirectory,"state.json")),MonitorJson.Default.MonitorState);
            if(state is null)return "尚未执行检查";
            return $"最近检查：{state.LastCheckUtc.LocalDateTime:yyyy-MM-dd HH:mm}\n{state.Error}";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return "等待服务首次检查（最多约 30 秒）；服务日志也可在 Windows 事件查看器中查看。";}
    }
    public static async Task InstallAsync()
    {
        await ManageAsync("Install");
        RegisterProtocol();
        await ManageNotificationsAsync(false);
    }
    public static async Task UninstallAsync()
    {
        var profile=ReadProfile();
        if(profile is not null)
        {
            profile.Enabled=false;profile.Revision=Guid.NewGuid().ToString("N");
            MonitorFiles.Write(Path.Combine(ProfileDirectory,"config.json"),JsonSerializer.Serialize(profile,MonitorJson.Default.MonitorProfile));
        }
        await ManageNotificationsAsync(true);
        await ManageAsync("Uninstall");
    }
    private static async Task ManageNotificationsAsync(bool remove)
    {
        using var source=typeof(WindowsMonitorManager).Assembly.GetManifestResourceStream("ReleasePilot.ManageNotifications.ps1")??throw new IOException("缺少通知管理脚本。");
        using var reader=new StreamReader(source);
        var script=reader.ReadToEnd();
        await RunPowerShellAsync("& {"+script+"} -UserSid '"+UserSid+"' -WorkerPath '"+Ps(WorkerPath)+"'"+(remove?" -Remove":""));
    }

    public static void RegisterProtocol()
    {
        using var protocol=Registry.CurrentUser.CreateSubKey(@"Software\Classes\releasepilot");
        protocol.SetValue("","URL:ReleasePilot");protocol.SetValue("URL Protocol","");
        using var command=protocol.CreateSubKey(@"shell\open\command");command.SetValue("",$"\"{Environment.ProcessPath}\" \"%1\"");
    }
    public static void RemoveLegacyStartup()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);
        if(key?.GetValue("ReleasePilot") is string value && value.Contains("--background",StringComparison.Ordinal))key.DeleteValue("ReleasePilot",false);
    }
    private static string Ps(string value)=>value.Replace("'","''");
    private static async Task RunPowerShellAsync(string script)
    {
        var start=new ProcessStartInfo(PowerShellPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';"+script)));
        using var process=Process.Start(start)??throw new IOException("无法运行 Windows 服务管理命令。");
        var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("通知任务设置失败："+await error);
    }
    private static string PowerShellPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
    private static async Task ManageAsync(string operation)
    {
        var temporary=Path.Combine(Path.GetTempPath(),"ReleasePilot-service-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        try
        {
            var assembly=typeof(WindowsMonitorManager).Assembly;
            async Task Extract(string resource,string filename)
            {
                await using var source=assembly.GetManifestResourceStream(resource)??throw new IOException("此构建未包含服务程序，请使用 Releases 中的正式 EXE。");
                await using var target=File.Create(Path.Combine(temporary,filename));await source.CopyToAsync(target);
            }
            await Extract("ReleasePilot.ManageService.ps1","ManageService.ps1");
            if(operation=="Install")await Extract("ReleasePilot.Monitor.exe","ReleasePilot.Monitor.exe");
            var start=new ProcessStartInfo(PowerShellPath){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(temporary,"ManageService.ps1"),"-Operation",operation,"-UserSid",UserSid,"-SourceWorker",Path.Combine(temporary,"ReleasePilot.Monitor.exe")})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("无法启动服务安装程序。");
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new IOException("服务操作未完成。请检查管理员授权以及 Windows 事件查看器；现有订阅不会被删除。");
        }
        finally{try{Directory.Delete(temporary,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
