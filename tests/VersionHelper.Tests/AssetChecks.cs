using System.Runtime.InteropServices;
using ReleasePilot.Services;

static class AssetChecks
{
    public static void Run()
    {
        var count=0;
        var cases=new (string Name,Architecture Device,string Platform,string Arch,AssetMatch Match)[] {
            ("app-windows-x64.zip",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app_WIN_AMD64.7Z",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-win-x86_64.zip",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-windows-x86-64.zip",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-windowsx64.zip",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-win64.zip",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-x64.exe",Architecture.X64,"Windows","x64",AssetMatch.Recommended),
            ("app-windows-i686.zip",Architecture.X64,"Windows","x86",AssetMatch.Compatible),
            ("app_windows_386.exe",Architecture.X64,"Windows","x86",AssetMatch.Compatible),
            ("app-win-x86.exe",Architecture.X86,"Windows","x86",AssetMatch.Recommended),
            ("app-win-x64.exe",Architecture.X86,"Windows","x64",AssetMatch.OtherArchitecture),
            ("app-windows-arm64.zip",Architecture.X64,"Windows","ARM64",AssetMatch.OtherArchitecture),
            ("app-windows-arm64.zip",Architecture.Arm64,"Windows","ARM64",AssetMatch.Recommended),
            ("app-win-aarch64.exe",Architecture.Arm64,"Windows","ARM64",AssetMatch.Recommended),
            ("app-windowsarm64.exe",Architecture.Arm64,"Windows","ARM64",AssetMatch.Recommended),
            ("app-win-x64.exe",Architecture.Arm64,"Windows","x64",AssetMatch.OtherArchitecture),
            ("app-windows-armv7.zip",Architecture.Arm,"Windows","ARM32",AssetMatch.Recommended),
            ("app-linux-x64.zip",Architecture.X64,"Linux","x64",AssetMatch.OtherSystem),
            ("app-linux-aarch64.tar.gz",Architecture.Arm64,"Linux","ARM64",AssetMatch.OtherSystem),
            ("app-darwin-arm64.zip",Architecture.Arm64,"macOS","ARM64",AssetMatch.OtherSystem),
            ("app-macos-universal2.zip",Architecture.X64,"macOS","通用架构",AssetMatch.OtherSystem),
            ("app.dmg",Architecture.X64,"macOS","架构未知",AssetMatch.OtherSystem),
            ("app.AppImage",Architecture.X64,"Linux","架构未知",AssetMatch.OtherSystem),
            ("app.apk",Architecture.X64,"Android","架构未知",AssetMatch.OtherSystem),
            ("app-freebsd-amd64.zip",Architecture.X64,"BSD","x64",AssetMatch.OtherSystem),
            ("app-windows-anycpu.zip",Architecture.X64,"Windows","通用架构",AssetMatch.Compatible),
            ("mpv_config-2026.08.12.7z",Architecture.X64,"系统未知","架构未知",AssetMatch.Unknown),
            ("app-x64.zip",Architecture.X64,"系统未知","x64",AssetMatch.Unknown),
            ("setup.exe",Architecture.X64,"Windows","架构未知",AssetMatch.Unknown),
            ("app-windows-x64-arm64.zip",Architecture.X64,"Windows","多架构，请核对",AssetMatch.Unknown),
            ("app-windows-linux-x64.zip",Architecture.X64,"系统标记冲突","x64",AssetMatch.Unknown),
            ("app-linux-x64.exe",Architecture.X64,"系统标记冲突","x64",AssetMatch.Unknown),
            ("mac-address-tool-x64.msi",Architecture.X64,"系统标记冲突","x64",AssetMatch.Unknown),
            ("app-windows-x64-symbols.zip",Architecture.X64,"Windows","x64",AssetMatch.Unknown),
        };
        ReleaseAsset Asset(string name)=>new(name,"https://github.com/owner/app/releases/download/v1/"+name,1024,null);
        void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
        foreach(var (name,device,platform,arch,match) in cases)
        {
            var result=AssetSelector.Describe(Asset(name),device);
            Check(result.Platform==platform && result.Architecture==arch && result.Match==match,$"Wrong asset classification for {name}: {result}");
            if(match==AssetMatch.OtherSystem)Check(!result.CanApply && !result.VisibleByDefault,"other OS must not be applied or shown by default");
        }
        var options=AssetSelector.Order(new[]{"app-linux-x64.zip","app-windows-arm64.zip","setup.exe","app-windows-x64.zip","SHA256SUMS.txt","config.7z","app.dmg"}.Select(Asset),Architecture.X64);
        Check(options.Count==6 && options[0].Match==AssetMatch.Recommended,"native asset first, checksum excluded");
        Check(AssetSelector.DefaultSelection(options,"")?.Asset.Name=="app-windows-x64.zip","automatic choice uses native Windows asset");
        Check(AssetSelector.DefaultSelection(options,"app-linux-x64.zip")?.Asset.Name=="app-windows-x64.zip","old wrong-platform selection cannot override native recommendation");
        Check(AssetSelector.DefaultSelection(options,"config.7z")?.Asset.Name=="config.7z","retain an explicit previous choice of platform-neutral config");
        var unknown=AssetSelector.Order(new[]{Asset("config.7z"),Asset("setup.exe")},Architecture.X64);
        Check(AssetSelector.DefaultSelection(unknown,"") is null,"unknown packages are never silently selected");
        var other=AssetSelector.Order(new[]{Asset("app-windows-arm64.zip"),Asset("app-linux-x64.zip")},Architecture.X64);
        Check(AssetSelector.DefaultSelection(other,"app-windows-arm64.zip") is null,"saved incompatible architecture is not automatically restored");
        Check(AssetSelector.Describe(Asset("app.dmg"),Architecture.X64).Supported==false,"unsupported format is clearly marked");
        Console.WriteLine($"PASS {count} platform, architecture and safe default-selection checks");
    }
}
