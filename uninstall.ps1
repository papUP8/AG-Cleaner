$ErrorActionPreference = "Stop"
$targetDir = Join-Path $env:LOCALAPPDATA "CCDesk"
$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

foreach ($lnk in @((Join-Path $desktop "CCDesk.lnk"), (Join-Path $startMenu "CCDesk.lnk"))) {
    if (Test-Path $lnk) { Remove-Item -LiteralPath $lnk -Force }
}

Get-Process -Name "CCDesk" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $targetDir) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
Write-Output "已卸载 CCDesk（保留 %USERPROFILE%\.config\ccdesk 下的配置与日志）"
