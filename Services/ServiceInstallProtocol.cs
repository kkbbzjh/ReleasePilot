using System.Text;
using System.Text.Json;
namespace ReleasePilot.Services;
public sealed class ServiceInstallResult
{
    public bool Success {get;set;}
    public string Step {get;set;}="";
    public string Error {get;set;}="";
    public string Details {get;set;}="";
    public int NativeErrorCode {get;set;}
    public string HResult {get;set;}="";
}
public static class ServiceInstallProtocol
{
    public static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
    public static string Bootstrap(string script,string operation,string sid,string worker,string report,string log)
    {
        if(operation is not ("Install" or "Uninstall"))throw new ArgumentException("Invalid service operation.",nameof(operation));
        return "$ErrorActionPreference='Stop';try{& "+Quote(script)+" -Operation "+Quote(operation)+" -UserSid "+Quote(sid)+" -SourceWorker "+Quote(worker)+" -ReportPath "+Quote(report)+" -LogPath "+Quote(log)+";exit $LASTEXITCODE}catch{$failure=$_;$result=@{Success=$false;Step='LaunchScript';Error=$failure.Exception.Message;Details=($failure|Out-String);NativeErrorCode=0;HResult=('{0:X8}' -f $failure.Exception.HResult)};try{[IO.File]::WriteAllText("+Quote(report)+",($result|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false));[IO.File]::AppendAllText("+Quote(log)+",($failure|Out-String),[Text.UTF8Encoding]::new($false))}catch{};exit 1}";
    }
    public static string Encode(string script)=>Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    public static ServiceInstallResult? Parse(string json)
    {
        try{return JsonSerializer.Deserialize<ServiceInstallResult>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}
        catch(JsonException){return null;}
    }
    public static string Failure(int exitCode,ServiceInstallResult? result,string logPath)
    {
        var step=result?.Step switch
        {
            "Validate"=>"检查安装条件", "StopService"=>"停止旧服务", "InstallFiles"=>"复制服务程序", "SetPermissions"=>"设置服务目录权限",
            "CreateService"=>"创建或修复 Windows 服务", "RegisterEventLog"=>"注册服务日志", "StartService"=>"启动 Windows 服务",
            "DeleteService"=>"卸载 Windows 服务", "LaunchScript"=>"启动安装脚本",_=>"启动安装程序"
        };
        var message=$"失败步骤：{step}\n";
        message+=!string.IsNullOrWhiteSpace(result?.Error)?result.Error:"安装程序未返回错误详情。可能被系统策略或安全软件阻止，请查看安装日志。";
        if(result?.NativeErrorCode is >0)message+=$"\nWindows 错误码：{result.NativeErrorCode}";
        if(!string.IsNullOrWhiteSpace(result?.HResult))message+=$"\nHRESULT：0x{result.HResult}";
        message+=$"\n安装退出码：{exitCode}（0x{unchecked((uint)exitCode):X8}）\n日志：{logPath}";
        return message;
    }
}
