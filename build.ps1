<#
.SYNOPSIS
  Builds dist\FanControl.Dashboard.dll, the harness and FcTool, and runs the tests.

.DESCRIPTION
  Uses the .NET SDK (dotnet build / dotnet test) when one is installed. Without an SDK it falls back to the
  C# compiler that ships with Visual Studio (Build Tools); that path builds everything but cannot run the tests.

  Fan Control's own assemblies are referenced from -FanControlDir (default: $env:FANCONTROL_DIR, then
  C:\Program Files (x86)\FanControl). They are never copied into the output.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -FanControlDir D:\Apps\FanControl
  .\build.ps1 -Compiler csc
#>
param(
    [string]$FanControlDir = $(if ($env:FANCONTROL_DIR) { $env:FANCONTROL_DIR } else { 'C:\Program Files (x86)\FanControl' }),
    [ValidateSet('auto', 'dotnet', 'csc')][string]$Compiler = 'auto',
    [switch]$NoTest
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

if (-not (Test-Path (Join-Path $FanControlDir 'FanControl.Plugins.dll'))) {
    throw "FanControl.Plugins.dll not found in '$FanControlDir'. Install Fan Control or pass -FanControlDir."
}

function Test-DotnetSdk {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    return [bool](& dotnet --list-sdks 2>$null)
}

$useDotnet = $Compiler -eq 'dotnet' -or ($Compiler -eq 'auto' -and (Test-DotnetSdk))

if ($useDotnet) {
    & dotnet build "$root\FanControl.Dashboard.sln" -c Release "-p:FanControlDir=$FanControlDir" -nologo
    if ($LASTEXITCODE) { throw 'dotnet build failed' }
    if (-not $NoTest) {
        & dotnet test "$root\FanControl.Dashboard.sln" -c Release --no-build "-p:FanControlDir=$FanControlDir" -nologo
        if ($LASTEXITCODE) { throw 'tests failed' }
    }
    Copy-Item "$root\src\bin\Release\FanControl.Dashboard.dll" $dist -Force
    Write-Host "Built dist\FanControl.Dashboard.dll, harness\bin\Release\Harness.exe and tools\FcTool\bin\Release\FcTool.exe"
    return
}

# ---- fallback: Visual Studio's csc.exe (no NuGet, no tests) ----
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = $null
if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($vs) { $csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe' }
}
if (-not $csc -or -not (Test-Path $csc)) { throw 'No .NET SDK and no Visual Studio C# compiler found. Install the .NET SDK: https://dotnet.microsoft.com/download' }
Write-Host 'No .NET SDK found: building with Visual Studio''s csc.exe (tests are skipped).'

$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$refs = @("$fw\WPF\PresentationFramework.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll", "$fw\System.Xaml.dll",
          "$fw\System.Web.Extensions.dll", "$fw\System.Core.dll", "$fw\netstandard.dll", "$fw\WPF\UIAutomationProvider.dll", "$fw\WPF\UIAutomationTypes.dll") | ForEach-Object { "-r:$_" }
$fcRefs = 'FanControl.Plugins', 'FanControl.IPC', 'Grpc.Core.Api', 'Google.Protobuf' | ForEach-Object { "-r:$FanControlDir\$_.dll" }
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

$info = Join-Path $bin 'AssemblyInfo.g.cs'
@"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
[assembly: System.Reflection.AssemblyTitle("Fan Control Dashboard plugin")]
[assembly: System.Reflection.AssemblyProduct("Fan Control Dashboard (unofficial plugin)")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Harness")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FanControl.Dashboard.Tests")]
"@ | Set-Content -Encoding UTF8 $info

& $csc -nologo -langversion:7.3 -target:library -optimize+ -deterministic "-out:$bin\FanControl.Dashboard.dll" @fcRefs @refs $info (Get-ChildItem "$root\src\*.cs").FullName
if ($LASTEXITCODE) { throw 'plugin build failed' }
& $csc -nologo -langversion:7.3 -target:winexe "-out:$bin\Harness.exe" "-r:$bin\FanControl.Dashboard.dll" @refs "$root\harness\Harness.cs"
if ($LASTEXITCODE) { throw 'harness build failed' }
& $csc -nologo -langversion:7.3 -target:exe "-out:$bin\FcTool.exe" @fcRefs "-r:$fw\System.Core.dll" "-r:$fw\netstandard.dll" "$root\tools\FcTool\FcTool.cs"
if ($LASTEXITCODE) { throw 'FcTool build failed' }
Copy-Item "$root\harness\App.config" "$bin\Harness.exe.config" -Force
Copy-Item "$root\harness\App.config" "$bin\FcTool.exe.config" -Force
Copy-Item "$bin\FanControl.Dashboard.dll" $dist -Force
Write-Host "Built dist\FanControl.Dashboard.dll $version, bin\Harness.exe and bin\FcTool.exe"
