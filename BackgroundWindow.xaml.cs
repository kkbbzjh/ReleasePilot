using System.Diagnostics;
using System.IO;
using System.Windows;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class BackgroundWindow:Window
{
    private readonly BackgroundSettings _settings;
    public BackgroundWindow(BackgroundSettings settings)
    {
        InitializeComponent();_settings=settings;
        EnabledOption.IsChecked=settings.Enabled;LoginOption.IsChecked=settings.StartAtLogin;
        IntervalInput.Text=settings.IntervalMinutes.ToString();
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(IntervalInput.Text,out var interval)||interval<5||interval>1440){ErrorText.Text="请输入 5 到 1440 之间的整数。";return;}
        if(LoginOption.IsChecked==true && EnabledOption.IsChecked!=true){ErrorText.Text="登录后后台启动需要先开启后台检测。";return;}
        try
        {
            using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if(LoginOption.IsChecked==true)key.SetValue("ReleasePilot",$"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("ReleasePilot",false);
            _settings.Enabled=EnabledOption.IsChecked==true;_settings.StartAtLogin=LoginOption.IsChecked==true;_settings.IntervalMinutes=interval;_settings.Save();
            DialogResult=true;
        }
        catch(Exception ex){ErrorText.Text="保存失败："+ex.Message;}
    }
    private void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
    private void OpenUpdates_Click(object sender,RoutedEventArgs e)
    {
        try{var path=Path.Combine(BackgroundSettings.DataDirectory,"Updates");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        catch(Exception ex){ErrorText.Text=ex.Message;}
    }
}
