$ErrorActionPreference = "Stop"
$targetDir = Join-Path $env:LOCALAPPDATA "CC-Desk"
$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

foreach ($lnk in @((Join-Path $desktop "CC-Desk.lnk"), (Join-Path $startMenu "CC-Desk.lnk"))) {
    if (Test-Path $lnk) { Remove-Item -LiteralPath $lnk -Force }
}

Get-Process -Name "CC-Desk","CCDesk" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $targetDir) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
Write-Output "已卸载 CC-Desk（保留 %USERPROFILE%\.config\ccdesk 下的配置与日志）"
