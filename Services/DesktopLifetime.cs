using System.IO;
using System.IO.Pipes;
using System.Windows;
namespace ReleasePilot.Services;
// Handles only reopening a second desktop instance. It never keeps a closed window alive.
public sealed class DesktopLifetime:IDisposable
{
    private readonly MainWindow _window;
    private readonly CancellationTokenSource _stop=new();
    public DesktopLifetime(MainWindow window){_window=window;_=ListenAsync();}
    public void Show(){_window.Show();if(_window.WindowState==WindowState.Minimized)_window.WindowState=WindowState.Normal;_window.Activate();}
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
            catch(OperationCanceledException){break;}
            catch(IOException){if(_stop.IsCancellationRequested)break;await Task.Delay(500);}
        }
    }
    public void Dispose(){_stop.Cancel();}
}
