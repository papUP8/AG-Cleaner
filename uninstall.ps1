$ErrorActionPreference = "Stop"
$targetDir = Join-Path $env:LOCALAPPDATA "AG Cleaner"
$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

foreach ($lnk in @((Join-Path $desktop "AG Cleaner.lnk"), (Join-Path $startMenu "AG Cleaner.lnk"))) {
    if (Test-Path $lnk) { Remove-Item -LiteralPath $lnk -Force }
}

Get-Process -Name "AG Cleaner","AGCleaner" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $targetDir) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
Write-Output "已卸载 AG Cleaner（保留 %USERPROFILE%\.config\agcleaner 下的配置与日志）"
