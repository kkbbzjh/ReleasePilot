using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ReleasePilot.Services;
public sealed record ReleaseInfo(string Tag,string Url,string Notes);
public sealed class GitHubClient
{
    private static readonly HttpClient Http = CreateClient();
    // HttpClient uses the Windows system proxy through the default proxy resolver.
    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseProxy = true, Proxy = HttpClient.DefaultProxy, AllowAutoRedirect = true };
        var client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ReleasePilot/0.1");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
    public static string ParseRepo(string input)
    {
        input=input.Trim().TrimEnd('/');
        if(Uri.TryCreate(input,UriKind.Absolute,out var uri))
        {
            if(uri.Scheme!="https" || !uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("仅支持 https://github.com 仓库");
            input=uri.AbsolutePath.Trim('/');
        }
        input=Regex.Replace(input,@"\.git$","",RegexOptions.IgnoreCase);
        if(!Regex.IsMatch(input,@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$")) throw new ArgumentException("请输入 owner/repo 或完整 GitHub 仓库 URL");
        return input;
    }
    public async Task<ReleaseInfo?> GetLatestAsync(string repo)
    {
        var path=string.Join('/',repo.Split('/').Select(Uri.EscapeDataString));
        using var response=await Http.GetAsync($"https://api.github.com/repos/{path}/releases/latest");
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new InvalidOperationException("GitHub API 限流或访问被拒绝");
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root=json.RootElement;
        return new ReleaseInfo(root.GetProperty("tag_name").GetString()??"",root.GetProperty("html_url").GetString()??"",root.TryGetProperty("body",out var body)?body.GetString()??"":"");
    }
}
