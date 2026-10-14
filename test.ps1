[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $testFolder = 'blc-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) $testFolder
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory (Join-Path $OutputDirectory 'bin')

$exe = Join-Path $OutputDirectory 'bin\BannerlordCoopLauncher.exe'
$report = Join-Path $OutputDirectory 'self-test.txt'
$testProcess = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
if (!(Test-Path -LiteralPath $report -PathType Leaf)) { throw 'Self-test did not produce a report.' }
Get-Content -LiteralPath $report -Encoding UTF8
Write-Host ('Test report: ' + $report)
if ($testProcess.ExitCode -ne 0) { throw ('Self-test failed with exit code ' + $testProcess.ExitCode) }
if (Select-String -LiteralPath $report -Pattern '^FAIL ' -Quiet) { throw 'Self-test report contains a failure.' }
Write-Host 'Self-test passed.'
