param([switch]$TestsOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskRepo = Split-Path -Parent $taskRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRepo '.codex-tmp\dotnet-native'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build (Join-Path $taskRoot 'MoreCars.NativeProbe.csproj') -c Release --nologo -m:1
if ($LASTEXITCODE -ne 0) { throw 'Native runtime host build failed.' }
$taskOutput = Join-Path $taskRoot 'bin\Release\net8.0'
$taskArtifacts = Join-Path $taskRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $taskArtifacts | Out-Null
$taskHeader = & dotnet (Join-Path $taskOutput 'MoreCarsNativeProbe.dll') --emit-cpp-profile
if ($LASTEXITCODE -ne 0) { throw 'Native profile generation failed.' }
[IO.File]::WriteAllText((Join-Path $taskArtifacts 'GameProfile.g.h'), ($taskHeader -join "`n"), [Text.UTF8Encoding]::new($false))
$taskVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskVs = & $taskVswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $taskVs) { throw 'MSVC x64 build tools are required.' }
$taskSetup = Join-Path $taskVs 'VC\Auxiliary\Build\vcvars64.bat'
$taskDll = Join-Path $taskOutput 'MoreCars.GameRuntime.dll'
$taskTestExe = Join-Path $taskArtifacts 'MoreCars.SkinTransactionTests.exe'
$taskQueueTestExe = Join-Path $taskArtifacts 'MoreCars.GameRuntimeTests.exe'
$taskDllCommand = if ($TestsOnly) { '' } else {
  'cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX /guard:cf /LD /I"' + $taskArtifacts + '" "' + $taskRoot + '\GameRuntime.cpp" "' + $taskRoot + '\SkinTransaction.cpp" /Fe"' + $taskDll + '" /link /INCREMENTAL:NO /guard:cf /IMPLIB:"' + $taskArtifacts + '\MoreCars.GameRuntime.lib"'
}
$taskBatch = @"
@echo off
call "$taskSetup" >nul
if errorlevel 1 exit /b 1
$taskDllCommand
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX /guard:cf /I"$taskArtifacts" "$taskRoot\SkinTransactionTests.cpp" "$taskRoot\SkinTransaction.cpp" /Fe"$taskTestExe" /link /INCREMENTAL:NO /guard:cf
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX /guard:cf /I"$taskArtifacts" "$taskRoot\GameRuntimeTests.cpp" "$taskRoot\SkinTransaction.cpp" /Fe"$taskQueueTestExe" /link /INCREMENTAL:NO /guard:cf
exit /b %errorlevel%
"@
[IO.File]::WriteAllText((Join-Path $taskArtifacts 'build-runtime.cmd'), $taskBatch, [Text.ASCIIEncoding]::new())
Push-Location $taskArtifacts
try { & $env:ComSpec /d /c build-runtime.cmd }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'Native skin runtime build failed.' }
& $taskTestExe (Join-Path $taskArtifacts 'transaction-tests')
if ($LASTEXITCODE -ne 0) { throw 'Native skin transaction checks failed.' }
& $taskQueueTestExe
if ($LASTEXITCODE -ne 0) { throw 'Native skin queue checks failed.' }
& dotnet (Join-Path $taskOutput 'MoreCarsNativeProbe.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Native reader checks failed.' }
Get-FileHash -LiteralPath $taskDll -Algorithm SHA256 | Select-Object Hash, Path
