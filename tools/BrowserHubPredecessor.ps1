function Assert-BrowserHubPredecessor($disposition,$selection,$listenerPort,[scriptblock]$ReadPinned,[scriptblock]$HoldPinned) {
 $ErrorActionPreference='Stop'
 if($disposition.historicalOutcomesPreserved-ne$true){throw 'Historical outcomes must remain unchanged'}
 if(@($disposition.sourcePins).Count-lt1){throw 'Pinned predecessor evidence required'}
 if($disposition.schema-ceq'questms.browser_hub.postboot_disposition.v1') {
  if($disposition.originalExitCertified-ne$false-or$disposition.originalCleanupCertified-ne$false){throw 'Reboot cannot certify original exit or cleanup'}
  $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime()
  if($boot.ToString('o')-cne$disposition.currentHostBootUtc-or[DateTimeOffset]$disposition.previousHostObservationUtc-ge$boot){throw 'Exact later host reboot required'}
  if(@($disposition.processes).Count-lt1-or@($disposition.previousPorts).Count-lt1){throw 'Exact prior host process and port inventory required'}
  foreach($row in $disposition.processes){
   if([long]$row.birthTicks-ge$boot.Ticks-or[int]$row.pid-le0){throw 'Prior process identity must predate reboot'}
   $live=Get-Process -Id $row.pid -ErrorAction SilentlyContinue
   if($live-and$live.StartTime.ToUniversalTime().Ticks.ToString()-ceq$row.birthTicks){throw 'Original host process still present'}
  }
  if(Get-NetTCPConnection -LocalPort @($disposition.previousPorts) -State Listen -ErrorAction SilentlyContinue){throw 'Prior browser listener or forward port occupied'}
  foreach($source in $selection.Sources){
   $joins=@($disposition.headsetBootChanges|Where-Object {$_.canonical-ceq$source.Canonical})
   if($joins.Count-ne1-or$joins[0].currentBoot-cne$source.Boot-or$joins[0].previousBoot-ceq$source.Boot){throw 'Selected device reboot join unavailable'}
  }
 } elseif($disposition.schema-ceq'questms.browser_hub.normal_retirement.v1') {
  $eof=& $ReadPinned $disposition.hostEof;$retirement=& $ReadPinned $disposition.hostRetirement;$released=& $ReadPinned $disposition.portsReleased
  if($eof.schema-cne'questms.browser_hub.host_eof_wait.v1'-or!$eof.exitKnown-or!$eof.reaped-or!$eof.actualOsExitWaitReturned-or!$eof.stdoutEof-or!$eof.stderrEof){throw 'Actual predecessor host exit and both EOF required'}
  if($retirement.pid-ne$eof.pid-or$retirement.birth-cne$eof.birth-or$retirement.actualExit-ne$eof.actualExit-or!$retirement.reaped-or!$retirement.stdoutEof-or!$retirement.stderrEof-or!$retirement.capturesRetired-or!$retirement.heldInputsVerified){throw 'Exact predecessor owned retirement required'}
  if(@($released.ownFreshLeases).Count-ne3){throw 'Exact predecessor three-port release required'}
  $board=& $selection.PowerShell -NoProfile -File $selection.Board status --json|ConvertFrom-Json -DateKind String
  if($LASTEXITCODE-ne0){throw 'Live predecessor lease verification unavailable'}
  foreach($lease in $released.ownFreshLeases){if($board.active_leases|Where-Object {$_.id-ceq$lease.id}){throw 'Predecessor lease remains active'}}
  $live=Get-Process -Id $eof.pid -ErrorAction SilentlyContinue
  if($live-and$live.StartTime.ToUniversalTime().Ticks.ToString()-ceq$eof.birth){throw 'Retired predecessor process still present'}
 } else {throw 'Unsupported predecessor disposition'}
 # A new scope never claims historical remote cleanup. Its existing Guard checks
 # current devices, daemon, source bytes and new port leases before any effect.
 foreach($pin in $disposition.sourcePins){$null=& $HoldPinned $pin}
}
