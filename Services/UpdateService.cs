using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ReleasePilot.Services;

public static class UpdateService
{
    private static readonly HttpClient Downloads=new(new HttpClientHandler {UseProxy=true,Proxy=HttpClient.DefaultProxy}) {Timeout=Timeout.InfiniteTimeSpan};
    public static string CreateWorkspace()
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReleasePilot","Updates",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);return path;
    }
    public static async Task<string> DownloadAsync(ReleaseAsset asset,string workspace,IProgress<string> progress,CancellationToken token)
    {
        if(!Uri.TryCreate(asset.Url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com")throw new IOException("附件必须来自 GitHub HTTPS Release。");
        var extension=Path.GetExtension(asset.Name).ToLowerInvariant();
        if(extension is not (".exe" or ".msi" or ".zip" or ".7z"))throw new IOException("当前支持 EXE、MSI、ZIP 和 7z 附件。");
        var path=Path.Combine(workspace,"download"+extension);
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        request.Headers.UserAgent.ParseAdd("ReleasePilot/0.2.3");
        using var response=await Downloads.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        await using(var source=await response.Content.ReadAsStreamAsync(token))
        await using(var destination=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true))
        {
            var buffer=new byte[131072];long total=0;var lastReport=DateTime.MinValue;
            int read;
            while((read=await source.ReadAsync(buffer,token))>0)
            {
                await destination.WriteAsync(buffer.AsMemory(0,read),token);total+=read;
                if(DateTime.UtcNow-lastReport>TimeSpan.FromMilliseconds(150))
                {
                    progress.Report($"正在下载：{total/1024d/1024d:F1} / {asset.Size/1024d/1024d:F1} MB");lastReport=DateTime.UtcNow;
                }
            }
            if(asset.Size>0 && total!=asset.Size)throw new IOException("附件大小不匹配，已停止更新。");
        }
        if(!string.IsNullOrEmpty(asset.Digest))
        {
            if(!asset.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))throw new IOException("不支持该附件的摘要算法，已停止更新。");
            await using var file=File.OpenRead(path);
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,token));
            if(!hash.Equals(asset.Digest[7..],StringComparison.OrdinalIgnoreCase))throw new IOException("附件 SHA256 不匹配，已停止更新。");
        }
        ValidatePackage(path);
        return path;
    }
    public static void ValidatePackage(string path)
    {
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".exe" or ".msi"))return;
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        if(extension==".msi")
        {
            if(!reader.ReadBytes(8).SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1}))throw new IOException("下载内容不是有效的 MSI 安装包。");
            return;
        }
        if(stream.Length<64 || reader.ReadUInt16()!=0x5A4D)throw new IOException("下载内容不是 Windows EXE。");
        stream.Position=0x3c;var offset=reader.ReadUInt32();
        if(offset>stream.Length-24)throw new IOException("EXE 文件头损坏。");
        stream.Position=offset;if(reader.ReadUInt32()!=0x4550)throw new IOException("EXE 文件头无效。");
    }
    public static string SafeRelative(string value)
    {
        var relative=value.Replace('\\','/');
        if(string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains(':'))throw new IOException("压缩包包含无效路径。");
        foreach(var part in relative.TrimEnd('/').Split('/'))
        {
            var stem=part.Split('.')[0];
            if(part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(['<','>','"','|','?','*','\0'])>=0 || part.Any(char.IsControl)
                || System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new IOException("压缩包或保留列表包含不安全的路径。");
        }
        return relative.TrimEnd('/').Replace('/',Path.DirectorySeparatorChar);
    }
    public static string[] PreservedPaths(string text)=>text.Split([';','\n','\r'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(SafeRelative).ToArray();
    private static bool IsPreserved(string relative,IEnumerable<string> preserved)=>preserved.Any(p=>relative.Equals(p,StringComparison.OrdinalIgnoreCase) || relative.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
    public static void RejectLinks(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("目标路径包含链接或重解析点，请选择真实目录。");
    }
    public static string Extract(string archivePath,string workspace,CancellationToken token)
    {
        var stage=Path.Combine(workspace,"stage");Directory.CreateDirectory(stage);
        using var archive=ArchiveFactory.Open(archivePath);
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;var count=0;
        if(archive.Type is not (ArchiveType.Zip or ArchiveType.SevenZip))throw new IOException("附件内容必须是 ZIP 或 7z 压缩包。");
        void ExtractEntry(IEntry entry,Func<Stream> open)
        {
            token.ThrowIfCancellationRequested();
            if(++count>100000)throw new IOException("压缩包文件数量过多。");
            var relative=SafeRelative(entry.Key??"");
            if(entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget) || (entry.Attrib.GetValueOrDefault()&0x400)!=0 || ((entry.Attrib.GetValueOrDefault()>>16)&0xF000)==0xA000)
                throw new IOException("不支持加密附件、符号链接或重解析点。");
            var destination=Path.Combine(stage,relative);
            if(entry.IsDirectory){Directory.CreateDirectory(destination);return;}
            if(!names.Add(relative))throw new IOException("压缩包存在重复文件路径。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source=open();using var output=new FileStream(destination,FileMode.CreateNew);
            var buffer=new byte[131072];int read;
            while((read=source.Read(buffer))>0)
            {
                token.ThrowIfCancellationRequested();size+=read;
                if(size>8L*1024*1024*1024)throw new IOException("解压内容超过 8 GB 限制。");
                output.Write(buffer,0,read);
            }
        }
        if(archive.Type==ArchiveType.SevenZip)
        {
            using var reader=archive.ExtractAllEntries();
            while(reader.MoveToNextEntry())ExtractEntry(reader.Entry,()=>reader.OpenEntryStream());
        }
        else foreach(var entry in archive.Entries)ExtractEntry(entry,entry.OpenEntryStream);
        if(names.Count==0)throw new IOException("压缩包中没有可更新的文件。");
        return stage;
    }
    public static int ApplyDirectory(string stage,string target,string backup,string preserve,bool unwrap)
    {
        target=Path.GetFullPath(target);RejectLinks(target);
        var preserved=PreservedPaths(preserve);
        if(unwrap)
        {
            var roots=Directory.GetDirectories(stage);
            if(Directory.GetFiles(stage).Length!=0 || roots.Length!=1)throw new IOException("压缩包并非只有一个顶层目录，不能去掉顶层目录。");
            stage=roots[0];
        }
        var files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories)
            .Select(source=>(Source:source,Relative:SafeRelative(Path.GetRelativePath(stage,source))))
            .Where(file=>!IsPreserved(file.Relative,preserved)).OrderBy(file=>file.Relative,StringComparer.Ordinal).ToArray();
        if(files.Length==0)throw new IOException("保留规则排除了全部文件，没有执行更新。");
        // Back up every overwritten file before changing the installation.
        foreach(var file in files)
        {
            var destination=Path.Combine(target,file.Relative);RejectLinks(destination);
            if(Directory.Exists(destination))throw new IOException($"目标文件与目录冲突：{file.Relative}");
            if(File.Exists(destination))
            {
                var saved=Path.Combine(backup,file.Relative);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.Copy(destination,saved,false);
            }
        }
        var applied=new List<string>();
        try
        {
            foreach(var file in files)
            {
                var destination=Path.Combine(target,file.Relative);RejectLinks(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary=destination+".releasepilot-"+Guid.NewGuid().ToString("N");
                try{File.Copy(file.Source,temporary,false);File.Move(temporary,destination,true);applied.Add(file.Relative);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        catch(Exception original)
        {
            var failures=new List<string>();
            foreach(var relative in applied.AsEnumerable().Reverse())
            {
                try
                {
                    var destination=Path.Combine(target,relative);var saved=Path.Combine(backup,relative);
                    if(File.Exists(saved))File.Copy(saved,destination,true);else File.Delete(destination);
                }
                catch(Exception ex){failures.Add(ex.Message);}
            }
            throw new IOException(failures.Count==0?"更新失败，已恢复被修改的文件。请关闭目标软件后重试。":$"更新失败，部分文件恢复失败。备份位于 {backup}。",original);
        }
        return files.Length;
    }
    public static void ReplaceFile(string source,string target,string backup)
    {
        if(!File.Exists(target))throw new IOException("请先选择已存在的本地 EXE。");
        RejectLinks(target);Directory.CreateDirectory(backup);
        var stage=Path.Combine(backup,"replacement");Directory.CreateDirectory(stage);
        File.Copy(source,Path.Combine(stage,Path.GetFileName(target)));
        ApplyDirectory(stage,Path.GetDirectoryName(Path.GetFullPath(target))!,Path.Combine(backup,"original"),"",false);
    }
    public static ProcessStartInfo InstallerStartInfo(string path)
    {
        var extension=Path.GetExtension(path);
        if(extension.Equals(".msi",StringComparison.OrdinalIgnoreCase))
        {
            var start=new ProcessStartInfo("msiexec.exe") {UseShellExecute=true};
            start.ArgumentList.Add("/i");start.ArgumentList.Add(Path.GetFullPath(path));return start;
        }
        if(!extension.Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("安装包必须是 EXE 或 MSI。");
        return new ProcessStartInfo(Path.GetFullPath(path)) {UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(path))!};
    }
    public static bool IsSelf(string path)=>Path.GetFullPath(path).Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase);
    public sealed record SelfUpdatePlan(int ParentId,string Source,string Target,string Backup,string Digest,string Repo,ReleaseInfo Release);
    public static void StartSelfReplacement(string source,string target,string workspace,string repo,ReleaseInfo release)
    {
        RejectLinks(target);
        if(!OperatingSystem.IsWindows() || !string.Equals(Path.GetExtension(Environment.ProcessPath),".exe",StringComparison.OrdinalIgnoreCase))
            throw new IOException("自更新需要运行发布的 Windows 单文件版本。");
        using var file=File.OpenRead(source);
        var plan=new SelfUpdatePlan(Environment.ProcessId,source,Path.GetFullPath(target),Path.Combine(workspace,"backup"),Convert.ToHexString(SHA256.HashData(file)),repo,release);
        var planPath=Path.Combine(workspace,"plan.json");
        File.WriteAllText(planPath,System.Text.Json.JsonSerializer.Serialize(plan));
        var helper=Path.Combine(workspace,"ReleasePilot.UpdateHelper.exe");
        File.Copy(Environment.ProcessPath!,helper);
        var start=new ProcessStartInfo(helper) {UseShellExecute=false};
        start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(planPath);
        _=Process.Start(start)??throw new IOException("无法启动自更新助手。");
    }
    public static async Task CompleteSelfReplacementAsync(string planPath)
    {
        var plan=System.Text.Json.JsonSerializer.Deserialize<SelfUpdatePlan>(File.ReadAllText(planPath))??throw new IOException("自更新计划无效。");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            using var parent=Process.GetProcessById(plan.ParentId);
            await parent.WaitForExitAsync(timeout.Token);
        }
        catch(ArgumentException) { }
        await using(var file=File.OpenRead(plan.Source))
        {
            var digest=Convert.ToHexString(await SHA256.HashDataAsync(file,timeout.Token));
            if(digest!=plan.Digest)throw new IOException("自更新附件 SHA256 不匹配。");
        }
        ReplaceFile(plan.Source,plan.Target,plan.Backup);
        RecordReplacement(new Database(),plan.Repo,plan.Target,plan.Release);
        _=Process.Start(new ProcessStartInfo(plan.Target) {UseShellExecute=true})??throw new IOException("更新完成，但无法自动重启，请手动启动软件。");
    }
    public static void RecordReplacement(Database database,string repo,string target,ReleaseInfo release)
    {
        var subscription=database.GetAll().FirstOrDefault(s=>s.Repo.Equals(repo,StringComparison.OrdinalIgnoreCase));
        if(subscription is not null)
        {
            subscription.ExePath=target;subscription.UpdateKind="replace";subscription.UpdateTarget=target;
            subscription.LatestVersion=release.Tag;subscription.ReleaseUrl=release.Url;subscription.ReleaseNotes=release.Notes;
            subscription.LocalVersion=VersionHelper.ReadExeVersion(target);
            InstallationConfirmation.Confirm(subscription);subscription.Status=InstallationConfirmation.GetStatus(subscription);
            database.Save(subscription);
        }
    }
}
