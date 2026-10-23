# Build ban Release va dong goi gon de gui cho nguoi khac:
#   dist\ScreenRecorder\                  (exe, config, NAudio dll, ffmpeg.exe, README.md, CHANGELOG.md)
#   dist\ScreenRecorder-v<version>.zip    (version doc tu <Version> trong ScreenRecorderApp.csproj)
#   dist\RELEASE_NOTES.md                 (muc cua phien ban nay trong CHANGELOG.md, dung cho GitHub Release)
# Khong kem videos\ va recorder-gui-settings.json (du lieu ca nhan).
# Chay:  powershell -ExecutionPolicy Bypass -File .\make-dist.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

$csproj  = Join-Path $root 'ScreenRecorderApp.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Khong doc duoc <Version> trong $csproj" }
$version = $version.Trim()

dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build that bai" }

$bin  = Join-Path $root 'bin\Release\net48'
$dist = Join-Path $root 'dist'
$pkg  = Join-Path $dist 'ScreenRecorder'
$zip  = Join-Path $dist "ScreenRecorder-v$version.zip"
$notes = Join-Path $dist 'RELEASE_NOTES.md'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $pkg -Force | Out-Null

$files = 'ScreenRecorder.exe', 'ScreenRecorder.exe.config', 'NAudio.Core.dll',
         'NAudio.Wasapi.dll', 'ffmpeg.exe', 'README.md', 'CHANGELOG.md'
foreach ($f in $files) {
    $src = Join-Path $bin $f
    if (-not (Test-Path $src)) { throw "Thieu file: $src" }
    Copy-Item $src $pkg
}

Compress-Archive -Path $pkg -DestinationPath $zip -CompressionLevel Optimal

# Trich muc "## <version> ..." trong CHANGELOG.md lam ghi chu phat hanh
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$out = New-Object System.Collections.Generic.List[string]
$inSection = $false
foreach ($l in $lines) {
    if ($l -match '^##\s+(\S+)') {
        if ($inSection) { break }
        if ($Matches[1] -eq $version) { $inSection = $true; $out.Add('# Screen Recorder ' + ($l -replace '^##\s+', '')); continue }
    }
    if ($inSection) { $out.Add($l.TrimEnd()) }
}
if (-not $inSection) { throw "CHANGELOG.md chua co muc '## $version'" }
$out.Add('')
$out.Add('**Cai dat:** tai `ScreenRecorder-v' + $version + '.zip`, giai nen roi chay `ScreenRecorder.exe` (can Windows 10/11, da kem ffmpeg).')
[System.IO.File]::WriteAllLines($notes, $out, (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Da dong goi xong phien ban $version :" -ForegroundColor Green
Get-ChildItem $pkg | Format-Table Name, Length -AutoSize
Write-Host "Zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "Ghi chu phat hanh: $notes"
