using System.Text.Json;
using ReleasePilot.Monitoring;
using ReleasePilot.Worker;
internal static class MonitorChecks
{
    public static async Task RunAsync()
    {
        int checks=0;void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
        var directory=Path.Combine(Path.GetTempPath(),"ReleasePilot-monitor-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var watch=new WatchSubscription{Repo="owner/app",SeenTag="v1",InstalledTag="v1"};
            Check(!MonitorFiles.NeedsNotification(watch,null,"v1"),"installed tag never triggers notification");
            Check(MonitorFiles.NeedsNotification(watch,null,"v2"),"new tag notifies after desktop exits");
            Check(!MonitorFiles.NeedsNotification(new(){Repo="owner/config"},null,"v1"),"first unknown version only establishes baseline");
            Check(MonitorFiles.NeedsNotification(new(){Repo="owner/app",SeenTag="v2",HasUpdate=true},null,"v2"),"already discovered update is notified");
            Check(!MonitorFiles.ValidRepo("../app")&&!MonitorFiles.ValidRepo("owner/app/../../x"),"invalid repository paths rejected");
            Check(MonitorFiles.ValidSid("S-1-5-21-1-2-3-1001")&&!MonitorFiles.ValidSid("../other"),"profile directories restricted to Windows user identifiers");
            var config=new MonitorProfile{Enabled=true,Revision="a",IntervalMinutes=60,Subscriptions=[watch]};
            void Save()=>MonitorFiles.Write(Path.Combine(directory,"config.json"),JsonSerializer.Serialize(config,MonitorJson.Default.MonitorProfile));
            MonitorState State()=>JsonSerializer.Deserialize(MonitorFiles.Read(Path.Combine(directory,"state.json")),MonitorJson.Default.MonitorState)!;
            Save();var engine=new MonitorEngine();int requests=0;
            Task<WatchRelease?> Fetch(string repo){requests++;return Task.FromResult<WatchRelease?>(new(){Tag="v2",Url="https://github.com/owner/app/releases/tag/v2"});}
            await engine.CheckAsync(directory,CancellationToken.None,Fetch);
            var state=State();Check(requests==1&&state.Releases[watch.Repo].Tag=="v2"&&state.Releases[watch.Repo].Notify,"service persists release and pending reminder");
            await engine.CheckAsync(directory,CancellationToken.None,Fetch);Check(requests==1,"configured interval prevents repeated requests");
            var receipt=new NotificationReceipt();Check(MonitorFiles.Pending(config,state,receipt).Count==1,"pending toast is discoverable by short-lived notifier");
            receipt.Tags["OWNER/APP"]="v2";Check(MonitorFiles.Pending(config,state,receipt).Count==0,"notification receipt deduplicates same tag");
            config.Enabled=false;Save();await engine.CheckAsync(directory,CancellationToken.None,Fetch);Check(requests==1&&MonitorFiles.Pending(config,state,new()).Count==0,"disabled profile neither checks nor notifies");
            config.Enabled=true;watch.InstalledTag="v2";config.Revision="b";Save();await engine.CheckAsync(directory,CancellationToken.None,Fetch);
            Check(!State().Releases[watch.Repo].Notify,"installed updates clear pending reminders");
            config.Revision="c";Save();
            await engine.CheckAsync(directory,CancellationToken.None,repo=>{config.Subscriptions.Clear();config.Revision="d";Save();return Fetch(repo);});
            Check(State().Revision=="b","configuration changed during HTTP request must win");
            await engine.CheckAsync(directory,CancellationToken.None,Fetch);Check(State().Releases.Count==0,"removed subscriptions disappear from service state");
            config.Subscriptions.Add(watch);config.Revision="e";Save();
            await engine.CheckAsync(directory,CancellationToken.None,repo=>throw new HttpRequestException("network unavailable"));
            Check(State().Error.Contains("network unavailable"),"offline errors persisted for settings page");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();config.Revision="f";Save();
            try{await engine.CheckAsync(directory,cancellation.Token,Fetch);throw new Exception("cancellation was ignored");}catch(OperationCanceledException){checks++;}
            Console.WriteLine($"PASS {checks} Windows service monitoring, notification, interval and configuration checks");
        }
        finally{Directory.Delete(directory,true);}
    }
}
