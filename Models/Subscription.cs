namespace ReleasePilot.Models;
public sealed class Subscription
{
    public string Repo { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string LocalVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string Status { get; set; } = "尚未检查";
    public string ReleaseNotes { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string CheckedAt { get; set; } = "";
}
