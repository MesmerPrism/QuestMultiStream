[CmdletBinding()]
param([Parameter(Mandatory)][string]$Application)
$ErrorActionPreference='Stop'
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('questms-browser-argv-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch)|Out-Null
$echo=Join-Path $scratch 'echo.ps1'
[IO.File]::WriteAllText($echo,'[Environment]::GetCommandLineArgs() | ConvertTo-Json -Compress')
function Invoke-Owned($exe,$arguments){
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.CreateNoWindow=$true
 foreach($arg in $arguments){$info.ArgumentList.Add($arg)}
 $child=[Diagnostics.Process]::Start($info);$out=$child.StandardOutput.ReadToEndAsync();$err=$child.StandardError.ReadToEndAsync();$child.WaitForExit()
 @{exit=$child.ExitCode;stdout=$out.GetAwaiter().GetResult();stderr=$err.GetAwaiter().GetResult()}
}
$culture=[Threading.Thread]::CurrentThread.CurrentCulture
try{
 [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]'de-DE'
 $plan='{"arguments":["--host-end","2030-10-12T01:32:12Z"]}'|ConvertFrom-Json -DateKind String
 $actual=Invoke-Owned (Join-Path $PSHOME 'pwsh.exe') (@('-NoProfile','-File',$echo)+@($plan.arguments))
 $argv=$actual.stdout|ConvertFrom-Json -DateKind String
 if($actual.exit-ne0-or$actual.stderr-or$argv[-1]-cne'2030-10-12T01:32:12Z'){throw 'Actual UTC argv changed under de-DE'}
 $denied=Invoke-Owned (Resolve-Path -LiteralPath $Application).Path @('--host-end','10/12/2030 01:32:12')
 if($denied.exit-eq0-or$denied.stderr-notmatch 'Deadline must be an explicit ISO UTC string'){throw 'Production entrypoint did not deny locale date before listening'}
 Write-Output 'PASS actual child argv under de-DE and actual production entrypoint UTC denial; exit/reap/both stream EOF, no listener/device effects'
}finally{[Threading.Thread]::CurrentThread.CurrentCulture=$culture}
