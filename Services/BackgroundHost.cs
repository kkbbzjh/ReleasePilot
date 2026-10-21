using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace ReleasePilot.Services;
public sealed class BackgroundHost:IDisposable
{
    private readonly MainWindow _window;
    private readonly BackgroundSettings _settings=BackgroundSettings.Load();
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _timer=new();
    private readonly CancellationTokenSource _stop=new();
    private bool _checking,_exiting,_hintShown;
    public BackgroundHost(MainWindow window)
    {
        _window=window;
        using var icon=Application.GetResourceStream(new Uri("/ReleasePilot;component/Assets/ReleasePilot.ico",UriKind.Relative)).Stream;
        using var sourceIcon=new System.Drawing.Icon(icon);
        _tray=new Forms.NotifyIcon{Icon=(System.Drawing.Icon)sourceIcon.Clone(),Text="ReleasePilot · 后台更新检测",Visible=_settings.Enabled};
        _tray.DoubleClick+=(_,_)=>Show();_tray.BalloonTipClicked+=(_,_)=>Show();
        _tray.MouseUp+=(_,e)=>{if(e.Button!=Forms.MouseButtons.Right)return;Application.Current.Dispatcher.Invoke(()=>
        {
            var menu=new ContextMenu();
            void Add(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Add("打开 ReleasePilot",Show);Add("立即检查更新",()=>_=CheckAsync());Add("后台与通知",Configure);Add("退出 ReleasePilot",()=>{_exiting=true;Application.Current.Shutdown();});
            menu.IsOpen=true;
        });};
        _window.Closing+=(_,e)=>{if(_settings.Enabled&&!_exiting){e.Cancel=true;_window.Hide();if(!_hintShown){_hintShown=true;_tray.ShowBalloonTip(5000,"ReleasePilot 仍在后台运行","定时检查新版本。右键托盘图标可打开窗口或退出。",Forms.ToolTipIcon.Info);}}};
        _timer.Tick+=async(_,_)=>await CheckAsync();ApplySettings();
        _=ListenAsync();
    }
    public bool Enabled=>_settings.Enabled;
    public void Show(){_window.Show();if(_window.WindowState==WindowState.Minimized)_window.WindowState=WindowState.Normal;_window.Activate();}
    public void Configure()
    {
        Show();
        var dialog=new BackgroundWindow(_settings){Owner=_window};
        if(dialog.ShowDialog()==true)ApplySettings();
    }
    private void ApplySettings()
    {
        _timer.Stop();_tray.Visible=_settings.Enabled;
        if(_settings.Enabled){_timer.Interval=TimeSpan.FromMinutes(_settings.IntervalMinutes);_timer.Start();_=CheckAsync();}
    }
    private async Task CheckAsync()
    {
        if(_checking||!_settings.Enabled||_window.OwnedWindows.Count>0||_window.IsChecking)return;
        _checking=true;
        try
        {
            var db=new Database();var github=new GitHubClient();var updates=new List<string>();
            foreach(var old in db.GetAll())
            {
                if(_stop.IsCancellationRequested||!_settings.Enabled||_window.OwnedWindows.Count>0||_window.IsChecking)break;
                try
                {
                    var release=await github.GetLatestAsync(old.Repo);
                    if(_stop.IsCancellationRequested||!_settings.Enabled||_window.OwnedWindows.Count>0||_window.IsChecking)break;
                    var s=db.GetAll().FirstOrDefault(x=>x.Repo==old.Repo);if(s is null)continue;
                    var snapshot=System.Text.Json.JsonSerializer.Serialize(s);
                    var previous=s.LatestVersion;
                    s.LocalVersion=s.UpdateKind=="archive"?s.ConfirmedTag:VersionHelper.ReadExeVersion(s.ExePath);
                    s.LatestVersion=release?.Tag??"";s.ReleaseNotes=release?.Notes??"";s.ReleaseUrl=release?.Url??"";
                    s.Status=release is null?"无正式 Release":await Task.Run(()=>InstallationConfirmation.GetStatus(s));
                    // Recheck after hashing: never overwrite changes from an update dialog or a manual check.
                    if(_window.OwnedWindows.Count>0||_window.IsChecking)break;
                    if(System.Text.Json.JsonSerializer.Serialize(db.GetAll().FirstOrDefault(x=>x.Repo==s.Repo))!=snapshot)continue;
                    s.CheckedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm");db.Save(s);
                    if(_settings.ShouldNotify(s.Repo,s.LatestVersion,s.Status,previous))
                    {updates.Add($"{s.Repo} → {s.LatestVersion}");_settings.NotifiedTags[s.Repo]=s.LatestVersion;}
                    await Task.Delay(1000,_stop.Token);
                }
                catch(OperationCanceledException){break;}
                catch(Exception ex){_window.SetBackgroundStatus($"后台检查 {old.Repo} 失败：{ex.Message}；下次自动重试");}
            }
            if(updates.Count>0 && _settings.Enabled && !_stop.IsCancellationRequested)
            {
                _tray.ShowBalloonTip(10000,$"发现 {updates.Count} 个新版本",string.Join("\n",updates).Substring(0,Math.Min(240,string.Join("\n",updates).Length))+"\n点击打开并选择安装 / 更新。",Forms.ToolTipIcon.Info);
                _settings.Save();
            }
            _window.RefreshSubscriptions();
        }
        catch(Exception ex){_window.SetBackgroundStatus("后台检测失败："+ex.Message);}
        finally{_checking=false;}
    }
    private async Task ListenAsync()
    {
        while(!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(App.InstanceName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                var data=new byte[1];if(await pipe.ReadAsync(data,_stop.Token)>0 && data[0]==1)Show();
            }
            catch(OperationCanceledException){break;}catch(IOException){await Task.Delay(500,_stop.Token);}
        }
    }
    public void Dispose(){_exiting=true;_timer.Stop();_stop.Cancel();_tray.Dispose();}
}
