# Skrip Kompilasi Windows 11 Update Blocker (.NET Framework 4.8 / csc.exe)
$ErrorActionPreference = "Stop"

Write-Host "=== Memulai Kompilasi Windows 11 Update Blocker ===" -ForegroundColor Cyan

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Error "Kompiler C# tidak ditemukan di $cscPath"
    exit 1
}

$outputExe = Join-Path $PSScriptRoot "Win11UpdateBlocker.exe"
$sourceFile = Join-Path $PSScriptRoot "src\Win11UpdateBlocker.cs"
$manifestFile = Join-Path $PSScriptRoot "src\app.manifest"

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
    "/win32manifest:`"$manifestFile`"",
    "/out:`"$outputExe`"",
    "/r:$references",
    "`"$sourceFile`""
)

Write-Host "Mengompilasi berkas sumber: $sourceFile" -ForegroundColor Yellow
$compileCmd = "& `"$cscPath`" " + ($argsList -join " ")
Invoke-Expression $compileCmd

if ($LASTEXITCODE -eq 0 -and (Test-Path $outputExe)) {
    $fileInfo = Get-Item $outputExe
    Write-Host "`n[SUKSES] Berkas executable berhasil dibuat!" -ForegroundColor Green
    Write-Host "Lokasi File : $($fileInfo.FullName)" -ForegroundColor White
    Write-Host "Ukuran File : $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor White
} else {
    Write-Error "[GAGAL] Kompilasi gagal dengan exit code $LASTEXITCODE"
    exit 1
}
