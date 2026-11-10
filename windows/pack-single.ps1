# PadLink single-file packaging script
# Steps: publish UI folder -> zip payload -> embed into launcher -> dist\PadLink.exe
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Windows = Join-Path $Root "windows"
$Out = Join-Path $Windows "out"
$Dist = Join-Path $Root "dist"
$UiProject = Join-Path $Windows "src\PadLink.App.WinUI\PadLink.App.csproj"
$LauncherProject = Join-Path $Windows "src\PadLink.Launcher\PadLink.Launcher.csproj"
$Dotnet = "C:\Users\trump\.dotnet\dotnet.exe"
if (-not (Test-Path $Dotnet)) { $Dotnet = "dotnet" }

Write-Host "=== PadLink single-file packaging ===" -ForegroundColor Cyan

Get-Process PadLink*,PadLink.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$UiPublish = Join-Path $Out "winui-app"
if (-not $SkipBuild) {
    Write-Host "[1/5] Publishing WinUI app..." -ForegroundColor Yellow
    if (Test-Path $UiPublish) { Remove-Item $UiPublish -Recurse -Force }
    & $Dotnet publish $UiProject -c Release -o $UiPublish -p:PublishSingleFile=false -p:SelfContained=true | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "UI publish failed" }
} else {
    Write-Host "[1/5] Skipped build (reusing output)" -ForegroundColor DarkGray
}

$Version = (Get-Date).ToString("yyyyMMdd.HHmmss")
Set-Content -Path (Join-Path $UiPublish "version.txt") -Value $Version -Encoding UTF8
Write-Host "      version marker: $Version"

Write-Host "[2/5] Creating payload.zip..." -ForegroundColor Yellow
$Payload = Join-Path $Windows "src\PadLink.Launcher\payload.zip"
if (Test-Path $Payload) { Remove-Item $Payload -Force }
& python ($Root + "/test/make_payload.py") $UiPublish $Payload
if ($LASTEXITCODE -ne 0) { throw "payload packaging failed" }
$payloadSize = [math]::Round((Get-Item $Payload).Length / 1MB, 1)
Write-Host "      payload.zip: $payloadSize MB"

Write-Host "[3/5] Building single-file launcher..." -ForegroundColor Yellow
$LauncherOut = Join-Path $Out "launcher"
if (Test-Path $LauncherOut) { Remove-Item $LauncherOut -Recurse -Force }
& $Dotnet publish $LauncherProject -c Release -o $LauncherOut | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed" }

Write-Host "[4/5] Copying to dist..." -ForegroundColor Yellow
if (-not (Test-Path $Dist)) { New-Item -ItemType Directory -Path $Dist | Out-Null }
$Exe = Join-Path $LauncherOut "PadLink.exe"
if (-not (Test-Path $Exe)) { throw "PadLink.exe not produced" }
Copy-Item $Exe (Join-Path $Dist "PadLink.exe") -Force

Write-Host "[5/5] Done" -ForegroundColor Green
$final = Get-Item (Join-Path $Dist "PadLink.exe")
Write-Host ""
Write-Host "=== OUTPUT ===" -ForegroundColor Cyan
Write-Host "  $($final.FullName)"
Write-Host "  Size: $([math]::Round($final.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Extracts on first run to: $env:LOCALAPPDATA\PadLink\app" -ForegroundColor DarkGray
