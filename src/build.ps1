param(
    [switch]$Icon
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = $PSScriptRoot
$dist = Join-Path $root "dist"
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$files = @(Get-ChildItem -Path $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
$final = Join-Path $dist "AG-Cleaner.exe"
$out = Join-Path $dist "AGCleaner.exe"  # csc 不认带空格的 /out，先编到这里再改名

$opt = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$out",
    "/win32manifest:$src\app.manifest",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll"
)

foreach ($doc in @("README.md", "CHANGES.md", "NOTICE.md", "LICENSE")) {
    $path = Join-Path $root $doc
    if (Test-Path $path) { $opt += "/resource:$path" }
}
if ($Icon) {
    $icoRes = Join-Path $root "assets\app.ico"
    if (Test-Path $icoRes) { $opt += "/resource:$icoRes" }
    $ico = Join-Path $root "assets\app.ico"
    if (Test-Path $ico) { $opt += "/win32icon:$ico" }
}

$opt += $files

Write-Host ("Compiling {0} source files -> {1}" -f $files.Count, $out)
& $csc $opt
if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }
if (Test-Path $final) { Remove-Item -LiteralPath $final -Force }
Move-Item -LiteralPath $out -Destination $final -Force
$kb = [math]::Round((Get-Item -LiteralPath $final).Length / 1KB)
Write-Host ("Build OK: {0} KB -> {1}" -f $kb, $final)
