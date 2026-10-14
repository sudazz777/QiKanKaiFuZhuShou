[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'bin\Release'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Cannot find the .NET Framework C# compiler. Build this project on 64-bit Windows with .NET Framework installed.'
}

$sourceNames = @(
    'CoopLauncherV2.cs', 'SteamJoinBridge.cs', 'SaveStorage.cs',
    'SaveManagement.cs', 'InstantSave.cs', 'SteamRuntime.cs',
    'HostInstallation.cs', 'HostBootstrap.cs', 'InstallationPage.cs', 'ServerConsole.cs', 'LauncherShutdown.cs', 'ServerPipeSession.cs'
)
$sources = @($sourceNames | ForEach-Object { Join-Path $projectRoot ('src\' + $_) })
$icon = Join-Path $projectRoot 'assets\bannerlord-knight.ico'
$visualBasic = Join-Path $frameworkDirectory 'Microsoft.VisualBasic.dll'
foreach ($inputFile in @($sources) + @($icon, $visualBasic)) {
    if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) {
        throw ('Missing build input: ' + $inputFile)
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputExe = Join-Path $OutputDirectory 'BannerlordCoopLauncher.exe'
$compilerArguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Web.Extensions.dll', ('/reference:' + $visualBasic),
    ('/win32icon:' + $icon),
    ('/resource:' + $icon + ',CoopLauncher.AppIcon.ico'),
    ('/out:' + $outputExe)
) + $sources

& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw ('Compilation failed with exit code ' + $LASTEXITCODE) }
if (!(Test-Path -LiteralPath $outputExe -PathType Leaf)) { throw 'Compilation did not produce an executable.' }
Write-Host ('Built: ' + $outputExe)
