using System.Windows;
using System.IO.Pipes;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class App : Application
{
    private Mutex? _instance;
    private BackgroundHost? _background;
    public static string InstanceName=>"ReleasePilot-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserName+BackgroundSettings.DataDirectory)))[..24];
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2 && e.Args[0]=="--apply-update")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{await UpdateService.CompleteSelfReplacementAsync(e.Args[1]);}
            catch(Exception ex){AppDialog.Show($"自更新未完成：{ex.Message}\n可在后台设置中打开临时更新目录。","ReleasePilot 更新助手");}
            Shutdown();return;
        }
        _instance=new Mutex(true,InstanceName,out var first);
        if(!first)
        {
            if(!e.Args.Contains("--background"))
            {
                try{using var pipe=new NamedPipeClientStream(".",InstanceName,PipeDirection.Out,PipeOptions.Asynchronous);using var timeout=new CancellationTokenSource(3000);await pipe.ConnectAsync(timeout.Token);await pipe.WriteAsync(new byte[]{1},timeout.Token);}
                catch(Exception ex) when(ex is System.IO.IOException or OperationCanceledException or TimeoutException){}
            }
            Shutdown();return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--cleanup-update" && int.TryParse(e.Args[2],out var helperId))
            _=CleanupHelperAsync(e.Args[1],helperId);
        var window=new MainWindow();MainWindow=window;
        _background=new BackgroundHost(window);window.ConfigureBackground=_background.Configure;
        if(!e.Args.Contains("--background")||!_background.Enabled)window.Show();
    }
    private static async Task CleanupHelperAsync(string path,int helperId)
    {
        try
        {
            var full=System.IO.Path.GetFullPath(path);
            var root=System.IO.Path.Combine(BackgroundSettings.DataDirectory,"Updates");
            if(!string.Equals(System.IO.Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(System.IO.Path.GetFileName(full),"N",out _))return;
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try{using var helper=System.Diagnostics.Process.GetProcessById(helperId);await helper.WaitForExitAsync(timeout.Token);}catch(ArgumentException){}
            System.IO.Directory.Delete(full,true);
        }
        catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or OperationCanceledException){}
    }
    protected override void OnExit(ExitEventArgs e){_background?.Dispose();_instance?.Dispose();base.OnExit(e);}
}
