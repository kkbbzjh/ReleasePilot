param(
 [Parameter(Mandatory=$true)][ValidateSet('Install','Uninstall')][string]$Operation,
 [Parameter(Mandatory=$true)][string]$UserSid,
 [string]$SourceWorker,
 [string]$ReportPath=(Join-Path $env:TEMP ('ReleasePilot-service-'+[Guid]::NewGuid().ToString('N')+'.json')),
 [string]$LogPath=(Join-Path $env:TEMP ('ReleasePilot-service-'+[Guid]::NewGuid().ToString('N')+'.log'))
)
$ErrorActionPreference='Stop'
$step='Validate'
$serviceName='ReleasePilotMonitor'
$service=$null
function Write-InstallLog([string]$Text) {
 try {[IO.File]::AppendAllText($LogPath,([DateTimeOffset]::Now.ToString('o')+' '+$Text+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))}catch{}
}
function Set-Step([string]$Name) {$script:step=$Name;Write-InstallLog ('STEP '+$Name)}
function Save-Result($Result) {
 [IO.File]::WriteAllText($ReportPath,($Result|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
}
function Assert-NoReparse([string]$Path) {
 $current=[IO.Path]::GetFullPath($Path)
 while($current) {
  if(Test-Path -LiteralPath $current){if(((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Service directory uses a symbolic link or junction: '+$current)}}
  $current=[IO.Path]::GetDirectoryName($current)
 }
}
function Invoke-ServiceCommand([string[]]$Arguments) {
 # Windows PowerShell 5.1 otherwise drops embedded quotes in service executable paths.
 $quoted=@($Arguments|ForEach-Object {$escaped=[regex]::Replace($_,'(\\*)"','$1$1\"');$escaped=[regex]::Replace($escaped,'(\\+)$','$1$1');'"'+$escaped+'"'})
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=Join-Path $env:SystemRoot 'System32\sc.exe'
 $start.Arguments=[string]::Join(' ',$quoted)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $process=[Diagnostics.Process]::Start($start)
 try {
  $output=$process.StandardOutput.ReadToEndAsync();$errorText=$process.StandardError.ReadToEndAsync()
  $process.WaitForExit();$code=$process.ExitCode
  Write-InstallLog ($output.GetAwaiter().GetResult()+$errorText.GetAwaiter().GetResult())
  if($code -ne 0){throw [ComponentModel.Win32Exception]::new($code)}
 }finally{$process.Dispose()}
}
function Stop-ExistingService {
 if(!$script:service){return}
 Set-Step 'StopService'
 $info=Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
 $oldProcessId=[int]$info.ProcessId
 $script:service.Refresh()
 if($script:service.Status -ne 'Stopped'){Stop-Service $serviceName -Force;$script:service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 # STOPPED can be reported before the executable is unlocked. Wait for that process too.
 if($oldProcessId -gt 0){$process=Get-Process -Id $oldProcessId -ErrorAction SilentlyContinue;if($process){try{if(!$process.WaitForExit(30000)){throw 'The old service process has not exited; close it before retrying.'}}finally{$process.Dispose()}}}
}
try {
 Write-InstallLog ('Operation='+$Operation+'; OS='+[Environment]::OSVersion.VersionString+'; PowerShell='+$PSVersionTable.PSVersion+'; UserSid='+$UserSid)
 if(!(New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator authorization is required to install or remove this service.'}
 $sid=New-Object Security.Principal.SecurityIdentifier($UserSid)
 if($UserSid -notmatch '^S-1-(5-21|12-1)-[0-9-]+$'){throw ('Unsupported user SID: '+$UserSid)}
 $destination=Join-Path $env:ProgramFiles 'ReleasePilot Service'
 $worker=Join-Path $destination 'ReleasePilot.Monitor.exe'
 Assert-NoReparse $destination;Assert-NoReparse $worker
 if($Operation -eq 'Install' -and !(Test-Path -LiteralPath $SourceWorker -PathType Leaf)){Set-Step 'InstallFiles';throw ('Missing service executable: '+$SourceWorker)}
 $service=Get-Service $serviceName -ErrorAction SilentlyContinue
 Stop-ExistingService
 if($Operation -eq 'Uninstall') {
  Set-Step 'DeleteService'
  if($service){$service.Dispose();$service=$null;Invoke-ServiceCommand @('delete',$serviceName)}
  if(Test-Path -LiteralPath $worker){Remove-Item -LiteralPath $worker -Force}
  Save-Result @{Success=$true;Step=$step;Error='';NativeErrorCode=0}
  Write-InstallLog 'SUCCESS';exit 0
 }
 Set-Step 'InstallFiles'
 if(!(Test-Path -LiteralPath $SourceWorker -PathType Leaf)){throw ('Missing service executable: '+$SourceWorker)}
 New-Item $destination -ItemType Directory -Force | Out-Null
 Set-Step 'SetPermissions'
 $acl=New-Object Security.AccessControl.DirectorySecurity
 $acl.SetAccessRuleProtection($true,$false)
 $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')) {
  $rule=New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($identity)),'FullControl','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
 }
 foreach($identity in @('S-1-5-19','S-1-5-32-545')) {
  $rule=New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($identity)),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
 }
 Set-Acl $destination $acl
 Set-Step 'InstallFiles'
 Copy-Item -LiteralPath $SourceWorker -Destination $worker -Force
 Write-InstallLog ('Worker SHA256='+((Get-FileHash -LiteralPath $worker -Algorithm SHA256).Hash))
 Set-Step 'SetPermissions'
 $parent=Join-Path $env:ProgramData 'ReleasePilot'
 $root=Join-Path $parent 'Monitor'
 Assert-NoReparse $root
 New-Item $parent -ItemType Directory -Force | Out-Null;Set-Acl $parent $acl
 New-Item $root -ItemType Directory -Force | Out-Null;Set-Acl $root $acl
 $profile=Join-Path $root $UserSid
 Assert-NoReparse $profile
 New-Item $profile -ItemType Directory -Force | Out-Null
 $profileAcl=New-Object Security.AccessControl.DirectorySecurity
 $profileAcl.SetAccessRuleProtection($true,$false)
 $profileAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')) {
  $rule=New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($identity)),'FullControl','ContainerInherit,ObjectInherit','None','Allow');$profileAcl.AddAccessRule($rule)
 }
 foreach($identity in @('S-1-5-19',$UserSid)) {
  $rule=New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($identity)),'Modify','ContainerInherit,ObjectInherit','None','Allow');$profileAcl.AddAccessRule($rule)
 }
 Set-Acl $profile $profileAcl
 Set-Step 'CreateService'
 if(!$service){Invoke-ServiceCommand @('create',$serviceName,'binPath=',('"'+$worker+'"'),'start=','auto','obj=','NT AUTHORITY\LocalService','DisplayName=','ReleasePilot Update Monitor')}
 else {Invoke-ServiceCommand @('config',$serviceName,'binPath=',('"'+$worker+'"'),'start=','auto','obj=','NT AUTHORITY\LocalService')}
 Invoke-ServiceCommand @('description',$serviceName,'Checks subscribed GitHub releases. Does not install software or run a desktop UI.')
 Invoke-ServiceCommand @('failure',$serviceName,'reset=','86400','actions=','restart/60000/restart/300000')
 Set-Step 'RegisterEventLog'
 if(![Diagnostics.EventLog]::SourceExists($serviceName)){[Diagnostics.EventLog]::CreateEventSource($serviceName,'Application')}
 Set-Step 'StartService'
 Start-Service $serviceName
 $started=Get-Service $serviceName
 try{$started.WaitForStatus('Running',[TimeSpan]::FromSeconds(30))}finally{$started.Dispose()}
 Save-Result @{Success=$true;Step=$step;Error='';NativeErrorCode=0}
 Write-InstallLog 'SUCCESS'
 exit 0
} catch {
 $failure=$_;$native=0;$exception=$failure.Exception
 for($i=0;$exception -and $i -lt 12;$i++) {if($exception -is [ComponentModel.Win32Exception]){$native=$exception.NativeErrorCode;break};$exception=$exception.InnerException}
 Write-InstallLog ('FAILED at '+$step+': '+($failure|Out-String))
 try {
  foreach($logName in @('System','Application')) {
   Get-WinEvent -FilterHashtable @{LogName=$logName;StartTime=(Get-Date).AddMinutes(-5)} -MaxEvents 200 -ErrorAction SilentlyContinue |
    Where-Object {$_.Message -like '*ReleasePilot*'} | Select-Object -First 5 | ForEach-Object {Write-InstallLog ('EVENT '+$_.Id+': '+$_.Message)}
  }
 }catch{}
 try{Save-Result @{Success=$false;Step=$step;Error=$failure.Exception.Message;Details=($failure|Out-String);NativeErrorCode=$native;HResult=('{0:X8}' -f $failure.Exception.HResult)}}catch{Write-InstallLog ('Cannot write result: '+$_)}
 [Console]::Error.WriteLine($failure.Exception.Message)
 exit 1
} finally {if($service){$service.Dispose()}}
