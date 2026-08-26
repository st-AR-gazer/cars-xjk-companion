param(
  [string]$Configuration = "Release",
  [ValidateSet("win-x64", "linux-x64")]
  [string]$Runtime = "win-x64",
  [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "artifacts"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $artifactRoot $Runtime
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $resolvedOutput.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "The companion publish output must stay under the companion artifacts directory."
}

$framework = if ($Runtime -eq "win-x64") { "net8.0-windows" } else { "net8.0" }
$fileName = if ($Runtime -eq "win-x64") { "MoreCarsCompanion.exe" } else { "MoreCarsCompanion" }
$artifactName = if ($Runtime -eq "win-x64") { $fileName } else { "MoreCarsCompanion-linux-x64.tar.gz" }
$staging = [System.IO.Path]::GetFullPath((Join-Path $resolvedOutput ".morecars-publish-$Runtime"))
if (-not $staging.StartsWith($resolvedOutput + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "The companion publish staging path escaped the downloads directory."
}
if (Test-Path -LiteralPath $staging) {
  Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null

try {
  $project = Join-Path $PSScriptRoot "MoreCars.Companion.csproj"
  dotnet restore $project `
    --runtime $Runtime `
    -p:TargetFramework=$framework `
    --ignore-failed-sources
  if ($LASTEXITCODE -ne 0) {
    throw "The companion restore failed for $Runtime."
  }

  dotnet publish $project `
    --configuration $Configuration `
    --framework $framework `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --no-restore `
    --output $staging
  if ($LASTEXITCODE -ne 0) {
    throw "The companion publish failed for $Runtime."
  }

  $executable = Join-Path $staging $fileName
  if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The companion publish did not produce $fileName."
  }

  $artifact = Join-Path $resolvedOutput $artifactName
  if ($Runtime -eq "win-x64") {
    Copy-Item -LiteralPath $executable -Destination $artifact -Force
  } else {
    if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact -Force }
    tar -czf $artifact -C $staging $fileName
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the Linux companion archive." }
  }

  $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
  $size = (Get-Item -LiteralPath $artifact).Length
  Write-Output "Published $artifact"
  Write-Output "Bytes: $size"
  Write-Output "SHA-256: $hash"
} finally {
  if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
  }
}
