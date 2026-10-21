param([Parameter(Mandatory=$true)][string]$UserSid,[string]$WorkerPath,[switch]$Remove)
$ErrorActionPreference='Stop'
if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $UserSid){throw 'Notification task must belong to the current user'}
$taskName='ReleasePilot Notifications-'+$UserSid
if($Remove){if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){Unregister-ScheduledTask -TaskName $taskName -Confirm:$false};exit 0}
$action=New-ScheduledTaskAction -Execute $WorkerPath -Argument ('--notify '+$UserSid)
$triggers=@((New-ScheduledTaskTrigger -AtLogOn -User $UserSid),(New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Minutes 5)))
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
$principal=New-ScheduledTaskPrincipal -UserId $UserSid -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Settings $settings -Principal $principal -Force | Out-Null
