using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReleasePilot;
using ReleasePilot.Models;
using ReleasePilot.Services;
internal static class ScreenshotGallery
{
    // Render the real application with demonstration records, without network or installation actions.
    public static async Task Run(string folder)
    {
        var database=new Database(folder);
        var checkedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach(var item in new Subscription[]
        {
            new(){Repo="kkbbzjh/ReleasePilot",ExePath=@"C:\Apps\ReleasePilot\ReleasePilot.exe",LocalVersion="0.3.2",LatestVersion="v0.3.3",ConfirmedTag="v0.3.2",Status="发现更新",CheckedAt=checkedAt,
                ReleaseNotes="ReleasePilot v0.3.3\n\n• 独立 Windows 系统服务，关闭界面后继续检查更新\n• 支持安装包、压缩包与单文件更新，也可重新安装来修复软件\n• 主界面直接打开临时目录；安装失败可复制错误详情与查看日志"},
            new(){Repo="dyphire/mpv-config",UpdateKind="archive",UpdateTarget=@"C:\Apps\mpv",LocalVersion="2026-10-01",LatestVersion="2026-10-20",ConfirmedTag="2026-10-01",Status="发现新发布",PreservePaths="mpv.conf;input.conf",CheckedAt=checkedAt,
                ReleaseNotes="示例配置包：关联已解压的 MPV 目录，在安装 / 更新中选择压缩包。\n自定义配置可加入保留路径，更新时不会覆盖这些文件。"},
            new(){Repo="microsoft/PowerToys",ExePath=@"C:\Apps\PowerToys\PowerToys.exe",LocalVersion="0.94.0",LatestVersion="v0.94.0",ConfirmedTag="v0.94.0",Status="已是最新（已确认）",CheckedAt=checkedAt},
            new(){Repo="ShareX/ShareX",ExePath=@"C:\Apps\ShareX\ShareX.exe",LocalVersion="18.0.1",LatestVersion="v18.0.1",Status="已是最新",CheckedAt=checkedAt}
        })database.Save(item);
        var main=new MainWindow(database){Width=1320,Height=940,MinWidth=1320,MinHeight=940};main.Show();
        await Pump();
        var grid=(DataGrid)main.FindName("SubscriptionsGrid");
        grid.SelectedItem=grid.Items.Cast<Subscription>().First(s=>s.Repo=="kkbbzjh/ReleasePilot");
        ((TextBlock)main.FindName("StatusText")).Text="ReleasePilot v0.3.3 · 示例订阅展示 · 数据保存在本机";
        ((TextBox)main.FindName("NotesBox")).ScrollToEnd();
        await Pump();Capture(main,"main");
        var settings=new BackgroundWindow(new BackgroundSettings()){Owner=main,Height=800,MinHeight=800};settings.Show();
        await Pump();Capture(settings,"service");settings.Close();main.Close();
        Console.WriteLine("Captured real WPF application screenshots with demonstration subscriptions.");
    }
    private static async Task Pump()
    {
        await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await Task.Delay(180);
    }
    private static void Capture(Window window,string name)
    {
        window.UpdateLayout();
        var directory=Path.Combine(Directory.GetCurrentDirectory(),"docs","screenshots");Directory.CreateDirectory(directory);
        var content=(FrameworkElement)window.Content;
        var width=content.ActualWidth+content.Margin.Left+content.Margin.Right;
        var height=content.ActualHeight+content.Margin.Top+content.Margin.Bottom;
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height));
            var brush=new VisualBrush(content);
            if(content.Margin==new Thickness(0))
            {
                brush.ViewboxUnits=BrushMappingMode.Absolute;
                brush.Viewbox=new Rect(0,0,content.ActualWidth,content.ActualHeight);
            }
            drawing.DrawRectangle(brush,null,new Rect(content.Margin.Left,content.Margin.Top,content.ActualWidth,content.ActualHeight));
        }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*1.5),(int)Math.Ceiling(height*1.5),144,144,PixelFormats.Pbgra32);
        bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(directory,name+".png"));encoder.Save(output);
    }
}
