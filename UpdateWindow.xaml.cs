using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ReleasePilot.Models;
using ReleasePilot.Services;

namespace ReleasePilot;
public partial class UpdateWindow : Window
{
    private readonly Subscription _subscription;
    private readonly ReleaseInfo _release;
    private readonly Action _save;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _applying;
    public bool Restarting {get;private set;}
    private string Kind=>(KindList.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"";
    public UpdateWindow(Subscription subscription,ReleaseInfo release,Action save)
    {
        InitializeComponent();_subscription=subscription;_release=release;_save=save;
        ReleaseLabel.Text=$"{subscription.Repo}  ·  {release.Tag}";
        PreserveInput.Text=subscription.PreservePaths;
        UnwrapArchive.IsChecked=subscription.UnwrapArchive;
        AssetList.ItemsSource=release.Assets.Where(a=>Path.GetExtension(a.Name).ToLowerInvariant() is ".exe" or ".msi" or ".zip" or ".7z").ToArray();
        AssetList.SelectedItem=AssetList.Items.Cast<ReleaseAsset>().FirstOrDefault(a=>a.Name==subscription.AssetName);
        if(AssetList.SelectedItem is null && AssetList.Items.Count>0)AssetList.SelectedIndex=0;
        if(!string.IsNullOrEmpty(subscription.UpdateKind))
            KindList.SelectedItem=KindList.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>i.Tag.ToString()==subscription.UpdateKind);
        UpdateTarget();
        if(AssetList.Items.Count==0){ProgressText.Text="此 Release 没有支持的附件，请使用「查看 Release」。";ApplyButton.IsEnabled=false;}
        Closing+=OnClosing;
    }
    private void Asset_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(AssetList.SelectedItem is not ReleaseAsset asset || KindList is null)return;
        var ext=Path.GetExtension(asset.Name).ToLowerInvariant();
        KindList.SelectedIndex=ext is ".zip" or ".7z"?1:ext==".msi" || asset.Name.Contains("setup",StringComparison.OrdinalIgnoreCase) || asset.Name.Contains("install",StringComparison.OrdinalIgnoreCase)?0:2;
    }
    private void Kind_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(TargetInput is null || _subscription is null)return;
        UpdateTarget();
    }
    private void UpdateTarget()
    {
        var archive=Kind=="archive";var installer=Kind=="installer";
        TargetLabel.Text=archive?"3. 解压目标目录（选择软件目录，不是压缩包）":installer?"3. 安装路径由安装向导选择":"3. 将被替换的本地 EXE";
        TargetInput.IsEnabled=BrowseButton.IsEnabled=!installer;
        TargetInput.Text=archive?(_subscription.UpdateKind=="archive"?_subscription.UpdateTarget:Path.GetDirectoryName(_subscription.ExePath)??""):_subscription.ExePath;
        ArchiveOptions.Visibility=archive?Visibility.Visible:Visibility.Collapsed;
        Explanation.Text=archive?"解压后合并到目标目录：同名文件先备份再覆盖，未包含的本地文件保留。配置包按安装记录追踪版本。":installer?"会启动安装向导；下载完成不代表安装成功。安装后需要你确认，再记录版本。":"请关闭目标软件。先备份旧 EXE，再替换；更新 ReleasePilot 自身时会退出并自动重启。";
    }
    private void Browse_Click(object sender,RoutedEventArgs e)
    {
        if(Kind=="archive")
        {
            var dialog=new OpenFolderDialog {Title="选择软件或配置包的解压目录"};if(dialog.ShowDialog(this)==true)TargetInput.Text=dialog.FolderName;
        }
        else
        {
            var dialog=new OpenFileDialog {Title="选择要替换的本地主程序",Filter="EXE 文件 (*.exe)|*.exe"};if(dialog.ShowDialog(this)==true)TargetInput.Text=dialog.FileName;
        }
    }
    private async void Apply_Click(object sender,RoutedEventArgs e)
    {
        if(_busy || AssetList.SelectedItem is not ReleaseAsset asset)return;
        var kind=Kind;var target=TargetInput.Text.Trim();var preserve=PreserveInput.Text;var unwrap=UnwrapArchive.IsChecked==true;
        var extension=Path.GetExtension(asset.Name).ToLowerInvariant();
        if((kind=="archive" && extension is not (".zip" or ".7z")) || (kind=="installer" && extension is not (".exe" or ".msi")) || (kind=="replace" && extension!=".exe"))
        {MessageBox.Show(this,"所选附件与更新方式不匹配。","请检查更新方式");return;}
        try
        {
            if(kind!="installer")
            {
                if(!Path.IsPathFullyQualified(target))throw new IOException("请选择完整的本地目标路径。");
                UpdateService.RejectLinks(target);
                if(kind=="replace" && !File.Exists(target))throw new IOException("请选择已存在的本地 EXE。");
                if(kind=="replace" && !Path.GetExtension(target).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("单文件更新的目标必须是 EXE。");
                if(kind=="archive" && !Directory.Exists(target))throw new IOException("请先选择或创建解压目标目录。");
                UpdateService.PreservedPaths(preserve);
            }
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"请检查目标位置");return;}
        var action=kind=="installer"?"启动安装程序，由你完成安装向导":kind=="archive"?$"解压并覆盖到：{target}\n保留路径：{(string.IsNullOrWhiteSpace(preserve)?"无（同名文件将备份后覆盖）":preserve)}\n去掉顶层目录：{(unwrap?"是":"否")}":$"备份并替换：{target}";
        if(MessageBox.Show(this,$"Release：{_release.Tag}\n附件：{asset.Name}\n\n{action}\n\n确认继续？","确认更新",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _busy=true;OptionsPanel.IsEnabled=false;ApplyButton.IsEnabled=false;DownloadProgress.Visibility=Visibility.Visible;CancelButton.Content="取消下载";
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(30));_cancellation=cancellation;
        var workspace="";
        try
        {
            workspace=UpdateService.CreateWorkspace();
            var progress=new Progress<string>(text=>ProgressText.Text=text);
            var downloaded=await UpdateService.DownloadAsync(asset,workspace,progress,cancellation.Token);
            string? stage=null;
            if(kind=="archive")
            {
                CancelButton.Content="取消解压";
                ExtractionProgress? snapshot=null;
                var elapsed=Stopwatch.StartNew();var lastChange=TimeSpan.Zero;
                var extraction=new Progress<ExtractionProgress>(value=>{snapshot=value;lastChange=elapsed.Elapsed;});
                void RenderExtraction()
                {
                    var percent=snapshot?.Percentage;
                    DownloadProgress.IsIndeterminate=percent is null;
                    if(percent is not null)DownloadProgress.Value=percent.Value;
                    ProgressText.Text=percent is null?"正在准备解压…":$"正在解压到临时目录 · {percent:F1}%";
                    CurrentFileText.Text=string.IsNullOrEmpty(snapshot?.CurrentFile)?"正在读取压缩包目录和准备解压器…":$"当前文件：{snapshot.CurrentFile}";
                    var duration=$"{(int)elapsed.Elapsed.TotalMinutes:D2}:{elapsed.Elapsed.Seconds:D2}";
                    ProgressDetails.Text=snapshot is {TotalFiles:>0}
                        ?$"已完成 {snapshot.CompletedFiles:N0} / {snapshot.TotalFiles:N0} 个文件 · {snapshot.ExtractedBytes/1048576d:F1} / {snapshot.TotalBytes/1048576d:F1} MB · 已用时 {duration}"
                        :$"已用时 {duration} · 正在准备，请稍候";
                    ProgressDetails.Text+=elapsed.Elapsed-lastChange>TimeSpan.FromSeconds(10)
                        ?"\n暂未收到新的解压数据。7z 大文件可能较慢，可继续等待或取消。"
                        :"\n7z 固实压缩包可能需要较长时间，解压期间可取消。";
                    if(cancellation.IsCancellationRequested)ProgressText.Text="正在取消解压，等待当前读取结束…";
                }
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(500)};
                EventHandler tick=(_,_)=>RenderExtraction();timer.Tick+=tick;
                RenderExtraction();timer.Start();
                try{stage=await Task.Run(()=>UpdateService.Extract(downloaded,workspace,cancellation.Token,extraction));}
                finally{timer.Stop();timer.Tick-=tick;}
                DownloadProgress.IsIndeterminate=true;CurrentFileText.Text="";ProgressDetails.Text="";
            }
            cancellation.Token.ThrowIfCancellationRequested();_applying=true;CancelButton.IsEnabled=false;
            if(kind=="installer")
            {
                ProgressText.Text="正在运行安装向导，请在安装完成后返回。";
                using var process=Process.Start(UpdateService.InstallerStartInfo(downloaded));
                if(process is not null)
                {
                    await process.WaitForExitAsync();
                    if(process.ExitCode is not (0 or 3010 or 1641))throw new IOException($"安装程序退出码为 {process.ExitCode}，未记录为更新成功。");
                }
                if(MessageBox.Show(this,"安装程序已退出。请确认安装向导已成功完成，且没有取消安装。\n\n是否确认本地已安装此 Release？（如安装器启动了另一个窗口，请先完成安装再确认。）","确认安装结果",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)
                {ProgressText.Text="安装结果未确认。可稍后关联 EXE，使用「确认已安装当前版」。";return;}
                var exe=new OpenFileDialog {Title="确认安装后的软件主程序",Filter="EXE 文件 (*.exe)|*.exe",FileName=_subscription.ExePath};
                if(exe.ShowDialog(this)!=true){ProgressText.Text="未选择安装后的主程序，未记录安装版本。";return;}
                _subscription.ExePath=exe.FileName;
            }
            else if(kind=="archive")
            {
                ProgressText.Text="正在备份并应用更新，请勿关闭窗口…";
                await Task.Run(()=>UpdateService.ApplyDirectory(stage!,target,Path.Combine(workspace,"backup"),preserve,unwrap));
            }
            else if(UpdateService.IsSelf(target))
            {
                UpdateService.StartSelfReplacement(downloaded,target,workspace,_subscription.Repo,_release);
                Restarting=true;Application.Current.Shutdown();return;
            }
            else
            {
                ProgressText.Text="正在备份并替换 EXE…";
                await Task.Run(()=>UpdateService.ReplaceFile(downloaded,target,Path.Combine(workspace,"backup")));
                _subscription.ExePath=target;
            }
            _subscription.UpdateKind=kind;_subscription.UpdateTarget=target;_subscription.AssetName=asset.Name;_subscription.PreservePaths=preserve;
            _subscription.UnwrapArchive=unwrap;
            _subscription.LatestVersion=_release.Tag;_subscription.ReleaseUrl=_release.Url;_subscription.ReleaseNotes=_release.Notes;
            _subscription.LocalVersion=VersionHelper.ReadExeVersion(_subscription.ExePath);
            await Task.Run(()=>InstallationConfirmation.Confirm(_subscription));
            _subscription.Status=InstallationConfirmation.GetStatus(_subscription);_save();
            ProgressText.Text=$"更新完成：{_release.Tag}\n旧文件备份与下载文件保留在：{workspace}";
            ApplyButton.Content="更新完成";
        }
        catch(OperationCanceledException){ProgressText.Text="更新已取消，尚未修改目标文件。";ApplyButton.IsEnabled=true;}
        catch(Exception ex){ProgressText.Text=$"更新未完成：{ex.Message}\n下载及备份目录：{workspace}";ApplyButton.IsEnabled=true;}
        finally
        {
            _busy=false;_applying=false;_cancellation=null;OptionsPanel.IsEnabled=true;CancelButton.IsEnabled=true;CancelButton.Content="关闭";DownloadProgress.Visibility=Visibility.Collapsed;
            CurrentFileText.Text="";ProgressDetails.Text="";DownloadProgress.IsIndeterminate=true;
        }
    }
    private void Cancel_Click(object sender,RoutedEventArgs e)
    {
        if(_busy){_cancellation?.Cancel();CancelButton.Content="正在取消…";CancelButton.IsEnabled=false;}
        else Close();
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!_busy || Restarting)return;
        e.Cancel=true;if(!_applying)_cancellation?.Cancel();
    }
}
