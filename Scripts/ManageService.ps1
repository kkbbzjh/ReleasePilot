param(
 [Parameter(Mandatory=$true)][ValidateSet('Install','Uninstall')][string]$Operation,
 [Parameter(Mandatory=$true)][string]$UserSid,
 [string]$SourceWorker
)
$ErrorActionPreference='Stop'
function Assert-NoReparse([string]$Path) {
 $current=[IO.Path]::GetFullPath($Path)
 while($current) {
  if(Test-Path -LiteralPath $current){if(((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Service directories cannot use symbolic links or junctions'}}
  $current=[IO.Path]::GetDirectoryName($current)
 }
}
try {
 $sid=New-Object System.Security.Principal.SecurityIdentifier($UserSid)
 if($UserSid -notmatch '^S-1-(5-21|12-1)-[0-9-]+$'){throw 'Invalid user SID'}
 $serviceName='ReleasePilotMonitor'
 $destination=Join-Path $env:ProgramFiles 'ReleasePilot Service'
 $worker=Join-Path $destination 'ReleasePilot.Monitor.exe'
 Assert-NoReparse $destination
 Assert-NoReparse $worker
 $service=Get-Service $serviceName -ErrorAction SilentlyContinue
 if($Operation -eq 'Uninstall') {
  if($service){Stop-Service $serviceName -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)); & sc.exe delete $serviceName;if($LASTEXITCODE -ne 0){throw 'Cannot remove service'}}
  # Keep subscription/configuration data. Remove only our installed executable.
  if(Test-Path $worker){Remove-Item $worker -Force}
  exit 0
 }
 if(!(Test-Path -LiteralPath $SourceWorker -PathType Leaf)){throw 'Missing monitor executable'}
 if($service){Stop-Service $serviceName -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 New-Item $destination -ItemType Directory -Force | Out-Null
 # The binary directory must be writable only by administrators and SYSTEM.
 $acl=New-Object System.Security.AccessControl.DirectorySecurity
 $acl.SetAccessRuleProtection($true,$false)
 $acl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')) {
  $rule=New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($identity)),'FullControl','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
 }
 foreach($identity in @('S-1-5-19','S-1-5-32-545')) {
  $rule=New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($identity)),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
 }
 Set-Acl $destination $acl
 Copy-Item -LiteralPath $SourceWorker -Destination $worker -Force
 $parent=Join-Path $env:ProgramData 'ReleasePilot'
 $root=Join-Path $parent 'Monitor'
 Assert-NoReparse $root
 New-Item $parent -ItemType Directory -Force | Out-Null
 Set-Acl $parent $acl
 New-Item $root -ItemType Directory -Force | Out-Null
 # Root is read-only for ordinary users; each profile grants only its owner access.
 Set-Acl $root $acl
 $profile=Join-Path $root $UserSid
 Assert-NoReparse $profile
 New-Item $profile -ItemType Directory -Force | Out-Null
 $profileAcl=New-Object System.Security.AccessControl.DirectorySecurity
 $profileAcl.SetAccessRuleProtection($true,$false)
 $profileAcl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')) {
  $rule=New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($identity)),'FullControl','ContainerInherit,ObjectInherit','None','Allow');$profileAcl.AddAccessRule($rule)
 }
 foreach($identity in @('S-1-5-19',$UserSid)) {
  $rule=New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($identity)),'Modify','ContainerInherit,ObjectInherit','None','Allow');$profileAcl.AddAccessRule($rule)
 }
 Set-Acl $profile $profileAcl
 if(!$service){New-Service -Name $serviceName -DisplayName 'ReleasePilot 更新检测服务' -BinaryPathName ('"'+$worker+'"') -StartupType Automatic -Credential (New-Object System.Management.Automation.PSCredential('NT AUTHORITY\LocalService',(New-Object System.Security.SecureString))) | Out-Null}
 else {& sc.exe config $serviceName binPath= ('"'+$worker+'"') start= auto;if($LASTEXITCODE -ne 0){throw 'Cannot configure service'}}
 & sc.exe description $serviceName 'Checks subscribed GitHub releases. Does not install software or run a desktop UI.' | Out-Null
 & sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/300000 | Out-Null
 if(![System.Diagnostics.EventLog]::SourceExists($serviceName)){[System.Diagnostics.EventLog]::CreateEventSource($serviceName,'Application')}
 Start-Service $serviceName
 (Get-Service $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
 exit 0
} catch { Write-Error $_;exit 1 }
