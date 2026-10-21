using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReleasePilot.Models;
using ReleasePilot.Services;
namespace ReleasePilot;
public partial class MainWindow : Window
{
    private readonly Database _db=new();
    private readonly GitHubClient _github=new();
    private readonly ObservableCollection<Subscription> _items=new();
    private Subscription? Selected=>SubscriptionsGrid.SelectedItem as Subscription;
    public MainWindow(){InitializeComponent();SubscriptionsGrid.ItemsSource=_items;Reload();}
    private void Reload()
    {
        _items.Clear();foreach(var s in _db.GetAll())_items.Add(s);
        StatusText.Text=$"已订阅 {_items.Count} 个仓库 · 数据保存在本机";
    }
    private async void Add_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var repo=GitHubClient.ParseRepo(RepoInput.Text);
            if(_items.Any(x=>x.Repo.Equals(repo,StringComparison.OrdinalIgnoreCase))){MessageBox.Show("该仓库已添加");return;}
            var s=new Subscription{Repo=repo};_db.Save(s);RepoInput.Clear();Reload();await CheckOne(s);Reload();
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"添加失败");}
    }
    private async Task CheckOne(Subscription s)
    {
        try
        {
            StatusText.Text=$"正在检查 {s.Repo} ...";
            var release=await _github.GetLatestAsync(s.Repo);
            s.LocalVersion=VersionHelper.ReadExeVersion(s.ExePath);
            if(release is null){s.Status="无正式 Release";s.LatestVersion="";s.ReleaseNotes="";s.ReleaseUrl="";}
            else{s.LatestVersion=release.Tag;s.ReleaseNotes=release.Notes;s.ReleaseUrl=release.Url;s.Status=VersionHelper.Compare(s.LocalVersion,s.LatestVersion);}
            s.CheckedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm");_db.Save(s);
        }
        catch(Exception ex){s.Status="检查失败";StatusText.Text=$"{s.Repo}: {ex.Message}";_db.Save(s);}
    }
    private async void CheckAll_Click(object sender,RoutedEventArgs e)
    {
        if(_items.Count==0)return;
        if(sender is Button b)b.IsEnabled=false;
        try{foreach(var s in _items.ToList()){await CheckOne(s);await Task.Delay(400);}Reload();StatusText.Text="检查完成";}
        finally{if(sender is Button button)button.IsEnabled=true;}
    }
    private async void LinkExe_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s){MessageBox.Show("请先选择软件");return;}
        var dialog=new OpenFileDialog{Title="选择需要关联的软件 EXE",Filter="可执行文件 (*.exe)|*.exe"};
        if(dialog.ShowDialog()!=true)return;
        s.ExePath=dialog.FileName;s.LocalVersion=VersionHelper.ReadExeVersion(s.ExePath);
        s.Status=string.IsNullOrEmpty(s.LatestVersion)?"尚未检查":VersionHelper.Compare(s.LocalVersion,s.LatestVersion);
        _db.Save(s);await CheckOne(s);Reload();StatusText.Text=$"已关联：{dialog.FileName}";
    }
    private void OpenRelease_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        var url=string.IsNullOrWhiteSpace(s.ReleaseUrl)?$"https://github.com/{s.Repo}/releases":s.ReleaseUrl;
        Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} s)return;
        if(MessageBox.Show($"确定删除 {s.Repo} 的订阅？","确认",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        _db.Delete(s.Repo);Reload();NotesBox.Text="";
    }
    private void Grid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        NotesBox.Text=Selected is {} s?$"仓库：{s.Repo}\n本地 EXE：{(string.IsNullOrEmpty(s.ExePath)?"未关联":s.ExePath)}\n\n{s.ReleaseNotes}":"选择一项订阅查看更新日志";
    }
}
