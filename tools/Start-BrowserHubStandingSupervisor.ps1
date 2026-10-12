param([Parameter(Mandatory)][string]$ProposalPath,[Parameter(Mandatory)][string]$ExpectedProposalSha)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$held=[Collections.Generic.List[IO.FileStream]]::new();$pins=[Collections.Generic.List[object]]::new();$child=$null;$retired=$false;$pulse=0
function Hold($pin){$held.Add([IO.File]::Open($pin.path,'Open','Read','Read'));if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){throw 'Selected standing input changed'};$pins.Add($pin)}
function Json($pin){Hold $pin;Get-Content -LiteralPath $pin.path -Raw|ConvertFrom-Json -Depth 50 -DateKind String}
function New($name,$value){$f=[IO.File]::Open((Join-Path $proposal.outputDirectory $name),'CreateNew');try{$b=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 40));$f.Write($b)}finally{$f.Dispose()}}
function CompleteOwnedRaw($outTask,$errTask,$outStream,$errStream,$outPath,$errPath) {
 $null=$outTask.GetAwaiter().GetResult();$null=$errTask.GetAwaiter().GetResult();$outStream.Flush();$errStream.Flush()
 $outBytes=$outStream.Length;$errBytes=$errStream.Length
 # Only after genuine pipe EOF and flush may our writable raw handles close.
 $outStream.Dispose();$errStream.Dispose()
 return @{stdoutBytes=$outBytes;stderrBytes=$errBytes;stdoutSha256=(Get-FileHash -LiteralPath $outPath).Hash.ToLowerInvariant();stderrSha256=(Get-FileHash -LiteralPath $errPath).Hash.ToLowerInvariant()}
}
function Pulse {
 $script:pulse++;$results=@()
 foreach($lease in $proposal.portLeases){$out=@(& $config.PowerShell -NoProfile -File $config.Board heartbeat $lease.id --duration 3h);$results+=@{id=$lease.id;exit=$LASTEXITCODE;output=$out}}
 try{New ('HEARTBEAT_'+$script:pulse+'.json') @{supervisorPid=$PID;supervisorBirth=$birth;hostPid=if($child){$child.Id}else{$null};leases=$results;utc=[DateTimeOffset]::UtcNow.ToString('o')}}catch{Write-Warning 'Heartbeat receipt failed; ownership retained'}
}
function Held($reason){try{New ('HELD_'+[guid]::NewGuid().ToString('N')+'.json') @{reason=$reason;supervisorPid=$PID;hostPid=if($child){$child.Id}else{$null};utc=[DateTimeOffset]::UtcNow.ToString('o')}}catch{};while($true){Pulse;[Threading.Thread]::Sleep(30000)}}
function Guard {
 $board=& $config.PowerShell -NoProfile -File $config.Board status --json|ConvertFrom-Json -DateKind String;if($LASTEXITCODE-ne0){throw 'Board status unavailable'}
 foreach($lease in $proposal.portLeases){if(!($board.active_leases|Where-Object {$_.id-ceq$lease.id-and$_.resource-ceq$lease.resource-and$_.status-ceq'active'-and[DateTimeOffset]$_.lease_until-gt[DateTimeOffset]::UtcNow})){throw 'Exact fresh port lease absent'}}
 foreach($source in $config.Sources){if(!($board.active_leases|Where-Object {$_.id-ceq$source.LeaseId-and$_.resource-ceq('quest:'+$source.Canonical)-and$_.status-ceq'active'-and[DateTimeOffset]$_.lease_until-gt[DateTimeOffset]::UtcNow})){throw 'Selected current headset lease absent'}}
 foreach($pin in $proposal.watchPins){$watch=Get-Content -LiteralPath $pin.path -Raw|ConvertFrom-Json -DateKind String;$state=Get-Content -LiteralPath (Join-Path (Split-Path $pin.path) 'STATE.json') -Raw|ConvertFrom-Json -DateKind String;$s=@($config.Sources|Where-Object {$_.Canonical-ceq$watch.canonical});if($s.Count-ne1-or$s[0].Boot-cne$watch.boot_id-or$s[0].LeaseId-cne$watch.lease_id-or$watch.daemon_birth_ticks-ne$config.DaemonStartUtcTicks-or$state.phase-cne'watching'-or$state.detail.sensor_lock-or$state.detail.wakefulness-cne'Awake'-or([DateTimeOffset]::UtcNow-[DateTimeOffset]$state.utc).TotalSeconds-gt120){throw 'Current selected watchdog identity/readback unavailable'};$p=Get-Process -Id $state.pid;if($p.StartTime.ToUniversalTime().Ticks-ne$state.birth_ticks){throw 'Watchdog birth changed'}}
 $daemon=Get-Process -Id $config.DaemonPid;if($daemon.StartTime.ToUniversalTime().Ticks-ne$config.DaemonStartUtcTicks){throw 'ADB daemon identity changed'}
}
function CapturesRetired {
 foreach($intent in Get-ChildItem -LiteralPath $config.EvidenceDirectory -Filter '*.001.intent.json'){
  $stem=$intent.Name.Replace('.001.intent.json','');$files=@(Get-ChildItem -LiteralPath $config.EvidenceDirectory -Filter ($stem+'*'))
  if(!($files|Where-Object {$_.Name-match 'terminal.json$'})){return $false}
  if($files|Where-Object {$_.Name-match 'server-started.json$'}){$r=@($files|Where-Object {$_.Name-match 'server-retired.json$'});if($r.Count-ne1){return $false};$v=(Get-Content -LiteralPath $r[0].FullName -Raw|ConvertFrom-Json -DateKind String).detail;if(!$v.reaped-or!$v.bothEOF-or!$v.stdout.Eof-or!$v.stderr.Eof){return $false}}
  if(($files|Where-Object {$_.Name-match 'forward-intent.json$'})-and!($files|Where-Object {$_.Name-match 'forward-removed.json$'})){return $false}
  if(($files|Where-Object {$_.Name-match 'push-intent.json$'})-and!($files|Where-Object {$_.Name-match 'jar-removed.json$'})){return $false}
 }
 return $true
}

