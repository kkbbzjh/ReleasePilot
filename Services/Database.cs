using System.IO;
using Microsoft.Data.Sqlite;
using ReleasePilot.Models;
namespace ReleasePilot.Services;
public sealed class Database
{
    private readonly string _connectionString;
    public Database()
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReleasePilot");
        Directory.CreateDirectory(folder);
        _connectionString=new SqliteConnectionStringBuilder { DataSource=Path.Combine(folder,"subscriptions.db") }.ToString();
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS subscriptions (repo TEXT PRIMARY KEY COLLATE NOCASE, exe_path TEXT NOT NULL DEFAULT '', local_version TEXT NOT NULL DEFAULT '', latest_version TEXT NOT NULL DEFAULT '', release_url TEXT NOT NULL DEFAULT '', release_notes TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT '尚未检查', checked_at TEXT NOT NULL DEFAULT '')";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var c=new SqliteConnection(_connectionString);c.Open();return c;}
    public List<Subscription> GetAll()
    {
        var result=new List<Subscription>();
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT repo,exe_path,local_version,latest_version,release_url,release_notes,status,checked_at FROM subscriptions ORDER BY repo";
        using var reader=cmd.ExecuteReader();
        while(reader.Read())result.Add(new Subscription {Repo=reader.GetString(0),ExePath=reader.GetString(1),LocalVersion=reader.GetString(2),LatestVersion=reader.GetString(3),ReleaseUrl=reader.GetString(4),ReleaseNotes=reader.GetString(5),Status=reader.GetString(6),CheckedAt=reader.GetString(7)});
        return result;
    }
    public void Save(Subscription s)
    {
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="INSERT INTO subscriptions(repo,exe_path,local_version,latest_version,release_url,release_notes,status,checked_at) VALUES($repo,$exe,$local,$latest,$url,$notes,$status,$checked) ON CONFLICT(repo) DO UPDATE SET exe_path=excluded.exe_path,local_version=excluded.local_version,latest_version=excluded.latest_version,release_url=excluded.release_url,release_notes=excluded.release_notes,status=excluded.status,checked_at=excluded.checked_at";
        cmd.Parameters.AddWithValue("$repo",s.Repo);cmd.Parameters.AddWithValue("$exe",s.ExePath);cmd.Parameters.AddWithValue("$local",s.LocalVersion);cmd.Parameters.AddWithValue("$latest",s.LatestVersion);cmd.Parameters.AddWithValue("$url",s.ReleaseUrl);cmd.Parameters.AddWithValue("$notes",s.ReleaseNotes);cmd.Parameters.AddWithValue("$status",s.Status);cmd.Parameters.AddWithValue("$checked",s.CheckedAt);
        cmd.ExecuteNonQuery();
    }
    public void Delete(string repo){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM subscriptions WHERE repo=$repo";cmd.Parameters.AddWithValue("$repo",repo);cmd.ExecuteNonQuery();}
}
