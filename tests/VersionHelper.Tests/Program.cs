using ReleasePilot.Services;
using ReleasePilot.Models;
using Microsoft.Data.Sqlite;
var cases = new (string Local,string Remote,string Expected)[] {
("0.2.0+7f0d62a83b4d41b5bd0fab6ef29fc1a26954e61c","v0.2.0","已是最新"),
("0.2.0.0","v0.2.0","已是最新"),
("0.2","v0.2.0","已是最新"),
(" V0.2.0 "," v0.2.0 ","已是最新"),
("0.2.0+old","0.2.0+new","已是最新"),
("0.2.0","v0.3.0","发现更新"),
("0.3.0","v0.2.0","本地版本较新"),
("0.2.0.1","v0.2.0","本地版本较新"),
("0.2.0-rc.1+abc","v0.2.0","发现更新"),
("0.2.0","v0.2.0-rc.1","本地版本较新"),
("0.2.0-rc.2","v0.2.0-rc.10","发现更新"),
("0.2.0-alpha","v0.2.0-beta","发现更新"),
("0.2.0-alpha.1","v0.2.0-alpha","本地版本较新"),
("0.2.0-1","v0.2.0-alpha","发现更新"),
("0.2.0-rc.01","v0.2.0","需要确认"),
("0.2.0+","v0.2.0","需要确认"),
("0.2.0","nightly-20261021","需要确认"),
("0.2.0 (abcdef)","v0.2.0","需要确认"),
("9999999999999.2.0","v0.2.0","需要确认"),
("","v0.2.0","未关联本地版本"),
("0.2.0","","需要确认"),
};
foreach(var (local,remote,expected) in cases) {
 var result=VersionHelper.Compare(local,remote);
 if(result!=expected)throw new Exception($"{local} vs {remote}: expected {expected}, got {result}");
}
Console.WriteLine($"PASS {cases.Length} version comparisons");
UpdateChecks.Run();
AssetChecks.Run();
var folder=Path.Combine(Path.GetTempPath(),"ReleasePilot-tests-"+Guid.NewGuid());
Directory.CreateDirectory(folder);
try
{
    // Exercise an existing v0.2.1 database, including repeatable migration and saved subscriptions.
    using(var connection=new SqliteConnection($"Data Source={Path.Combine(folder,"subscriptions.db")}"))
    {
        connection.Open();using var command=connection.CreateCommand();
        command.CommandText="CREATE TABLE subscriptions (repo TEXT PRIMARY KEY COLLATE NOCASE, exe_path TEXT NOT NULL DEFAULT '', local_version TEXT NOT NULL DEFAULT '', latest_version TEXT NOT NULL DEFAULT '', release_url TEXT NOT NULL DEFAULT '', release_notes TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT '尚未检查', checked_at TEXT NOT NULL DEFAULT ''); INSERT INTO subscriptions(repo) VALUES('owner/app')";
        command.ExecuteNonQuery();
    }
    var db=new Database(folder);
    var subscription=db.GetAll().Single();
    Check(subscription.Repo=="owner/app" && subscription.ConfirmedTag=="","legacy data preserved");
    subscription.ExePath=Path.Combine(folder,"app.exe");File.WriteAllText(subscription.ExePath,"original app bytes");
    subscription.LocalVersion="custom-build";subscription.LatestVersion="release-a";
    Check(InstallationConfirmation.GetStatus(subscription)=="需要确认","unknown version requires confirmation");
    InstallationConfirmation.Confirm(subscription);
    Check(InstallationConfirmation.GetStatus(subscription)=="已是最新（已确认）","confirmation resolves status");
    subscription.UpdateKind="replace";subscription.UpdateTarget=subscription.ExePath;subscription.AssetName="app.exe";subscription.PreservePaths="config;settings.ini";subscription.UnwrapArchive=true;
    db.Save(subscription);
    subscription=new Database(folder).GetAll().Single();
    Check(subscription.ConfirmedTag=="release-a" && InstallationConfirmation.GetStatus(subscription)=="已是最新（已确认）","confirmation persists after reopening database");
    Check(subscription.UpdateKind=="replace" && subscription.UpdateTarget==subscription.ExePath && subscription.AssetName=="app.exe" && subscription.PreservePaths=="config;settings.ini" && subscription.UnwrapArchive,"update settings persist");
    subscription.LatestVersion="release-b";
    Check(InstallationConfirmation.GetStatus(subscription)=="发现新发布","new unknown release still detected");
    subscription.ConfirmedTag="v1.0.0";subscription.LatestVersion="v1.1.0";
    Check(InstallationConfirmation.GetStatus(subscription)=="发现更新","new numeric release still detected");
    File.WriteAllText(subscription.ExePath,"different app bytes");
    Check(InstallationConfirmation.GetStatus(subscription)=="本地文件已变化，请确认","changed file invalidates confirmation");
    InstallationConfirmation.Clear(subscription);
    Check(subscription.ConfirmedTag=="" && subscription.ConfirmedSha256=="" && InstallationConfirmation.GetStatus(subscription)=="需要确认","clear restores automatic comparison");
    File.Delete(subscription.ExePath);
    var rejected=false;
    try{InstallationConfirmation.Confirm(subscription);}catch(IOException){rejected=true;}
    Check(rejected,"missing file cannot be confirmed");
    subscription.LatestVersion="";rejected=false;
    try{InstallationConfirmation.Confirm(subscription);}catch(InvalidOperationException){rejected=true;}
    Check(rejected,"missing release cannot be confirmed");
    File.WriteAllText(subscription.ExePath,"updated self EXE bytes");
    db.Save(subscription);
    UpdateService.RecordReplacement(db,subscription.Repo,subscription.ExePath,new ReleaseInfo("v0.2.3","https://github.com/owner/app/releases/tag/v0.2.3","new notes",Array.Empty<ReleaseAsset>()));
    var updated=db.GetAll().Single();
    Check(updated.ConfirmedTag=="v0.2.3" && updated.ReleaseNotes=="new notes" && InstallationConfirmation.GetStatus(updated)=="已是最新（已确认）","self replacement refreshes the persisted installation receipt");
    db.Save(subscription);db.Delete(subscription.Repo);
    Check(db.GetAll().Count==0,"delete after migration");
    Console.WriteLine("PASS 13 confirmation, SQLite migration and persistence checks");
}
finally
{
    SqliteConnection.ClearAllPools();Directory.Delete(folder,true);
}
static void Check(bool condition,string name)
{
    if(!condition)throw new Exception(name);
}
if(args.Length==2)
{
    var actual=VersionHelper.ReadExeVersion(args[0]);
    Console.WriteLine($"Published EXE ProductVersion: {actual}");
    if(VersionHelper.Compare(actual,args[1])!="已是最新")
        throw new Exception($"Published EXE version {actual} does not match {args[1]}");
    Console.WriteLine("PASS published EXE version matches release tag");
}

var background=new BackgroundSettings();
void NotifyCheck(bool value,string message){if(!value)throw new Exception(message);}
NotifyCheck(!background.Enabled&&!background.StartAtLogin,"background features must be opt in");
NotifyCheck(background.ShouldNotify("owner/app","v2","发现更新","v1"),"newer release notifies");
background.NotifiedTags["owner/app"]="v2";
NotifyCheck(!background.ShouldNotify("OWNER/app","v2","发现更新","v1"),"same tag notification deduplicates ignoring repository casing");
NotifyCheck(background.ShouldNotify("owner/app","v3","需要确认","v2"),"unknown version notifies when upstream release changes");
NotifyCheck(!background.ShouldNotify("owner/app","v3","已是最新（已确认）","v2"),"already installed release must not notify");
NotifyCheck(!background.ShouldNotify("owner/app","v3","本地版本较新","v2"),"local newer version must not notify");
NotifyCheck(!background.ShouldNotify("owner/app","v3","未关联本地版本",""),"first unknown local version has no false update alert");
NotifyCheck(!background.ShouldNotify("owner/app","","发现更新","v2"),"missing release never notifies");
Console.WriteLine("PASS 8 background notification policy checks");