# Pure admission checks are shared with real-process regression controls.
. (Join-Path $PSScriptRoot 'BrowserHubPredecessor.ps1')

try {
 $proposal=Json @{path=$ProposalPath;sha256=$ExpectedProposalSha};if($proposal.schema-cne'questms.browser_hub.standing.v1'-or$proposal.approved-ne$true){throw 'Root-selected standing scope required'}
 Hold $proposal.self;if($proposal.self.path-cne$PSCommandPath){throw 'Selected supervisor source differs'}
 $config=Json $proposal.selection;foreach($pin in @($proposal.sourcePins)+@($proposal.artifactPins)+@($proposal.watchPins)){Hold $pin}
 if($config.CaptureLifetime-cne'until-operator-stop'-or$null-ne$config.DeadlineUtc-or$proposal.portLeases.Count-ne3){throw 'Explicit until-stop / three-port scope required'}
 $disposition=Json $proposal.previousDisposition
 if($disposition.schema -in @('questms.browser_hub.postboot_disposition.v1','questms.browser_hub.normal_retirement.v1')) {
  if(!(@($proposal.sourcePins)|Where-Object {$_.path-ceq(Join-Path $PSScriptRoot 'BrowserHubPredecessor.ps1')})){throw 'Predecessor admission source must be pinned'}
  Assert-BrowserHubPredecessor $disposition $config $proposal.port ${function:Json} ${function:Hold}
 } else {
 if($disposition.schema-cne'questms.browser_hub.retained_supervisor_disposition.v1'-or!$disposition.hostOsExitActuallyWaited-or$disposition.supervisorRetired-ne$false-or$disposition.oldSourceHandlesReleased-ne$false-or$disposition.oldLeasesReleased-ne$false-or!$disposition.historicalFailurePreserved-or!$disposition.noAppRestart-or!$disposition.noWakeChanges){throw 'Previous quiescent retained scope disposition unavailable'}
 $witness=Json $disposition.hostPipeEofOrderedWitness;Hold $disposition.oldSupervisorSource
 if($witness.host_pid-ne$disposition.hostPid-or$witness.supervisor_pid-ne$disposition.supervisorPid-or$witness.reason-cne'New capture EOF/cleanup missing after host exit; no automatic process kill/replay'){throw 'Original ordered host EOF witness differs'}
 if(Get-Process -Id $disposition.hostPid -ErrorAction SilentlyContinue){throw 'Original host is still present; no competing scope'}
 $oldSupervisor=Get-Process -Id $disposition.supervisorPid;if($oldSupervisor.StartTime.ToUniversalTime().Ticks.ToString()-cne$disposition.supervisorBirth){throw 'Retained original supervisor identity changed'}
 foreach($row in $disposition.captures){$intent=Json $row.intent;$started=Json $row.started;$r=Json $row.serverRetired;if($r.stage-cne'server-retired'-or!$r.detail.reaped-or!$r.detail.bothEOF-or!$r.detail.stdout.Eof-or!$r.detail.stderr.Eof){throw 'Original native capture remains unresolved'};foreach($proof in @($row.remoteProcessProof,$row.literalForwardProof,$row.literalJarProof)){ $v=Json $proof;if(!$v.exitKnown-or!$v.reaped-or!$v.stdout.Eof-or!$v.stderr.Eof-or$v.stdout.Error-or$v.stderr.Error-or$v.stdout.Truncated-or$v.stderr.Truncated){throw 'Original remote probe EOF remains unresolved'};Hold @{path=($proof.path-replace '\.native\.json$','.stdout');sha256=$v.stdout.Sha256};Hold @{path=($proof.path-replace '\.native\.json$','.stderr');sha256=$v.stderr.Sha256}}
  $p=Get-Content -LiteralPath $row.remoteProcessProof.path -Raw|ConvertFrom-Json;$f=Get-Content -LiteralPath $row.literalForwardProof.path -Raw|ConvertFrom-Json;$j=Get-Content -LiteralPath $row.literalJarProof.path -Raw|ConvertFrom-Json
  $ps=[IO.File]::ReadAllText(($row.remoteProcessProof.path-replace '\.native\.json$','.stdout'));$maps=[IO.File]::ReadAllText(($row.literalForwardProof.path-replace '\.native\.json$','.stdout'))
  if($p.exit-ne0-or$j.exit-ne1-or$f.exit-ne0-or$ps-cnotmatch'USER\s+PID\s+(ARGS|COMMAND)'-or($ps-split"`n"|Where-Object {$_-match'com\.genymobile\.scrcpy\.Server'-and$_-match('(?:^|\s)scid='+$row.scid+'(?:\s|$)')})-or($maps-split"`n"|Where-Object {$a=$_.Trim()-split'\s+';$a.Count-eq3-and$a[0]-ceq$row.source.Serial-and$a[1]-ceq('tcp:'+$row.source.ForwardPort)})){throw 'Exact original remote process/forward/jar absence differs'}
 }
 foreach($source in $config.Sources){if($source.ForwardPort-in@(43119,43120)){throw 'Retained original forward ports cannot be reused'}}
 if($proposal.port-in@(43118,43119,43120)-or$proposal.portLeases.resource-notcontains('port:'+$proposal.port)){throw 'Distinct selected listener port required'}
 }
 if($proposal.portLeases.resource-notcontains('port:'+$proposal.port)){throw 'Selected listener lease required'}
 Guard
 if(Get-NetTCPConnection -LocalPort (@($proposal.port)+@($config.Sources.ForwardPort)) -State Listen -ErrorAction SilentlyContinue){throw 'Selected new ports are occupied'}
 foreach($pin in @(@{path=$config.Adb;sha256=$config.AdbSha256},@{path=$config.Server;sha256=$config.ServerSha256},@{path=$config.Board;sha256=$config.BoardSha256},@{path=$config.PowerShell;sha256=$config.PowerShellSha256})){Hold $pin}
 $birth=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString();Pulse
 $args=@('--selection',$proposal.selection.path,'--host-end','until-operator-stop','--port',[string]$proposal.port)
 if($proposal.hubOwner){Hold $proposal.hubOwner;$args+=@('--hub-owner',$proposal.hubOwner.path)}
 if($proposal.hubBridge){Hold $proposal.hubBridge;$args+=@('--hub-bridge',$proposal.hubBridge.path)}
 if($proposal.hubEvidence){Hold $proposal.hubEvidence;$args+=@('--hub-evidence',$proposal.hubEvidence.path)}
 $start=[Diagnostics.ProcessStartInfo]::new($proposal.executable);$start.WorkingDirectory=$proposal.workingDirectory;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;foreach($arg in $args){$start.ArgumentList.Add($arg)}
 $stdout=[IO.FileStream]::new((Join-Path $proposal.outputDirectory 'HOST.stdout'),'CreateNew','Write','Read',1);$stderr=[IO.FileStream]::new((Join-Path $proposal.outputDirectory 'HOST.stderr'),'CreateNew','Write','Read',1)
 $child=[Diagnostics.Process]::Start($start);$childBirth=$child.StartTime.ToUniversalTime().Ticks.ToString();$out=$child.StandardOutput.BaseStream.CopyToAsync($stdout);$err=$child.StandardError.BaseStream.CopyToAsync($stderr)
 New 'HOST_LAUNCH.json' @{pid=$child.Id;birth=$child.StartTime.ToUniversalTime().Ticks.ToString();supervisorPid=$PID;supervisorBirth=$birth;arguments=@($start.ArgumentList);previousSupervisorRetired=$false;previousLeasesReleased=$false;noAppRestart=$true;noWakeChanges=$true;utc=[DateTimeOffset]::UtcNow.ToString('o')}
 $next=[DateTimeOffset]::UtcNow.AddMinutes(10);while(!$child.WaitForExit(1000)){if([DateTimeOffset]::UtcNow-ge$next){Pulse;$next=[DateTimeOffset]::UtcNow.AddMinutes(10)}}
 $raw=CompleteOwnedRaw $out $err $stdout $stderr (Join-Path $proposal.outputDirectory 'HOST.stdout') (Join-Path $proposal.outputDirectory 'HOST.stderr')
 New 'HOST_EOF_WAIT.json' @{schema='questms.browser_hub.host_eof_wait.v1';pid=$child.Id;birth=$childBirth;actualExit=$child.ExitCode;exitKnown=$true;reaped=$true;actualOsExitWaitReturned=$true;stdoutEof=$true;stderrEof=$true;stdoutBytes=$raw.stdoutBytes;stderrBytes=$raw.stderrBytes;stdoutSha256=$raw.stdoutSha256;stderrSha256=$raw.stderrSha256;capturesRetired=$false;leasesReleased=$false;utc=[DateTimeOffset]::UtcNow.ToString('o')}
 if(!(CapturesRetired)){Held 'Actual new capture retirement/cleanup remains unresolved; HOST_EOF_WAIT preserves genuine host termination'}
 foreach($pin in $pins){if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){Held 'Held source/input changed'}}
 New 'HOST_RETIREMENT.json' @{pid=$child.Id;birth=$childBirth;actualExit=$child.ExitCode;reaped=$true;stdoutEof=$true;stderrEof=$true;capturesRetired=$true;heldInputsVerified=$true;historicalSupervisorRetired=$false;historicalLeasesReleased=$false}
 foreach($lease in $proposal.portLeases){& $config.PowerShell -NoProfile -File $config.Board release $lease.id --result done|Out-Null;if($LASTEXITCODE-ne0){Held 'Own fresh lease release unresolved'}}
 New 'PORTS_RELEASED.json' @{ownFreshLeases=$proposal.portLeases;historicalLeasesReleased=$false};$retired=$true
}catch{if($child){Held ('Standing owner retained: '+$_.Exception.Message)};throw}finally{if($child-and!$retired){Held 'Same new host/capture source and port ownership retained'};if(Get-Variable stdout -ErrorAction SilentlyContinue){$stdout.Dispose()};if(Get-Variable stderr -ErrorAction SilentlyContinue){$stderr.Dispose()};foreach($f in $held){$f.Dispose()}}
