using System.Net;
using System.Text.Json;
using ReleasePilot.Monitoring;
namespace ReleasePilot.Worker;
public sealed class MonitorEngine
{
    public async Task CheckAsync(string directory,CancellationToken token,Func<string,Task<WatchRelease?>>? fetch=null)
    {
        MonitorFiles.RejectLinks(directory);
        var configPath=Path.Combine(directory,"config.json");var statePath=Path.Combine(directory,"state.json");
        var config=JsonSerializer.Deserialize(MonitorFiles.Read(configPath),MonitorJson.Default.MonitorProfile)??new();
        if(!config.Enabled)return;
        MonitorState state;
        try{state=JsonSerializer.Deserialize(MonitorFiles.Read(statePath),MonitorJson.Default.MonitorState)??new();}
        catch(Exception ex) when(ex is IOException or JsonException){state=new();}
        state.Releases=new(state.Releases,StringComparer.OrdinalIgnoreCase);
        if(state.Revision==config.Revision && DateTimeOffset.UtcNow-state.LastCheckUtc<TimeSpan.FromMinutes(Math.Clamp(config.IntervalMinutes,5,1440)))return;
        using var handler=new HttpClientHandler();
        if(!string.IsNullOrWhiteSpace(config.ProxyUrl))
        {
            if(!Uri.TryCreate(config.ProxyUrl,UriKind.Absolute,out var proxy)||proxy.Scheme is not ("http" or "https"))throw new IOException("服务代理地址无效。");
            handler.Proxy=new WebProxy(proxy);
        }
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(25)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReleasePilot-Monitor/0.3.1");
        var releases=new Dictionary<string,WatchRelease>(StringComparer.OrdinalIgnoreCase);
        state.Error="";
        foreach(var subscription in config.Subscriptions.Where(x=>MonitorFiles.ValidRepo(x.Repo)).DistinctBy(x=>x.Repo,StringComparer.OrdinalIgnoreCase).Take(200))
        {
            token.ThrowIfCancellationRequested();
            state.Releases.TryGetValue(subscription.Repo,out var old);
            try
            {
                WatchRelease? release;
                if(fetch is not null)release=await fetch(subscription.Repo);
                else
                {
                    using var response=await http.GetAsync($"https://api.github.com/repos/{subscription.Repo}/releases/latest",token);
                    if(response.StatusCode==HttpStatusCode.NotFound)continue;
                    if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                        throw new HttpRequestException("GitHub 请求受限，将在下个检查周期重试。");
                    response.EnsureSuccessStatusCode();
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    var root=json.RootElement;
                    release=new(){Tag=root.GetProperty("tag_name").GetString()??"",Url=root.GetProperty("html_url").GetString()??"",Notes=root.TryGetProperty("body",out var notes)?notes.GetString()??"":""};
                    if(release.Notes.Length>8000)release.Notes=release.Notes[..8000];
                }
                if(release is null)continue;
                release.Notify=MonitorFiles.NeedsNotification(subscription,old,release.Tag);release.CheckedAt=DateTimeOffset.UtcNow;
                releases[subscription.Repo]=release;
            }
            catch(Exception ex) when(ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                token.ThrowIfCancellationRequested();
                state.Error=$"{subscription.Repo}: {ex.Message}";
                if(old is not null)releases[subscription.Repo]=old;
            }
            if(fetch is null)await Task.Delay(1000,token);
        }
        // A desktop edit during a network check wins; never restore a removed subscription.
        var latest=JsonSerializer.Deserialize(MonitorFiles.Read(configPath),MonitorJson.Default.MonitorProfile);
        if(latest?.Revision!=config.Revision || !latest.Enabled)return;
        state.Releases=releases;state.LastCheckUtc=DateTimeOffset.UtcNow;state.Revision=config.Revision;
        MonitorFiles.Write(statePath,JsonSerializer.Serialize(state,MonitorJson.Default.MonitorState));
    }
    public async Task RunAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            if(Directory.Exists(MonitorFiles.Root))
            {
                MonitorFiles.RejectLinks(MonitorFiles.Root);
                foreach(var directory in Directory.GetDirectories(MonitorFiles.Root).Where(d=>MonitorFiles.ValidSid(Path.GetFileName(d))))
                {
                    try{await CheckAsync(directory,token);}
                    catch(OperationCanceledException) when(token.IsCancellationRequested){return;}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        try{MonitorFiles.Write(Path.Combine(directory,"error.txt"),DateTimeOffset.Now+" "+ex.Message);}catch(Exception){}
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(30),token);
        }
    }
}
