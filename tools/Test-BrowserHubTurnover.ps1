param([Parameter(Mandatory)][string]$Application,[Parameter(Mandatory)][string]$Board,[Parameter(Mandatory)][string]$Output,[int]$Port=43140)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BrowserHubPredecessor.ps1')
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Start-BrowserHubStandingSupervisor.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Supervisor parse failed'}
$definition=@($ast.FindAll({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-ceq'CompleteOwnedRaw'},$true))
Invoke-Expression $definition[0].Extent.Text
[IO.Directory]::CreateDirectory($Output)|Out-Null
$pwsh=(Get-Process -Id $PID).Path;$selection=@{PowerShell=$pwsh;Board=$Board;Sources=@()};$leases=@();$runs=@()
function Pin($path){@{path=$path;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
function ReadPinned($pin){if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){throw 'Pinned test input changed'};Get-Content -LiteralPath $pin.path -Raw|ConvertFrom-Json -DateKind String}
function HoldPinned($pin){if((Get-FileHash -LiteralPath $pin.path).Hash.ToLowerInvariant()-cne$pin.sha256){throw 'Pinned test source changed'}}
function WriteReceipt($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 20))}
function ReadLive($path){$f=[IO.File]::Open($path,'Open','Read','ReadWrite');try{$r=[IO.StreamReader]::new($f);$r.ReadToEnd()}finally{$f.Dispose()}}
try {
 foreach($iteration in 1..2){
  foreach($number in $Port..($Port+2)){
   $lease=& $pwsh -NoProfile -File $Board reserve ('port:'+$number) --duration 20m --task 'Browser source regression: real offline host turnover' --json|ConvertFrom-Json -DateKind String
   if($LASTEXITCODE-ne0){throw 'Regression port reservation failed'}
   $leases+=@{id=$lease.lease.id;resource=('port:'+$number)}
  }
  $dir=Join-Path $Output ('host'+$iteration);[IO.Directory]::CreateDirectory($dir)|Out-Null
  $start=[Diagnostics.ProcessStartInfo]::new($Application);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
  foreach($arg in @('--port',[string]$Port,'--host-end','until-operator-stop')){$start.ArgumentList.Add($arg)}
  $outPath=Join-Path $dir 'HOST.stdout';$errPath=Join-Path $dir 'HOST.stderr'
  $stdout=[IO.FileStream]::new($outPath,'CreateNew','Write','Read',1);$stderr=[IO.FileStream]::new($errPath,'CreateNew','Write','Read',1)
  $child=[Diagnostics.Process]::Start($start);$birth=$child.StartTime.ToUniversalTime().Ticks.ToString();$out=$child.StandardOutput.BaseStream.CopyToAsync($stdout);$err=$child.StandardError.BaseStream.CopyToAsync($stderr)
  $url=$null
  while(!$url){if($child.HasExited){throw 'Host exited before startup'};$rawText=ReadLive $outPath;if($rawText-match 'Browser viewer: (http://127\.0\.0\.1:\d+)/#token=([a-f0-9]+)'){$url=$Matches[1];$token=$Matches[2]}else{Start-Sleep -Milliseconds 100}}
  $headers=@{Origin=$url}
  $id=[guid]::NewGuid().ToString('D');$action=@{action='establish';direction='both';generation='offline';requestId=$id;execute=$false}
  $prepared=Invoke-RestMethod -Method Post -Uri ($url+'/api/hub/actions?token='+$token) -Headers $headers -ContentType application/json -Body ($action|ConvertTo-Json -Compress)
  $actionPath=Join-Path $dir 'ACTION.json';WriteReceipt $actionPath $action
  $cliInfo=[Diagnostics.ProcessStartInfo]::new($Application);$cliInfo.UseShellExecute=$false;$cliInfo.CreateNoWindow=$true;$cliInfo.RedirectStandardOutput=$true;$cliInfo.RedirectStandardError=$true
  foreach($arg in @('--hub-action',$actionPath)){$cliInfo.ArgumentList.Add($arg)}
  $cli=[Diagnostics.Process]::Start($cliInfo);$cliOut=$cli.StandardOutput.ReadToEndAsync();$cliErr=$cli.StandardError.ReadToEndAsync();$cli.WaitForExit();$cliValue=$cliOut.GetAwaiter().GetResult()|ConvertFrom-Json
  if($cli.ExitCode-ne0-or$cliErr.GetAwaiter().GetResult()-or($prepared|ConvertTo-Json -Compress)-cne($cliValue|ConvertTo-Json -Compress)){throw 'Actual CLI/HTTP application handler parity failed'}
  $stop=Invoke-RestMethod -Method Post -Uri ($url+'/operator-stop?token='+$token) -Headers $headers
  if($stop.state-cne'requested'-or!$stop.actual_retirement_pending){throw 'Production stop falsely reported completion'}
  $child.WaitForExit();$raw=CompleteOwnedRaw $out $err $stdout $stderr $outPath $errPath
  if($child.ExitCode-ne0){throw 'Actual host stop failed'}
  foreach($path in @($outPath,$errPath)){$probe=[IO.File]::Open($path,'Open','ReadWrite','None');$probe.Dispose()}
  $scope=@{proposalSha256=(Pin $actionPath).sha256;supervisorPid=$PID;supervisorBirth=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString();pid=$child.Id;birth=$birth}
  $eofPath=Join-Path $dir 'HOST_EOF_WAIT.json';WriteReceipt $eofPath ($scope+@{schema='questms.browser_hub.host_eof_wait.v1';actualExit=$child.ExitCode;exitKnown=$true;reaped=$true;actualOsExitWaitReturned=$true;stdoutEof=$true;stderrEof=$true;raw=$raw})
  $retirementPath=Join-Path $dir 'HOST_RETIREMENT.json';WriteReceipt $retirementPath ($scope+@{actualExit=$child.ExitCode;reaped=$true;stdoutEof=$true;stderrEof=$true;capturesRetired=$true;heldInputsVerified=$true;offlineNoCaptures=$true;ownFreshLeases=$leases;heldPins=@((Pin (Join-Path $PSScriptRoot 'Start-BrowserHubStandingSupervisor.ps1')))})
  foreach($lease in $leases){& $pwsh -NoProfile -File $Board release $lease.id --result done|Out-Null;if($LASTEXITCODE-ne0){throw 'Regression release failed'}}
  $releasedPath=Join-Path $dir 'PORTS_RELEASED.json';WriteReceipt $releasedPath ($scope+@{ownFreshLeases=$leases});$leases=@()
  $runs+=@{pid=$child.Id;birth=$birth;actualExit=$child.ExitCode;bothEOF=$true;reaped=$true;cliHttpParity=$true;stopResponsePending=$true;raw=$raw};$child.Dispose();$child=$null;$cli.Dispose()
 }
 WriteReceipt (Join-Path $Output 'QUALIFICATION.json') @{schema='questms.browser_hub.host_turnover_test.v1';runs=$runs;deviceCalls=0;realProductionHost=$true;productionRawCompletion=$true;exclusiveReopen=$true;freshSecondHost=$true;offlineNoCaptureQualification=$true}
 Write-Output 'PASS two actual production hosts: HTTP stop/native0/both EOF/reap/exclusive reopen, fresh host and actual CLI/HTTP parity; no device calls'
} finally {
 if((Get-Variable child -ErrorAction SilentlyContinue)-and$child-and!$child.HasExited){
  if($url){$null=Invoke-RestMethod -Method Post -Uri ($url+'/operator-stop?token='+$token) -Headers @{Origin=$url};$child.WaitForExit();$null=CompleteOwnedRaw $out $err $stdout $stderr $outPath $errPath}
  else{throw 'HELD actual test host; retain its port leases until supported stop'}
 }
 foreach($lease in $leases){& $pwsh -NoProfile -File $Board release $lease.id --result test-failed|Out-Null}
}
