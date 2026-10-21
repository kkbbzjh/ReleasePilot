$ErrorActionPreference='Stop'
$name='ReleasePilotMonitor'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
try {
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Scripts/ManageService.ps1 -Operation Install -UserSid $sid -SourceWorker (Join-Path $PWD 'service-payload/ReleasePilot.Monitor.exe')
 if($LASTEXITCODE -ne 0){throw 'Service installation failed'}
 $service=Get-Service $name
 if($service.Status -ne 'Running'){throw 'Native service did not reach Running state'}
 $info=Get-CimInstance Win32_Service -Filter "Name='$name'"
 if($info.StartName -ne 'NT AUTHORITY\LocalService' -or $info.StartMode -ne 'Auto'){throw 'Service must use LocalService and automatic startup'}
 if($info.PathName -ne ('"'+(Join-Path $env:ProgramFiles 'ReleasePilot Service\ReleasePilot.Monitor.exe')+'"')){throw 'Service binary path must retain quotes around its spaces'}
 $process=Get-Process -Id $info.ProcessId
 Write-Host "::notice title=Native service resource check::Native service running: working set $([Math]::Round($process.WorkingSet64/1MB,1)) MB; binary $((Get-Item 'service-payload/ReleasePilot.Monitor.exe').Length) bytes"
 $profile=Join-Path $env:ProgramData "ReleasePilot\Monitor\$sid"
 @{Enabled=$true;Revision='ci';IntervalMinutes=60;Subscriptions=@()} | ConvertTo-Json | Set-Content (Join-Path $profile 'config.json') -Encoding utf8
 $deadline=(Get-Date).AddSeconds(45)
 while(!(Test-Path (Join-Path $profile 'state.json')) -and (Get-Date) -lt $deadline){Start-Sleep -Seconds 1}
 if(!(Test-Path (Join-Path $profile 'state.json'))){throw 'Service did not read its profile under LocalService'}
 Stop-Service $name
 $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))
 Start-Service $name
 (Get-Service $name).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
 & ./Scripts/ManageNotifications.ps1 -UserSid $sid -WorkerPath (Join-Path $env:ProgramFiles 'ReleasePilot Service/ReleasePilot.Monitor.exe')
 $task=Get-ScheduledTask -TaskName ('ReleasePilot Notifications-'+$sid)
 if($task.Principal.RunLevel -ne 'Limited' -or $task.Triggers.Count -ne 2){throw 'Notification task must be short-lived and unelevated'}
 $notifier=Start-Process -FilePath (Join-Path $env:ProgramFiles 'ReleasePilot Service/ReleasePilot.Monitor.exe') -ArgumentList @('--notify',$sid) -PassThru
 if(!$notifier.WaitForExit(15000)){Stop-Process -Id $notifier.Id -Force;throw 'Notification helper stayed resident'}
 if($notifier.ExitCode -ne 0){throw 'No-update notification helper did not exit successfully'}
 Write-Host 'PASS real SCM installation, low-privilege profile access, stop and restart'
} catch {
 Write-Host "::error title=Windows service lifecycle::$($_.Exception.Message)"
 Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match 'ReleasePilot|.NET Runtime|Application Error'} | Select-Object -First 5 | ForEach-Object {Write-Host "::error title=Service event::$($_.Message -replace "`r?`n",'%0A')"}
 exit 1
 } finally {
 & ./Scripts/ManageNotifications.ps1 -UserSid $sid -Remove
 if($service){$service.Dispose()}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Scripts/ManageService.ps1 -Operation Uninstall -UserSid $sid
 $profile=Join-Path $env:ProgramData "ReleasePilot\Monitor\$sid"
 if(Test-Path $profile){Remove-Item $profile -Recurse -Force}
}
