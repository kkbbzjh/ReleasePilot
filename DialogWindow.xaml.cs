using System.Windows;
using System.Windows.Media;

namespace ReleasePilot;

public partial class DialogWindow : Window
{
    private readonly MessageBoxResult _primary;
    private readonly MessageBoxResult _secondary;
    public MessageBoxResult Result {get;private set;}
    public DialogWindow(string message,string title,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.Information,MessageBoxResult defaultResult=MessageBoxResult.None)
    {
        if(buttons is not (MessageBoxButton.OK or MessageBoxButton.OKCancel or MessageBoxButton.YesNo))throw new ArgumentOutOfRangeException(nameof(buttons));
        InitializeComponent();Title=title+" · ReleasePilot";HeadingText.Text=title;MessageText.Text=message;
        var question=buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;
        _primary=question?MessageBoxResult.Yes:MessageBoxResult.OK;
        _secondary=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        Result=buttons==MessageBoxButton.OK?MessageBoxResult.OK:_secondary;
        PrimaryButton.Content=question?"确认继续":buttons==MessageBoxButton.OKCancel?"确认":"知道了";
        SecondaryButton.Visibility=buttons==MessageBoxButton.OK?Visibility.Collapsed:Visibility.Visible;
        // Confirmations default to the safe action; Enter does not approve a destructive action.
        var primaryDefault=buttons==MessageBoxButton.OK || defaultResult is MessageBoxResult.Yes or MessageBoxResult.OK;
        PrimaryButton.IsDefault=primaryDefault;SecondaryButton.IsDefault=!primaryDefault;
        Loaded+=(_,_)=>{if(primaryDefault)PrimaryButton.Focus();else SecondaryButton.Focus();};
        BadgeText.Text=icon==MessageBoxImage.Error?"!":question?"?":"i";
        if(icon is MessageBoxImage.Warning or MessageBoxImage.Error)
        {
            Badge.Background=new SolidColorBrush(Color.FromRgb(253,235,237));
            BadgeText.Foreground=(Brush)FindResource("DangerBrush");
        }
        if(title.Contains("删除",StringComparison.Ordinal))
        {
            PrimaryButton.Content="删除订阅";PrimaryButton.Style=(Style)FindResource("DangerButton");
        }
        MaxHeight=Math.Max(300,Math.Min(720,SystemParameters.WorkArea.Height-80));
    }
    private void Primary_Click(object sender,RoutedEventArgs e){Result=_primary;DialogResult=true;}
    private void Secondary_Click(object sender,RoutedEventArgs e){Result=_secondary;DialogResult=false;}
}

public static class AppDialog
{
    public static MessageBoxResult Show(string message,string title="提示",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.Information,MessageBoxResult defaultResult=MessageBoxResult.None)
    {
        var owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(window=>window.IsActive);
        return Show(owner,message,title,buttons,icon,defaultResult);
    }
    public static MessageBoxResult Show(Window? owner,string message,string title="提示",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.Information,MessageBoxResult defaultResult=MessageBoxResult.None)
    {
        var dialog=new DialogWindow(message,title,buttons,icon,defaultResult);
        if(owner is {IsVisible:true})dialog.Owner=owner;
        else dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();return dialog.Result;
    }
}
