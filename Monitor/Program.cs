using System.ServiceProcess;
using ReleasePilot.Monitoring;
using ReleasePilot.Worker;
if(args.Length==2 && args[0]=="--notify")return await DesktopNotification.RunAsync(args[1]);
if(args.Length==2 && args[0]=="--check-once")
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await new MonitorEngine().CheckAsync(args[1],timeout.Token);return 0;
}
ServiceBase.Run(new MonitorService());return 0;
sealed class MonitorService:ServiceBase
{
    private readonly CancellationTokenSource _stop=new();
    private Task? _worker;
    public MonitorService(){ServiceName=MonitorFiles.ServiceName;CanStop=true;AutoLog=true;}
    protected override void OnStart(string[] args)
    {
        _worker=Task.Run(async()=>
        {
            try{await new MonitorEngine().RunAsync(_stop.Token);}
            catch(OperationCanceledException) when(_stop.IsCancellationRequested){}
            catch(Exception ex){System.Diagnostics.EventLog.WriteEntry(ServiceName,ex.ToString(),System.Diagnostics.EventLogEntryType.Error);Environment.Exit(1);}
        });
    }
    protected override void OnStop(){_stop.Cancel();_worker?.Wait(TimeSpan.FromSeconds(15));}
}
