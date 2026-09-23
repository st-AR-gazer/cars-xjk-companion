param([ValidateSet('Inspect', 'Observe', 'Refresh', 'SkinCommit', 'Recover')][string]$Mode = 'Inspect', [string]$GamePath = '')
$ErrorActionPreference = 'Stop'
$taskGames = @(Get-Process Trackmania -ErrorAction SilentlyContinue)
$taskProbe = Join-Path $PSScriptRoot 'bin\Release\net8.0\MoreCarsNativeProbe.dll'
if ($Mode -eq 'Recover') {
  if ($taskGames.Count) { throw 'Close Trackmania before recovering the native test.' }
  if (-not $GamePath) { throw 'Recovery requires -GamePath with the full path to Trackmania.exe.' }
  $taskArguments = @($taskProbe, '--recover-skin-commit', '--game', $GamePath)
} else {
  if ($taskGames.Count -ne 1) { throw 'Run exactly one Trackmania instance for this test.' }
  $taskGame = $taskGames[0]
  $taskArguments = @($taskProbe, '--pid', [string]$taskGame.Id, '--game', $taskGame.MainModule.FileName)
}
if ($Mode -eq 'Observe') { $taskArguments += '--observe-game-thread' }
if ($Mode -eq 'Refresh') { $taskArguments += '--refresh-game-fids' }
if ($Mode -eq 'SkinCommit') { $taskArguments += '--test-skin-commit' }
$taskEvidence = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskOutput = Join-Path $taskEvidence ($Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$taskPreviousErrorAction = $ErrorActionPreference
try {
  $ErrorActionPreference = 'Continue'
  $taskLines = @(& dotnet @taskArguments 2>&1)
  $taskExit = $LASTEXITCODE
}
finally { $ErrorActionPreference = $taskPreviousErrorAction }
$taskText = ($taskLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
[IO.File]::WriteAllText($taskOutput, $taskText + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$taskLines | Write-Output
Write-Host "Result saved to $taskOutput"
exit $taskExit
