param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($Source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Source parse failed'}
$definition=@($ast.FindAll({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-ceq'CompleteOwnedRaw'},$true))
if($definition.Count-ne1){throw 'Production raw completion helper missing'}
Invoke-Expression $definition[0].Extent.Text
[IO.Directory]::CreateDirectory($Output)|Out-Null
$outPath=Join-Path $Output 'stdout.raw';$errPath=Join-Path $Output 'stderr.raw'
$stdout=[IO.FileStream]::new($outPath,'CreateNew','Write','Read',1);$stderr=[IO.FileStream]::new($errPath,'CreateNew','Write','Read',1)
$start=[Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$code='$a=[Text.Encoding]::UTF8.GetBytes(("漢字🙂"*20000));$b=[Text.Encoding]::UTF8.GetBytes(("ошибка🙂"*10000));[Console]::OpenStandardOutput().Write($a);[Console]::OpenStandardError().Write($b)'
foreach($arg in @('-NoProfile','-EncodedCommand',[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code)))){$start.ArgumentList.Add($arg)}
$child=[Diagnostics.Process]::Start($start);$birth=$child.StartTime.ToUniversalTime().Ticks.ToString();$out=$child.StandardOutput.BaseStream.CopyToAsync($stdout);$err=$child.StandardError.BaseStream.CopyToAsync($stderr)
$child.WaitForExit();$out.GetAwaiter().GetResult();$err.GetAwaiter().GetResult();$stdout.Flush();$stderr.Flush()
$inheritedFailure=$false;try{Get-FileHash -LiteralPath $outPath -ErrorAction Stop|Out-Null}catch{$inheritedFailure=$_.Exception-is[IO.IOException]}
if(!$inheritedFailure){throw 'Control failed to reproduce original sharing violation'}
$raw=CompleteOwnedRaw $out $err $stdout $stderr $outPath $errPath
$expectedOut=[Text.Encoding]::UTF8.GetBytes(('漢字🙂'*20000));$expectedErr=[Text.Encoding]::UTF8.GetBytes(('ошибка🙂'*10000))
if($child.ExitCode-ne0-or$raw.stdoutBytes-ne$expectedOut.Length-or$raw.stderrBytes-ne$expectedErr.Length-or$raw.stdoutSha256-cne[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expectedOut)).ToLowerInvariant()-or$raw.stderrSha256-cne[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expectedErr)).ToLowerInvariant()){throw 'Actual raw length/hash/exit mismatch'}
foreach($path in @($outPath,$errPath)){$probe=[IO.File]::Open($path,'Open','ReadWrite','None');$probe.Dispose()}
$value=@{source=$Source;sourceSha256=(Get-FileHash -LiteralPath $Source).Hash.ToLowerInvariant();originalSharingViolationReproduced=$true;productionHelperUsed=$true;actualExit=$child.ExitCode;pid=$child.Id;birth=$birth;reaped=$true;stdoutEof=$true;stderrEof=$true;writersDisposed=$true;exclusiveOpenAfterCompletion=$true;raw=$raw;deviceCalls=0;mockedPipes=$false}
$value|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Output 'QUALIFICATION.json') -Encoding utf8
$value|ConvertTo-Json -Depth 5
