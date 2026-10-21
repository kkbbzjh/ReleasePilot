using System.IO.Compression;
using ReleasePilot.Models;
using ReleasePilot.Services;

static class UpdateChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"ReleasePilot-update-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);var checks=0;
        void Assert(bool value,string name){if(!value)throw new Exception(name);checks++;}
        void Reject(Action action,string name){try{action();}catch(IOException){checks++;return;}throw new Exception(name);}
        try
        {
            var zip=Path.Combine(root,"update.zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            {
                foreach(var (name,text) in new[]{("package/app.exe","new app"),("package/config/mpv.conf","upstream config"),("package/new.txt","new file")})
                {using var writer=new StreamWriter(archive.CreateEntry(name).Open());writer.Write(text);}
            }
            var zipProgress=new RecordedProgress();
            var workspace=Path.Combine(root,"workspace");var stage=UpdateService.Extract(zip,workspace,CancellationToken.None,zipProgress);
            var zipDone=zipProgress.Values.Last();
            Assert(zipDone.Complete && zipDone.CompletedFiles==3 && zipDone.TotalFiles==3 && zipDone.ExtractedBytes==Directory.GetFiles(stage,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length) && zipDone.ExtractedBytes==zipDone.TotalBytes && zipDone.Percentage==100,"ZIP progress reports actual bytes and completed files");
            Assert(zipProgress.Values.Any(value=>value.CurrentFile.EndsWith("app.exe") && !value.Complete) && zipProgress.Values.Where(value=>!value.Complete).All(value=>value.Percentage is null or <100),"current file is reported and 100 percent is reserved for completion");
            var target=Path.Combine(root,"target");Directory.CreateDirectory(Path.Combine(target,"config"));
            File.WriteAllText(Path.Combine(target,"app.exe"),"old app");File.WriteAllText(Path.Combine(target,"config/mpv.conf"),"my settings");File.WriteAllText(Path.Combine(target,"unrelated.txt"),"keep me");
            var backup=Path.Combine(root,"backup");
            var count=UpdateService.ApplyDirectory(stage,target,"config/mpv.conf",true);
            Assert(count==2 && File.ReadAllText(Path.Combine(target,"app.exe"))=="new app","archive applies updated files");
            Assert(File.ReadAllText(Path.Combine(target,"config/mpv.conf"))=="my settings","custom config preserved");
            Assert(File.ReadAllText(Path.Combine(target,"unrelated.txt"))=="keep me","unrelated file retained");
            Assert(!Directory.Exists(backup),"no backup directory created");
            var sevenProgress=new RecordedProgress();
            var seven=UpdateService.Extract(Path.Combine(AppContext.BaseDirectory,"Fixtures/tiny.7z"),Path.Combine(root,"seven"),CancellationToken.None,sevenProgress);
            Assert(File.ReadAllText(Path.Combine(seven,"portable/hello.txt"))=="hello from 7z\n","real 7z payload extracted");
            Assert(sevenProgress.Values.Last() is {Complete:true,CompletedFiles:1,TotalFiles:1,ExtractedBytes:14,TotalBytes:14},"7z progress excludes directory entries and reports decompressed bytes");
            var empty=Path.Combine(root,"empty-file.zip");
            using(var archive=ZipFile.Open(empty,ZipArchiveMode.Create))archive.CreateEntry("empty.txt");
            var emptyProgress=new RecordedProgress();UpdateService.Extract(empty,Path.Combine(root,"empty"),CancellationToken.None,emptyProgress);
            Assert(emptyProgress.Values.Last() is {Complete:true,CompletedFiles:1,ExtractedBytes:0,Percentage:100},"zero-byte file extraction completes without dividing by zero");
            var traversal=Path.Combine(root,"traversal.zip");
            using(var archive=ZipFile.Open(traversal,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("../escaped.txt").Open());writer.Write("bad");}
            var failedProgress=new RecordedProgress();
            Reject(()=>UpdateService.Extract(traversal,Path.Combine(root,"evil"),CancellationToken.None,failedProgress),"archive traversal must fail");
            Assert(!failedProgress.Values.Any(value=>value.Complete),"failed extraction cannot report completion");
            Assert(!File.Exists(Path.Combine(root,"evil/escaped.txt")),"archive cannot escape staging");
            foreach(var value in new[]{"../outside","C:/Windows/file","/absolute","config/../../bad","CON.txt","ok/file:stream","bad./name"})
                Reject(()=>UpdateService.SafeRelative(value),"unsafe archive/preserve path accepted");
            var cancel=new CancellationToken(true);var canceled=false;
            try{UpdateService.Extract(zip,Path.Combine(root,"canceled"),cancel);}catch(OperationCanceledException){canceled=true;}
            Assert(canceled,"cancellation before extraction");
            var replacement=Path.Combine(root,"replacement.exe");File.WriteAllText(replacement,"replacement bytes");
            UpdateService.ReplaceFile(replacement,Path.Combine(target,"app.exe"));
            Assert(File.ReadAllText(Path.Combine(target,"app.exe"))=="replacement bytes","single-file replacement");
            Assert(!Directory.Exists(Path.Combine(root,"replace-backup")),"replacement creates no backup");
            Reject(()=>UpdateService.ValidatePackage(replacement),"non-PE EXE must be rejected");
            var msi=UpdateService.InstallerStartInfo(Path.Combine(root,"installer with spaces.msi"));
            Assert(msi.FileName=="msiexec.exe" && msi.ArgumentList[0]=="/i" && msi.ArgumentList[1].EndsWith("installer with spaces.msi"),"MSI interactive arguments preserve spaces");
            Assert(UpdateService.InstallerStartInfo(replacement).FileName==replacement,"EXE installer launches selected payload");
            Reject(()=>UpdateService.InstallerStartInfo(zip),"ZIP must not launch as installer");
            var broken=Path.Combine(root,"broken-stage");Directory.CreateDirectory(Path.Combine(broken,"z"));File.WriteAllText(Path.Combine(broken,"a.txt"),"changed");File.WriteAllText(Path.Combine(broken,"z/child.txt"),"cannot copy");
            var rollback=Path.Combine(root,"rollback-target");Directory.CreateDirectory(rollback);File.WriteAllText(Path.Combine(rollback,"a.txt"),"original");File.WriteAllText(Path.Combine(rollback,"z"),"blocks directory creation");
            Reject(()=>UpdateService.ApplyDirectory(broken,rollback,"",false),"failed update must report failure");
            Assert(File.ReadAllText(Path.Combine(rollback,"a.txt"))=="original","partial update rolled back");
            var subscription=new Subscription {UpdateKind="archive",UpdateTarget=target,LatestVersion="config-2026.10"};
            InstallationConfirmation.Confirm(subscription);
            Assert(subscription.LocalVersion==subscription.LatestVersion && InstallationConfirmation.GetStatus(subscription)=="已是最新（已确认）","archive records installed release without EXE metadata");
            subscription.LatestVersion="config-2026.11";
            Assert(InstallationConfirmation.GetStatus(subscription)=="发现新发布","archive detects new nonnumeric release");
            InstallationConfirmation.Clear(subscription);
            Assert(InstallationConfirmation.GetStatus(subscription)=="未记录安装版本","archive confirmation can be reset");
            var link=Path.Combine(root,"linked-target");
            if(OperatingSystem.IsWindows())
            {
                var start=new System.Diagnostics.ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true};
                start.Arguments=$"/d /c mklink /J \"{link}\" \"{target}\"";
                using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();
                if(process.ExitCode!=0)throw new Exception("Failed to create Windows junction test fixture");
            }
            else Directory.CreateSymbolicLink(link,target);
            Reject(()=>UpdateService.ApplyDirectory(stage,link,"",true),"linked installation directory rejected");
            Directory.Delete(link);
            Console.WriteLine($"PASS {checks} archive, installer, replacement, no-backup and preflight checks");
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class RecordedProgress : IProgress<ExtractionProgress>
    {
        public List<ExtractionProgress> Values {get;}=[];
        public void Report(ExtractionProgress value)=>Values.Add(value);
    }
}
