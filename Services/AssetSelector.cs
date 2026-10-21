using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ReleasePilot.Services;

public enum AssetMatch { Recommended,Compatible,Unknown,OtherArchitecture,OtherSystem }
public sealed record AssetOption(ReleaseAsset Asset,string Platform,string Architecture,AssetMatch Match,bool Supported,string Explanation)
{
    public bool CanApply=>Supported && Match!=AssetMatch.OtherSystem;
    public bool VisibleByDefault=>Match is not (AssetMatch.OtherSystem or AssetMatch.OtherArchitecture);
    public string DisplayName=>$"[{(Match==AssetMatch.Recommended?"推荐 · ":Match==AssetMatch.Compatible?"兼容 · ":"")}{Platform} · {Architecture}{(!Supported?" · 暂不支持":"")}] {Asset}";
    public override string ToString()=>DisplayName;
}

public static class AssetSelector
{
    public static string DeviceLabel(Architecture architecture)=>architecture switch {Architecture.X64=>"x64",Architecture.X86=>"x86",Architecture.Arm64=>"ARM64",Architecture.Arm=>"ARM32",_=>architecture.ToString()};
    public static bool IsPackage(string name)=>Regex.IsMatch(name,@"\.(exe|msi|zip|7z|dmg|pkg|deb|rpm|appimage|apk|aab|tgz|tar\.(gz|xz|bz2))$",RegexOptions.IgnoreCase);
    private static bool Has(string name,string pattern)=>Regex.IsMatch(name,@"(?<![a-z0-9])(?:"+pattern+@")(?![a-z0-9])",RegexOptions.IgnoreCase);
    public static AssetOption Describe(ReleaseAsset asset,Architecture device)
    {
        var name=asset.Name.ToLowerInvariant();var extension=Path.GetExtension(name);
        var supported=extension is ".exe" or ".msi" or ".zip" or ".7z";
        var platforms=new List<string>();
        if(Has(name,@"windows|win|win32|win64|win10|win11|mingw32|mingw64|(?:win|windows)(?:x64|x86|arm64|amd64)"))platforms.Add("Windows");
        if(Has(name,"linux|ubuntu|debian|fedora|centos|rhel|musl|appimage") || extension is ".deb" or ".rpm" or ".appimage")platforms.Add("Linux");
        if(Has(name,"mac|macos|osx|darwin|macosx") || extension is ".dmg" or ".pkg")platforms.Add("macOS");
        if(Has(name,"android") || extension is ".apk" or ".aab")platforms.Add("Android");
        if(Has(name,"freebsd|openbsd|netbsd"))platforms.Add("BSD");
        if(extension is ".exe" or ".msi" && !platforms.Contains("Windows"))platforms.Add("Windows");
        var platform=platforms.Count==1?platforms[0]:platforms.Count>1?"系统标记冲突":"系统未知";
        var arches=Regex.Matches(name,@"(?<![a-z0-9])(?:(?<x64>x86[_-]64|amd64|x64|win64)|(?<arm64>aarch64|arm64|armv8)|(?<x86>x86|i[3-6]86|386|ia32|win32)|(?<arm>armv7l?|arm32|armhf))(?![a-z0-9])",RegexOptions.IgnoreCase)
            .Select(match=>match.Groups["x64"].Success?"x64":match.Groups["arm64"].Success?"ARM64":match.Groups["x86"].Success?"x86":"ARM32").ToHashSet();
        // Common concatenated Windows names do not have a separator before the architecture.
        if(Has(name,"(?:win|windows)(?:x64|amd64)"))arches.Add("x64");
        if(Has(name,"(?:win|windows)arm64"))arches.Add("ARM64");
        if(Has(name,"(?:win|windows)x86"))arches.Add("x86");
        var universal=Has(name,"anycpu|noarch|universal|universal2");
        var architecture=arches.Count==1?arches.Single():arches.Count>1?"多架构，请核对":universal?"通用架构":"架构未知";
        var auxiliary=Has(name,"source|src|symbols|debug|pdb");
        AssetMatch match;string explanation;
        if(platforms.Count==1 && platform!="Windows")
        {match=AssetMatch.OtherSystem;explanation=$"文件名表明这是 {platform} 附件，不能用于此 Windows 设备。";}
        else if(platforms.Count!=1 || arches.Count>1 || auxiliary)
        {match=AssetMatch.Unknown;explanation="无法可靠判断适用系统/架构，或可能是源码/调试附件。请先核对发布说明，软件不会自动选择此附件。";}
        else if(arches.Count==1 && architecture==DeviceLabel(device))
        {match=AssetMatch.Recommended;explanation=$"文件名标记为 Windows {architecture}，与当前设备匹配；仍请核对附件用途。";}
        else if((device==Architecture.X64 && architecture=="x86") || (universal && arches.Count==0))
        {match=AssetMatch.Compatible;explanation=architecture=="x86"?"Windows x64 通常可以运行 x86 应用；驱动、插件等需另外核对宿主架构。":"文件名标记为 Windows 通用架构，请核对具体软件要求。";}
        else if(arches.Count==1)
        {match=AssetMatch.OtherArchitecture;explanation=$"附件标记为 {architecture}，当前设备为 {DeviceLabel(device)}。不会默认选择；仅在确认系统支持对应架构或仿真运行后使用。";}
        else
        {match=AssetMatch.Unknown;explanation="架构未标明，请手动核对附件适用系统、架构和用途。此附件不会自动推荐。";}
        if(!supported)explanation+=" 当前更新器仅能处理 EXE、MSI、ZIP 和 7z；请使用「查看 Release」手动处理该格式。";
        return new AssetOption(asset,platform,architecture,match,supported,explanation);
    }
    public static IReadOnlyList<AssetOption> Order(IEnumerable<ReleaseAsset> assets,Architecture device)=>assets.Where(asset=>IsPackage(asset.Name)).Select(asset=>Describe(asset,device)).OrderBy(option=>option.Match).ThenBy(option=>option.Asset.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    public static AssetOption? DefaultSelection(IEnumerable<AssetOption> options,string previousName)
    {
        var available=options.ToArray();
        return available.FirstOrDefault(option=>option.Asset.Name==previousName && option.CanApply && option.VisibleByDefault)
            ??available.FirstOrDefault(option=>option.Match==AssetMatch.Recommended && option.Supported);
    }
}
