using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReleasePilot;
using ReleasePilot.Models;
using ReleasePilot.Services;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try{return RunApplication();}catch(Exception ex){ReportFailure(ex);return 1;}
    }
    private static int RunApplication()
    {
        RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var exit=1;
        application.DispatcherUnhandledException+=(_,e)=>{ReportFailure(e.Exception);e.Handled=true;application.Shutdown(1);};
        application.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/ReleasePilot;component/Themes/Theme.xaml")});
        application.Dispatcher.BeginInvoke(new Action(async ()=>
        {
            var folder=Path.Combine(Path.GetTempPath(),"ReleasePilot-ui-tests-"+Guid.NewGuid());
            Directory.CreateDirectory(folder);
            try
            {
                await Run(folder);exit=0;
            }
            catch(Exception ex){ReportFailure(ex);}
            finally
            {
                foreach(var window in application.Windows.Cast<Window>().ToArray())window.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(folder,true);application.Shutdown(exit);
            }
        }));
        application.Run();return exit;
    }
    private static async Task Run(string folder)
    {
        var checks=0;
        void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;}
        var subscription=new Subscription {Repo="owner/example",LocalVersion="1.2.0.0",LatestVersion="v1.2.0",UpdateKind="archive",UpdateTarget=folder,PreservePaths="config;settings.ini"};
        var db=new Database(folder);db.Save(subscription);
        var main=new MainWindow(db);main.Show();await Pump();
        ((DataGrid)main.FindName("SubscriptionsGrid")).SelectedIndex=0;await Pump();Capture(main,"subscriptions");
        using(var host=new DesktopLifetime(main))
        {
            host.Show();Check(main.IsVisible,"desktop lifetime restores the main window");
        }
        var settings=new BackgroundWindow(new BackgroundSettings()){Owner=main};settings.Show();await Pump();
        Check(((CheckBox)settings.FindName("EnabledOption")).IsChecked==false,"service monitoring is opt in until installed");
        Check(((TextBox)settings.FindName("IntervalInput")).Text=="60","background interval defaults to one hour");
        Check(FindVisual<Button>(main).Any(b=>Equals(b.Content,"打开临时目录")),"temporary directory is accessible directly from main window");
        Check(!FindVisual<Button>(settings).Any(b=>b.Content?.ToString()?.Contains("临时")==true),"service settings do not contain unrelated file actions");
        Capture(settings,"service-settings");settings.Close();
        var addButton=FindVisual<Button>(main).First(button=>Equals(button.Content,"＋ 添加订阅"));
        Check(ReferenceEquals(addButton.Style,main.FindResource("PrimaryButton")),"main primary action uses the shared theme");
        var release=new ReleaseInfo("v1.2.0","https://github.com/owner/example/releases/tag/v1.2.0","Example notes",[
            new ReleaseAsset("example-v1.2.0-windows-x64.7z","https://github.com/owner/example/releases/download/v1.2.0/example.7z",1024,null),
            new ReleaseAsset("example-v1.2.0-setup.exe","https://github.com/owner/example/releases/download/v1.2.0/example.exe",1024,null),
            new ReleaseAsset("example-linux-x64.zip","https://github.com/owner/example/releases/download/v1.2.0/linux.zip",1024,null),
            new ReleaseAsset("example-windows-arm64.zip","https://github.com/owner/example/releases/download/v1.2.0/arm64.zip",1024,null),
            new ReleaseAsset("example-macos-arm64.dmg","https://github.com/owner/example/releases/download/v1.2.0/mac.dmg",1024,null)]);
        var update=new UpdateWindow(subscription,release,()=>{}) {Owner=main};update.Show();await Pump();
        var apply=(Button)update.FindName("ApplyButton");
        Check(ReferenceEquals(apply.Style,addButton.Style),"update and main primary actions share one style");
        var assets=(ComboBox)update.FindName("AssetList");
        Check(assets.SelectedItem is AssetOption {Match:AssetMatch.Recommended},"native package is automatically recommended");
        Check(assets.Items.Cast<AssetOption>().All(option=>option.VisibleByDefault && option.Supported),"other systems and architectures are hidden initially");
        var all=(CheckBox)update.FindName("ShowOtherAssets");all.IsChecked=true;await Pump();
        assets.SelectedItem=assets.Items.Cast<AssetOption>().First(option=>option.Platform=="Linux");await Pump();
        Check(!apply.IsEnabled && ((TextBlock)update.FindName("AssetHint")).Text.Contains("不能"),"Linux attachment is labeled and cannot be applied on Windows");
        assets.SelectedItem=assets.Items.Cast<AssetOption>().First(option=>!option.Supported);await Pump();
        Check(!apply.IsEnabled,"unsupported format cannot be applied");
        assets.SelectedItem=assets.Items.Cast<AssetOption>().First(option=>option.Match==AssetMatch.OtherArchitecture);await Pump();
        var compatibility=(CheckBox)update.FindName("ArchitectureOverride");
        Check(!apply.IsEnabled && compatibility.Visibility==Visibility.Visible,"architecture mismatch requires explicit compatibility acknowledgement");
        compatibility.IsChecked=true;Check(apply.IsEnabled,"explicit emulation compatibility acknowledgement permits manual selection");
        all.IsChecked=false;await Pump();
        Check(assets.SelectedItem is AssetOption {Match:AssetMatch.Recommended} && apply.IsEnabled,"returning to default filter restores a safe native choice");
        var repair=(CheckBox)update.FindName("RepairMode");
        Check(repair.IsChecked==true && apply.Content.ToString()!.Contains("重新"),"same installed release offers repair reinstallation");
        repair.IsChecked=false;Check(apply.Content.ToString()=="下载并更新","normal update label is distinct from repair");repair.IsChecked=true;
        Check(FindVisual<Button>(main).Any(button=>Equals(button.Content,"标记为已安装此版本") && button.ToolTip.ToString()!.Contains("不会下载")),"version marking explains it does not download or install");
        var unknownRelease=new ReleaseInfo("config-2026.10","https://github.com/owner/config/releases","",[new ReleaseAsset("config.7z","https://github.com/owner/config/releases/download/v1/config.7z",100,null)]);
        var unknown=new UpdateWindow(new Subscription {Repo="owner/config",UpdateKind="archive",UpdateTarget=folder},unknownRelease,()=>{}) {Owner=main};unknown.Show();await Pump();
        var unknownAssets=(ComboBox)unknown.FindName("AssetList");var unknownApply=(Button)unknown.FindName("ApplyButton");
        Check(unknownAssets.SelectedItem is null && !unknownApply.IsEnabled,"ambiguous config archive requires an explicit choice");
        unknownAssets.SelectedIndex=0;await Pump();Check(unknownApply.IsEnabled,"manual choice allows a platform-neutral config archive");unknown.Close();
        var input=(TextBox)update.FindName("TargetInput");
        Check(input.Template.FindName("PART_ContentHost",input) is ScrollViewer,"themed textbox retains its edit surface");
        var kinds=(ComboBox)update.FindName("KindList");kinds.IsDropDownOpen=true;await Pump();
        Check(kinds.Template.FindName("PART_Popup",kinds) is Popup {IsOpen:true},"custom dropdown opens");
        Check(kinds.ItemContainerGenerator.ContainerFromIndex(1) is ComboBoxItem {IsSelected:true},"archive selection survives templating");
        Capture(update,"update-dropdown");
        Capture((FrameworkElement)((Popup)kinds.Template.FindName("PART_Popup",kinds)).Child,"dropdown-popup");
        kinds.IsDropDownOpen=false;
        kinds.SelectedIndex=0;await Pump();
        Check(!input.IsEnabled && ((FrameworkElement)update.FindName("ArchiveOptions")).Visibility==Visibility.Collapsed,"installer mode disables the target field");
        kinds.SelectedIndex=1;await Pump();
        var checkbox=(CheckBox)update.FindName("UnwrapArchive");checkbox.IsChecked=true;
        Check(checkbox.Template.FindName("CheckMark",checkbox) is System.Windows.Shapes.Path {Visibility:Visibility.Visible},"themed checkbox displays the checked state");
        var progress=(ProgressBar)update.FindName("DownloadProgress");progress.Visibility=Visibility.Visible;progress.IsIndeterminate=false;progress.Value=50;update.UpdateLayout();
        var indicator=(FrameworkElement)progress.Template.FindName("PART_Indicator",progress);
        var track=(FrameworkElement)progress.Template.FindName("PART_Track",progress);
        Check(indicator.ActualWidth>0 && indicator.ActualWidth<track.ActualWidth,"themed progress bar represents partial progress");
        progress.IsIndeterminate=true;await Pump();progress.IsIndeterminate=false;
        Check(progress.Template is not null,"indeterminate progress can switch back to determinate");
        var menu=input.ContextMenu;
        menu.PlacementTarget=input;menu.IsOpen=true;await Pump();
        Check(menu.Style is not null && ((MenuItem)menu.Items[0]).Template is not null,"context menu and entries load themed templates");menu.IsOpen=false;
        Check(menu.Items.OfType<MenuItem>().Count()==4,"textbox retains cut, copy, paste and select-all actions");
        var selectAll=menu.Items.OfType<MenuItem>().Last();menu.IsOpen=true;await Pump();
        Check(ReferenceEquals(selectAll.CommandTarget,input),"edit commands target the textbox that opened the menu");
        System.Windows.Input.ApplicationCommands.SelectAll.Execute(null,input);
        Check(input.SelectionLength==input.Text.Length,"text selection still works with the shared edit menu");menu.IsOpen=false;
        var dialog=new DialogWindow("将下载更新并覆盖目标目录中的同名文件。\n直接覆盖，不创建备份。","确认更新",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No) {Owner=update};
        dialog.Show();await Pump();
        Check(ReferenceEquals(((Button)dialog.FindName("PrimaryButton")).Style,apply.Style),"confirmation shares the primary action style");
        Check(((Button)dialog.FindName("SecondaryButton")).IsDefault && !((Button)dialog.FindName("PrimaryButton")).IsDefault,"confirmation defaults to cancel");Capture(dialog,"confirmation");dialog.Close();
        var canceled=new DialogWindow("删除此订阅？","删除订阅",MessageBoxButton.YesNo,MessageBoxImage.Warning) {Owner=update};
        _=canceled.Dispatcher.BeginInvoke(new Action(()=>canceled.Close()),DispatcherPriority.ApplicationIdle);
        canceled.ShowDialog();Check(canceled.Result==MessageBoxResult.No,"closing a confirmation never approves deletion");
        var confirmed=new DialogWindow("确认版本对应关系。","确认安装",MessageBoxButton.YesNo) {Owner=update};
        _=confirmed.Dispatcher.BeginInvoke(new Action(()=>((Button)confirmed.FindName("PrimaryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))),DispatcherPriority.ApplicationIdle);
        confirmed.ShowDialog();Check(confirmed.Result==MessageBoxResult.Yes,"explicit confirmation returns Yes");
        update.Close();main.Close();Console.WriteLine($"PASS {checks} WPF theme, popup, progress and confirmation checks");
    }
    private static void ReportFailure(Exception exception)
    {
        Console.Error.WriteLine(exception);
        Console.WriteLine("::error title=WPF interface check::"+exception.ToString().Replace("%","%25").Replace("\r","%0D").Replace("\n","%0A"));
    }
    private static async Task Pump(){await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);}
    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;
            foreach(var nested in FindVisual<T>(child))yield return nested;
        }
    }
    private static void Capture(FrameworkElement window,string name)
    {
        var folder=Path.Combine(Directory.GetCurrentDirectory(),"ui-screenshots");Directory.CreateDirectory(folder);
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(folder,name+".png"));encoder.Save(output);
    }
}
