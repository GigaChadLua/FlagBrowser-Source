param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repo,

    [string]$Notes = "Release $Version",

    [switch]$UploadToGitHub
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch "^v?\d+\.\d+\.\d+(\.\d+)?$") {
    throw "Invalid version format. Use 1.2.3 or v1.2.3"
}

$normalizedVersion = $Version.TrimStart("v")
$tag = $normalizedVersion

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish-win-x64"
$releaseDir = Join-Path $artifacts "release-$tag"
$exeName = "flagbrowser.exe"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null
New-Item -ItemType Directory -Path $releaseDir | Out-Null

Write-Host "Building release..."
dotnet publish (Join-Path $root "FlagInjector.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:Version=$normalizedVersion `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=false `
  -o $publishDir

$exePath = Join-Path $publishDir $exeName
if (-not (Test-Path $exePath)) {
    throw "$exeName not found in publish output."
}

$outExe = Join-Path $releaseDir $exeName
Copy-Item $exePath $outExe -Force

$hash = (Get-FileHash $outExe -Algorithm SHA256).Hash.ToUpper()
$downloadUrl = "https://github.com/$Repo/releases/download/$tag/$exeName"

$manifest = @{
    version     = $normalizedVersion
    downloadUrl = $downloadUrl
    sha256      = $hash
    notes       = $Notes
} | ConvertTo-Json -Depth 5

$manifestPath = Join-Path $releaseDir "manifest.json"
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

Write-Host ""
Write-Host "Release files generated:"
Write-Host " - $outExe"
Write-Host " - $manifestPath"
Write-Host "SHA256: $hash"

if ($UploadToGitHub) {
    Write-Host ""
    Write-Host "Uploading to GitHub release..."
    gh release create $tag $outExe $manifestPath --repo $Repo --title $tag --notes $Notes
    Write-Host "Done."
}
