using System.ComponentModel;
using System.Windows;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class BackgroundWindow:Window
{
    private readonly BackgroundSettings _settings;
    private bool _busy;
    public BackgroundWindow(BackgroundSettings settings)
    {
        InitializeComponent();_settings=settings;
        var profile=WindowsMonitorManager.ReadProfile();
        EnabledOption.IsChecked=profile?.Enabled??false;
        IntervalInput.Text=(profile?.IntervalMinutes??settings.IntervalMinutes).ToString();
        ProxyInput.Text=profile?.ProxyUrl??settings.ProxyUrl;
        RefreshStatus();Closing+=(_,e)=>{if(_busy)e.Cancel=true;};
    }
    private void RefreshStatus(){ServiceStatus.Text=WindowsMonitorManager.Status();CheckStatus.Text=WindowsMonitorManager.CheckSummary();}
    private bool ReadInputs()
    {
        if(!int.TryParse(IntervalInput.Text,out var interval)||interval<5||interval>1440){ErrorText.Text="请输入 5 到 1440 之间的整数。";return false;}
        var proxy=ProxyInput.Text.Trim();
        if(proxy.Length>0 && (!Uri.TryCreate(proxy,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(uri.UserInfo))){ErrorText.Text="请输入不含账号密码的 HTTP / HTTPS 代理地址。";return false;}
        _settings.Enabled=EnabledOption.IsChecked==true;_settings.StartAtLogin=false;_settings.IntervalMinutes=interval;_settings.ProxyUrl=proxy;return true;
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(!ReadInputs())return;
        if(!WindowsMonitorManager.Installed){ErrorText.Text="请先点击「安装 / 修复服务」，然后保存。";return;}
        try{WindowsMonitorManager.Export(new Database(),_settings);_settings.Save();ErrorText.Text="已保存。服务会自动读取设置，桌面界面可直接关闭。";RefreshStatus();}
        catch(Exception ex){ErrorText.Text="保存失败："+ex.Message;}
    }
    private async void Install_Click(object sender,RoutedEventArgs e)
    {
        if(!ReadInputs())return;
        _busy=true;ActionsPanel.IsEnabled=false;ErrorText.Text="正在安装服务，请完成 Windows 管理员授权…";
        try
        {
            await WindowsMonitorManager.InstallAsync();
            EnabledOption.IsChecked=true;_settings.Enabled=true;
            WindowsMonitorManager.Export(new Database(),_settings);_settings.Save();
            ErrorText.Text="服务已安装并启用。现在可以关闭界面。";
        }
        catch(Win32Exception ex) when(ex.NativeErrorCode==1223){ErrorText.Text="已取消管理员授权，服务未完成安装。";}
        catch(Exception ex){ErrorText.Text="安装未完成："+ex.Message;}
        finally{_busy=false;ActionsPanel.IsEnabled=true;RefreshStatus();}
    }
    private async void Uninstall_Click(object sender,RoutedEventArgs e)
    {
        if(AppDialog.Show(this,"将停止并卸载此电脑的 ReleasePilot 检测服务，其他用户也将停止后台检查。\n订阅记录和本地软件会保留。","卸载后台服务",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _busy=true;ActionsPanel.IsEnabled=false;
        try{await WindowsMonitorManager.UninstallAsync();_settings.Enabled=false;_settings.Save();EnabledOption.IsChecked=false;ErrorText.Text="服务已卸载，订阅记录已保留。";}
        catch(Exception ex){ErrorText.Text="卸载未完成："+ex.Message;}
        finally{_busy=false;ActionsPanel.IsEnabled=true;RefreshStatus();}
    }
    private void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
}
