# Skrip Kompilasi Windows 11 Update Blocker (.NET Framework 4.8 / csc.exe)
$ErrorActionPreference = "Stop"

Write-Host "=== Memulai Kompilasi Windows 11 Update Blocker ===" -ForegroundColor Cyan

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Error "Kompiler C# tidak ditemukan di $cscPath"
    exit 1
}

$outputExe = Join-Path $PSScriptRoot "Win11UpdateBlocker.exe"
$oldExe = Join-Path $PSScriptRoot "Win11UpdateBlocker.old.exe"
if (Test-Path $oldExe) {
    Remove-Item $oldExe -Force -ErrorAction SilentlyContinue
}
if (Test-Path $outputExe) {
    try {
        Remove-Item $outputExe -Force -ErrorAction Stop
    } catch {
        Move-Item $outputExe $oldExe -Force -ErrorAction SilentlyContinue
    }
}
$sourceFile = Join-Path $PSScriptRoot "src\Win11UpdateBlocker.cs"
$manifestFile = Join-Path $PSScriptRoot "src\app.manifest"
$iconFile = Join-Path $PSScriptRoot "src\fixnow.ico"
$logoFile = Join-Path $PSScriptRoot "src\fixnow_logo.png"

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.ServiceProcess.dll"
) -join ","

$argsList = @(
    "/target:winexe",
    "/optimize+",
    "/platform:x64",
    "/win32manifest:$manifestFile",
    "/win32icon:$iconFile",
    "/resource:$logoFile,FixNowLogo.png",
    "/resource:$iconFile,FixNowIcon.ico",
    "/out:$outputExe",
    "/r:$references",
    "$sourceFile"
)

Write-Host "Mengompilasi berkas sumber: $sourceFile" -ForegroundColor Yellow
& $cscPath @argsList

if ($LASTEXITCODE -eq 0 -and (Test-Path $outputExe)) {
    $fileInfo = Get-Item $outputExe
    Write-Host "`n[SUKSES] Berkas executable berhasil dibuat!" -ForegroundColor Green
    Write-Host "Lokasi File : $($fileInfo.FullName)" -ForegroundColor White
    Write-Host "Ukuran File : $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor White
} else {
    Write-Error "[GAGAL] Kompilasi gagal dengan exit code $LASTEXITCODE"
    exit 1
}
