$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReleasePilotWindow {
 [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
}
'@
$app=Start-Process -FilePath (Join-Path $PWD 'publish/ReleasePilot.exe') -PassThru
try {
 $deadline=(Get-Date).AddSeconds(45)
 do {Start-Sleep -Milliseconds 200;$app.Refresh()} while(!$app.HasExited -and $app.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
 if($app.HasExited -or $app.MainWindowHandle -eq 0){throw 'Published desktop EXE did not create its main window'}
 if(![ReleasePilotWindow]::PostMessage($app.MainWindowHandle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)){throw 'Could not close desktop window'}
 if(!$app.WaitForExit(10000)){throw 'Closing the main window left a resident desktop process'}
 if($app.ExitCode -ne 0){throw "Desktop process exited with error $($app.ExitCode)"}
 Write-Host 'PASS published single-file EXE exits completely when its window is closed'
} catch {Write-Host "::error title=Desktop exit check::$($_.Exception.Message)";throw}
finally {if(!$app.HasExited){Stop-Process -Id $app.Id -Force}}
