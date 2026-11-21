$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here "AGCleaner.exe"
if (-not (Test-Path $exe)) { Write-Output "找不到 AGCleaner.exe，请把安装脚本放在程序同目录再运行"; exit 1 }

$targetDir = Join-Path $env:LOCALAPPDATA "AG Cleaner"
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$targetExe = Join-Path $targetDir "AGCleaner.exe"
Copy-Item -LiteralPath $exe -Destination $targetExe -Force
Copy-Item -LiteralPath (Join-Path $here "README.md") -Destination $targetDir -Force -ErrorAction SilentlyContinue

$shell = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath("Desktop")
$lnk1 = Join-Path $desktop "AG Cleaner.lnk"
$sc = $shell.CreateShortcut($lnk1)
$sc.TargetPath = $targetExe
$sc.WorkingDirectory = $targetDir
$sc.IconLocation = "$targetExe,0"
$sc.Description = "AG Cleaner - Windows 清理与优化"
$sc.Save()

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$lnk2 = Join-Path $startMenu "AG Cleaner.lnk"
$sc2 = $shell.CreateShortcut($lnk2)
$sc2.TargetPath = $targetExe
$sc2.WorkingDirectory = $targetDir
$sc2.IconLocation = "$targetExe,0"
$sc2.Description = "AG Cleaner - Windows 清理与优化"
$sc2.Save()

Copy-Item -LiteralPath (Join-Path $here "uninstall.ps1") -Destination $targetDir -Force -ErrorAction SilentlyContinue

Write-Output "已安装到 $targetDir"
Write-Output "桌面快捷方式：$lnk1"
Write-Output "开始菜单快捷方式：$lnk2"
Write-Output "卸载：powershell -ExecutionPolicy Bypass -File `"$targetDir\uninstall.ps1`""
