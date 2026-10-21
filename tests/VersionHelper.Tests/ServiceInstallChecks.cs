using System.Text;
using ReleasePilot.Services;
internal static class ServiceInstallChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool value,string description){if(!value)throw new Exception(description);checks++;}
        var script=ServiceInstallProtocol.Bootstrap(@"C:\安装 路径\O'Brien\install.ps1","Install","S-1-5-21-1-2-3-1001",@"C:\测试\worker.exe",@"C:\测试\result.json",@"C:\测试\installer.log");
        Check(script.StartsWith(ServiceInstallProtocol.PowerShellPrelude),"Windows PowerShell module resolution is isolated from an inherited Core module path");
        Check(script.Contains("'C:\\安装 路径\\O''Brien\\install.ps1'"),"bootstrap safely quotes non-ASCII paths with spaces and apostrophes");
        Check(Encoding.Unicode.GetString(Convert.FromBase64String(ServiceInstallProtocol.Encode(script)))==script,"PowerShell encoded command preserves Unicode without system code page dependency");
        var result=ServiceInstallProtocol.Parse("{\"Success\":false,\"Step\":\"StartService\",\"Error\":\"无法启动服务\",\"NativeErrorCode\":1053,\"HResult\":\"80004005\"}");
        Check(result is {Success:false,NativeErrorCode:1053},"structured native service errors deserialize");
        var message=ServiceInstallProtocol.Failure(1,result,@"C:\Logs\service.log");
        Check(message.Contains("启动 Windows 服务")&&message.Contains("1053")&&message.Contains("无法启动服务")&&message.Contains(@"C:\Logs\service.log"),"failure contains actual stage, native code, message and persistent log path");
        Check(ServiceInstallProtocol.Parse("unexpected output")==null,"malformed or missing reports fall back safely");
        Check(ServiceInstallProtocol.Failure(unchecked((int)0xC0000428),null,"log").Contains("0xC0000428"),"blocked or failed process reports hexadecimal Windows exit status");
        try{ServiceInstallProtocol.Bootstrap("script","unknown","sid","exe","report","log");throw new Exception("invalid operation accepted");}catch(ArgumentException){checks++;}
        Console.WriteLine($"PASS {checks} service installer diagnostics and Unicode launch checks");
    }
}
