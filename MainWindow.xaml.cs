using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ReleasePilot.Models;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class MainWindow : Window
{
    private readonly Database _db;
    private readonly GitHubClient _github=new();
    private readonly ObservableCollection<Subscription> _items=new();
    private Subscription? Selected=>SubscriptionsGrid.SelectedItem as Subscription;
    public MainWindow():this(new Database()) { }
    public MainWindow(Database database){_db=database;InitializeComponent();SubscriptionsGrid.ItemsSource=_items;Reload();}
    public Action? ConfigureBackground {get;set;}
    public bool IsChecking {get;private set;}
    public void RefreshSubscriptions(){if(!IsChecking && OwnedWindows.Count==0)Reload();}
    public void SetBackgroundStatus(string text)=>StatusText.Text=text;
    private void Background_Click(object sender,RoutedEventArgs e)=>ConfigureBackground?.Invoke();
    private void Reload()
    {
        var selectedRepo=Selected?.Repo;
        _items.Clear();foreach(var s in _db.GetAll())_items.Add(s);
        TotalCountText.Text=_items.Count.ToString();
        UpdateCountText.Text=_items.Count(s=>s.Status is "发现更新" or "发现新发布").ToString();
        LinkedCountText.Text=_items.Count(s=>!string.IsNullOrWhiteSpace(s.ExePath)).ToString();
        if(selectedRepo is not null)SubscriptionsGrid.SelectedItem=_items.FirstOrDefault(s=>s.Repo==selectedRepo);
        StatusText.Text=$"已订阅 {_items.Count} 个仓库 · 数据保存在本机";
    }
    private void RepoInput_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter)return;
        e.Handled=true;Add_Click(sender,e);
    }
    private async void Add_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var repo=GitHubClient.ParseRepo(RepoInput.Text);
            if(_items.Any(x=>x.Repo.Equals(repo,StringComparison.OrdinalIgnoreCase))){AppDialog.Show("该仓库已添加");return;}
            var s=new Subscription{Repo=repo};_db.Save(s);RepoInput.Clear();Reload();await CheckOne(s);Reload();
        }
        catch(Exception ex){AppDialog.Show(this,ex.Message,"添加失败",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private async Task CheckOne(Subscription s)
    {
        try
        {
            StatusText.Text=$"正在检查 {s.Repo} ...";
            var release=await _github.GetLatestAsync(s.Repo);
            s.LocalVersion=s.UpdateKind=="archive"?s.ConfirmedTag:VersionHelper.ReadExeVersion(s.ExePath);
            if(release is null){s.Status="无正式 Release";s.LatestVersion="";s.ReleaseNotes="";s.ReleaseUrl="";}
            else{s.LatestVersion=release.Tag;s.ReleaseNotes=release.Notes;s.ReleaseUrl=release.Url;s.Status=await Task.Run(()=>InstallationConfirmation.GetStatus(s));}
            s.CheckedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm");_db.Save(s);
        }
        catch(Exception ex){s.Status="检查失败";StatusText.Text=$"{s.Repo}: {ex.Message}";_db.Save(s);}
    }
    private async void CheckAll_Click(object sender,RoutedEventArgs e)
    {
        if(_items.Count==0||IsChecking)return;
        IsChecking=true;
        if(sender is Button b)b.IsEnabled=false;
        try{foreach(var s in _items.ToList()){await CheckOne(s);await Task.Delay(400);}Reload();StatusText.Text="检查完成";}
        finally{IsChecking=false;if(sender is Button button)button.IsEnabled=true;}
    }
    private async void LinkExe_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s){AppDialog.Show("请先选择软件");return;}
        var dialog=new OpenFileDialog{Title="选择需要关联的软件 EXE",Filter="可执行文件 (*.exe)|*.exe"};
        if(dialog.ShowDialog(this)!=true)return;
        s.UpdateKind="";s.UpdateTarget="";InstallationConfirmation.Clear(s);
        s.ExePath=dialog.FileName;s.LocalVersion=VersionHelper.ReadExeVersion(s.ExePath);
        s.Status=string.IsNullOrEmpty(s.LatestVersion)?"尚未检查":InstallationConfirmation.GetStatus(s);
        _db.Save(s);await CheckOne(s);Reload();StatusText.Text=$"已关联：{dialog.FileName}";
    }
    private async void LinkFolder_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        var dialog=new OpenFolderDialog {Title="选择软件或配置包的解压目录"};
        if(dialog.ShowDialog(this)!=true)return;
        if(s.UpdateKind!="archive" || s.UpdateTarget!=dialog.FolderName)InstallationConfirmation.Clear(s);
        s.UpdateKind="archive";s.UpdateTarget=dialog.FolderName;
        _db.Save(s);await CheckOne(s);Reload();
    }
    private async void Update_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        var button=(Button)sender;button.IsEnabled=false;
        try
        {
            var release=await _github.GetLatestAsync(s.Repo);
            if(release is null){AppDialog.Show(this,"该仓库暂无正式 Release。","无法更新");return;}
            var window=new UpdateWindow(s,release,()=>_db.Save(s)) {Owner=this};
            window.ShowDialog();Reload();
        }
        catch(Exception ex){AppDialog.Show(this,ex.Message,"无法获取更新",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{button.IsEnabled=true;}
    }
    private void OpenRelease_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        var url=string.IsNullOrWhiteSpace(s.ReleaseUrl)?$"https://github.com/{s.Repo}/releases":s.ReleaseUrl;
        Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    }
    private async void ConfirmInstalled_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        var button=(Button)sender;button.IsEnabled=false;
        try
        {
            await CheckOne(s);
            if(s.Status=="检查失败" || string.IsNullOrWhiteSpace(s.LatestVersion))
            {
                AppDialog.Show(this,"无法获取最新 Release，请成功检查后再确认。","暂时无法确认");return;
            }
            var local=s.UpdateKind=="archive"?$"解压目录：{s.UpdateTarget}":$"本地 EXE：{s.ExePath}\n文件版本：{(string.IsNullOrWhiteSpace(s.LocalVersion)?"未读取到":s.LocalVersion)}";
            var prompt=$"仓库：{s.Repo}\n{local}\n最新 Release：{s.LatestVersion}\n\n只有你确定本地已经是上述 Release 时，才点击「确认继续」。\n此操作只登记已经安装好的版本，不会下载、解压、安装或修复软件。\n尚未更新时，请先点击「安装 / 更新」。";
            if(AppDialog.Show(this,prompt,"标记为已安装此版本",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            await Task.Run(()=>InstallationConfirmation.Confirm(s));
            s.Status=InstallationConfirmation.GetStatus(s);_db.Save(s);
        }
        catch(Exception ex){AppDialog.Show(this,ex.Message,"确认失败",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{Reload();button.IsEnabled=true;}
    }
    private void ClearConfirmation_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        try
        {
            InstallationConfirmation.Clear(s);
            s.LocalVersion=s.UpdateKind=="archive"?s.ConfirmedTag:VersionHelper.ReadExeVersion(s.ExePath);
            s.Status=string.IsNullOrWhiteSpace(s.LatestVersion)?"尚未检查":InstallationConfirmation.GetStatus(s);
            _db.Save(s);Reload();
        }
        catch(Exception ex){AppDialog.Show(this,ex.Message,"清除安装标记失败",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        if(AppDialog.Show(this,$"确定删除 {s.Repo} 的订阅？\n仅移除订阅记录，不删除本地软件。","删除订阅",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _db.Delete(s.Repo);Reload();
    }
    private void Grid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        NotesBox.Text=Selected is {} s?$"仓库：{s.Repo}\n本地 EXE：{(string.IsNullOrEmpty(s.ExePath)?"未关联 · 点击「关联 EXE」选择软件主程序":s.ExePath)}\n解压目录：{s.UpdateTarget}\n本地版本：{s.LocalVersion} · 最新 Release：{s.LatestVersion}\n已记录的安装版本：{(string.IsNullOrEmpty(s.ConfirmedTag)?"无":s.ConfirmedTag)}\n\n{(string.IsNullOrWhiteSpace(s.ReleaseNotes)?"暂无发布说明，可点击「查看 Release」前往仓库查看。":s.ReleaseNotes)}":"还没有选择订阅。添加仓库后，在列表中选择软件即可查看详情。";
    }
}
