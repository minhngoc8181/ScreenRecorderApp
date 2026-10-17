# Build ban Release va dong goi gon de gui cho nguoi khac:
#   dist\ScreenRecorder\      (exe, config, NAudio dll, ffmpeg.exe, README.md)
#   dist\ScreenRecorder.zip
# Khong kem videos\ va recorder-gui-settings.json (du lieu ca nhan).
# Chay:  powershell -ExecutionPolicy Bypass -File .\make-dist.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

dotnet build "$root\ScreenRecorderApp.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build that bai" }

$bin  = Join-Path $root 'bin\Release\net48'
$dist = Join-Path $root 'dist'
$pkg  = Join-Path $dist 'ScreenRecorder'
$zip  = Join-Path $dist 'ScreenRecorder.zip'

if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Path $pkg -Force | Out-Null

$files = 'ScreenRecorder.exe', 'ScreenRecorder.exe.config', 'NAudio.Core.dll',
         'NAudio.Wasapi.dll', 'ffmpeg.exe', 'README.md'
foreach ($f in $files) {
    $src = Join-Path $bin $f
    if (-not (Test-Path $src)) { throw "Thieu file: $src" }
    Copy-Item $src $pkg
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Da dong goi xong:" -ForegroundColor Green
Get-ChildItem $pkg | Format-Table Name, Length -AutoSize
Write-Host "Zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
