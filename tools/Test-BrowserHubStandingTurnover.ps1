param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$Board,[Parameter(Mandatory)][string]$PostbootDisposition,[Parameter(Mandatory)][string[]]$Guards,[Parameter(Mandatory)][string]$Output,[int]$Port=43140)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BrowserHubPredecessor.ps1')
$pwsh=(Get-Process -Id $PID).Path;$supervisor=Join-Path $PSScriptRoot 'Start-BrowserHubStandingSupervisor.ps1';$helper=Join-Path $PSScriptRoot 'BrowserHubPredecessor.ps1'
[IO.Directory]::CreateDirectory($Output)|Out-Null
function Pin($path){@{path=[IO.Path]::GetFullPath($path);sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
function Put($path,$value){$f=[IO.File]::Open($path,'CreateNew');try{$bytes=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 30));$f.Write($bytes)}finally{$f.Dispose()}}
function ReadPinned($pin){if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){throw 'Input drift'};Get-Content -LiteralPath $pin.path -Raw|ConvertFrom-Json -DateKind String}
function HoldPinned($pin){if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){throw 'Source drift'}}
$previous=Pin $PostbootDisposition;$runs=@();$leases=@();$completed=$true
foreach($iteration in 1..2){
 $dir=Join-Path $Output ('scope'+$iteration);[IO.Directory]::CreateDirectory($dir)|Out-Null
 $evidence=Join-Path $dir 'captures';[IO.Directory]::CreateDirectory($evidence)|Out-Null
 # Intentionally invalid device configuration makes the actual browser reject
 # before any platform command. The real supervisor must own that failed child.
 $sources=@();$index=0;foreach($path in $Guards){$g=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -DateKind String;$index++;$sources+=@{Id=('test'+$index);Label='Host-only rejection control';Serial=$g.transport;Canonical=$g.canonical;Boot=$g.boot_id;LeaseId=$g.lease_id;ForwardPort=($Port+$index)}}
 if($sources.Count-ne2){throw 'Two guarded source identities required'}
 $config=@{Enabled=$true;CaptureLifetime='until-operator-stop';DeadlineUtc=$null;Sources=$sources;EvidenceDirectory=$evidence;DaemonPid=$PID;DaemonStartUtcTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks;Adb=$pwsh;AdbSha256=(Pin $pwsh).sha256;Server=$helper;ServerSha256=(Pin $helper).sha256;Board=$Board;BoardSha256=(Pin $Board).sha256;PowerShell=$pwsh;PowerShellSha256=(Pin $pwsh).sha256}
 $selectionPath=Join-Path $dir 'SELECTION.json';Put $selectionPath $config
 foreach($number in $Port..($Port+2)){$v=& $pwsh -NoProfile -File $Board reserve ('port:'+$number) --duration 20m --task 'Actual browser supervisor failed-child turnover regression' --json|ConvertFrom-Json -DateKind String;if($LASTEXITCODE-ne0){throw 'Port lease failed'};$leases+=@{id=$v.lease.id;resource=('port:'+$number)}}
 $proposal=@{schema='questms.browser_hub.standing.v1';approved=$true;self=(Pin $supervisor);selection=(Pin $selectionPath);sourcePins=@((Pin $supervisor),(Pin $helper));artifactPins=@((Pin $Application));watchPins=@();portLeases=$leases;previousDisposition=$previous;port=$Port;executable=$Application;workingDirectory=[IO.Path]::GetDirectoryName($Application);outputDirectory=$dir;hubOwner=$null;hubBridge=$null;hubEvidence=$null}
 $proposalPath=Join-Path $dir 'PROPOSAL.json';Put $proposalPath $proposal
 $start=[Diagnostics.ProcessStartInfo]::new($pwsh);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($arg in @('-NoProfile','-File',$supervisor,'-ProposalPath',$proposalPath,'-ExpectedProposalSha',(Pin $proposalPath).sha256)){$start.ArgumentList.Add($arg)}
 $completed=$false;$child=[Diagnostics.Process]::Start($start);$out=$child.StandardOutput.ReadToEndAsync();$err=$child.StandardError.ReadToEndAsync();$child.WaitForExit();$stdout=$out.GetAwaiter().GetResult();$stderr=$err.GetAwaiter().GetResult()
 Put (Join-Path $dir 'SUPERVISOR_RESULT.json') @{pid=$child.Id;birth=$child.StartTime.ToUniversalTime().Ticks.ToString();actualExit=$child.ExitCode;stdout=$stdout;stderr=$stderr;bothEOF=$true;reaped=$true}
 if($child.ExitCode-ne0){throw 'Actual supervisor failed'}
 $releasedPath=Join-Path $dir 'PORTS_RELEASED.json';if(!(Test-Path $releasedPath)){throw 'Actual supervisor did not release its owned ports'};$completed=$true;$leases=@()
 $disposition=@{schema='questms.browser_hub.normal_retirement.v1';historicalOutcomesPreserved=$true;hostEof=(Pin (Join-Path $dir 'HOST_EOF_WAIT.json'));hostRetirement=(Pin (Join-Path $dir 'HOST_RETIREMENT.json'));portsReleased=(Pin $releasedPath);sourcePins=@((Pin $supervisor),(Pin $helper))}
 Assert-BrowserHubPredecessor $disposition $config $Port ${function:ReadPinned} ${function:HoldPinned}
 $eof=ReadPinned $disposition.hostEof
 if($eof.actualExit-eq0){throw 'Invalid selection unexpectedly admitted by real host'}
 $released=ReadPinned $disposition.portsReleased;$released.proposalSha256='0'*64;$mixedPath=Join-Path $dir 'MIXED_RELEASE.json';Put $mixedPath $released;$bad=$disposition.Clone();$bad.portsReleased=Pin $mixedPath;$denied=$false
 try{Assert-BrowserHubPredecessor $bad $config $Port ${function:ReadPinned} ${function:HoldPinned}}catch{$denied=$true};if(!$denied){throw 'Mixed predecessor scope accepted'}
 $released=ReadPinned $disposition.portsReleased;$released.ownFreshLeases[0].id=[guid]::NewGuid().ToString('D');$wrongLease=Join-Path $dir 'WRONG_LEASE.json';Put $wrongLease $released;$bad=$disposition.Clone();$bad.portsReleased=Pin $wrongLease;$denied=$false
 try{Assert-BrowserHubPredecessor $bad $config $Port ${function:ReadPinned} ${function:HoldPinned}}catch{$denied=$true};if(!$denied){throw 'Unjoined lease accepted'}
 $bad=$disposition.Clone();$bad.sourcePins=@((Pin $Application));$denied=$false
 # The executable is held too: test an actually unheld source instead.
 $bad.sourcePins=@((Pin $wrongLease));try{Assert-BrowserHubPredecessor $bad $config $Port ${function:ReadPinned} ${function:HoldPinned}}catch{$denied=$true};if(!$denied){throw 'Unheld predecessor source accepted'}
 $nextPath=Join-Path $dir 'NEXT_PREDECESSOR.json';Put $nextPath $disposition;$previous=Pin $nextPath
 $runs+=@{supervisorPid=$child.Id;supervisorExit=$child.ExitCode;hostPid=$eof.pid;hostExit=$eof.actualExit;bothEOF=$true;ownedPortsReleased=$true;exactNormalSuccessorAccepted=$true;mixedScopeDenied=$true};$child.Dispose()
}
Put (Join-Path $Output 'QUALIFICATION.json') @{schema='questms.browser_hub.actual_supervisor_test.v1';runs=$runs;productionSupervisorSource=(Pin $supervisor);productionHostApplication=(Pin $Application);deviceCalls=0;childRejectedBeforePlatformEffects=$true;fullSupervisorExecuted=$true;physicalCaptureTurnoverQualified=$false}
Write-Output 'PASS two actual standing supervisors: real host rejection, actual child exit/EOF, exact emitted receipt joins, live owned port release and successor launch; mixed-scope denial; no device effects'
