using System.Windows;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2 && e.Args[0]=="--apply-update")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{await UpdateService.CompleteSelfReplacementAsync(e.Args[1]);}
            catch(Exception ex){AppDialog.Show($"自更新未完成：{ex.Message}\n下载和备份保留在更新目录中。","ReleasePilot 更新助手");}
            Shutdown();return;
        }
        MainWindow=new MainWindow();MainWindow.Show();
    }
}
