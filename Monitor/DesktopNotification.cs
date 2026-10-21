using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ReleasePilot.Monitoring;
namespace ReleasePilot.Worker;
public static class DesktopNotification
{
    public static async Task<int> RunAsync(string sid)
    {
        if(!MonitorFiles.ValidSid(sid)||WindowsIdentity.GetCurrent().User?.Value!=sid)return 1;
        var directory=Path.Combine(MonitorFiles.Root,sid);
        try
        {
            using var mutex=new Mutex(true,"Local\\ReleasePilotNotification-"+sid,out var first);
            if(!first)return 0;
            var config=JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(directory,"config.json")),MonitorJson.Default.MonitorProfile)??new();
            var state=JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(directory,"state.json")),MonitorJson.Default.MonitorState)??new();
            var receiptPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReleasePilot","service-notifications.json");
            NotificationReceipt receipt;
            try{receipt=JsonSerializer.Deserialize(MonitorFiles.Read(receiptPath),MonitorJson.Default.NotificationReceipt)??new();}
            catch(Exception ex) when(ex is IOException or JsonException){receipt=new();}
            receipt.Tags=new(receipt.Tags,StringComparer.OrdinalIgnoreCase);
            var pending=MonitorFiles.Pending(config,state,receipt);if(pending.Count==0)return 0;
            // This process exits after submitting a toast. No tray or desktop process stays resident.
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\ReleasePilot")){key.SetValue("DisplayName","ReleasePilot");}
            var text=System.Security.SecurityElement.Escape(string.Join("\n",pending.Take(4).Select(x=>$"{x.Key} → {x.Value.Tag}")))??"";
            var xml=$"<toast activationType='protocol' launch='releasepilot://updates'><visual><binding template='ToastGeneric'><text>发现 {pending.Count} 个软件更新</text><text>{text}</text></binding></visual></toast>";
            var encodedXml=Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
            var script="$ErrorActionPreference='Stop';[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] > $null;[Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime] > $null;[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime] > $null;$xml=New-Object Windows.Data.Xml.Dom.XmlDocument;$xml.LoadXml([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encodedXml+"')));$notifier=[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('ReleasePilot');if($notifier.Setting -ne [Windows.UI.Notifications.NotificationSetting]::Enabled){exit 2};$notifier.Show([Windows.UI.Notifications.ToastNotification]::new($xml));";
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var process=Process.Start(start)??throw new IOException("无法启动通知助手。");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);return 1;}
            if(process.ExitCode!=0)return process.ExitCode;
            foreach(var update in pending)receipt.Tags[update.Key]=update.Value.Tag;
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            MonitorFiles.Write(receiptPath,JsonSerializer.Serialize(receipt,MonitorJson.Default.NotificationReceipt));
            return 0;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return 1;}
    }
}
