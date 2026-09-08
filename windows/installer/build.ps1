param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$windowsDir = Split-Path $installerDir
$repoRoot = Split-Path $windowsDir
$appProject = Join-Path $windowsDir "QuotaArc\QuotaArc.csproj"
$payload = Join-Path $installerDir "payload"
$dist = Join-Path $windowsDir "dist"
$logo = Join-Path $repoRoot "assets\windows\QuotaArc-logo.jpg"
$iconOut = Join-Path $repoRoot "assets\windows\QuotaArc.ico"

New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Icon"
dotnet run --project (Join-Path $installerDir "MakeIcon\MakeIcon.csproj") -c $Configuration -- $logo $iconOut
if ($LASTEXITCODE -ne 0) { throw "icon failed" }
if (-not (Test-Path $iconOut)) { throw "QuotaArc.ico was not produced" }
$banner = Join-Path $repoRoot "assets\windows\WixUIBannerBmp.bmp"
$dialog = Join-Path $repoRoot "assets\windows\WixUIDialogBmp.bmp"
if (-not (Test-Path $banner)) { throw "WixUIBannerBmp.bmp was not produced" }
if (-not (Test-Path $dialog)) { throw "WixUIDialogBmp.bmp was not produced" }

Write-Host "Publish $Runtime"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishReadyToRun=true `
    -o $payload
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Get-ChildItem $payload -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $payload "createdump.exe")) {
    Remove-Item (Join-Path $payload "createdump.exe") -Force
}

Write-Host "MSI"
dotnet build (Join-Path $installerDir "QuotaArc.Installer.wixproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "msi build failed" }

$msi = Get-ChildItem (Join-Path $installerDir "bin") -Filter "QuotaArc.msi" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $msi) { throw "QuotaArc.msi was not produced" }

$csproj = Get-Content $appProject -Raw
if ($csproj -notmatch "<Version>([^<]+)</Version>") { throw "could not read Version from csproj" }
$version = $Matches[1]
$outMsi = Join-Path $dist "QuotaArc-$version.msi"
Copy-Item $msi.FullName $outMsi -Force

$zip = Join-Path $dist "QuotaArc-$version-portable-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $zip

Write-Host ""
Write-Host "Installer: $outMsi"
Write-Host "Portable:  $zip"
