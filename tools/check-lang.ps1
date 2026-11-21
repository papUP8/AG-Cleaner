param([string]$Root = "D:\Projects\AGCleaner\src")

$langPath = Join-Path $Root "Lang.cs"
$dict = @{}
foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($langPath), '\{\s*"((?:[^"\\]|\\.)*)"\s*,')) {
  $dict[$m.Groups[1].Value] = 1
}

$used = @{}
$files = Get-ChildItem -LiteralPath $Root -Recurse -Filter *.cs | Where-Object { $_.Name -ne 'Lang.cs' }
foreach ($f in $files) {
  $t = [System.IO.File]::ReadAllText($f.FullName)
  foreach ($m in [regex]::Matches($t, 'Lang\.T\("((?:[^"\\]|\\.)*)"')) {

    $used[$m.Groups[1].Value] = $f.Name
  }
  foreach ($m in [regex]::Matches($t, 'Lang\.F\("((?:[^"\\]|\\.)*)"')) {
    $used[$m.Groups[1].Value] = $f.Name
  }
}

"dict keys   = " + $dict.Count
"used keys   = " + $used.Count
$miss = @()
foreach ($k in $used.Keys) { if (-not $dict.ContainsKey($k)) { $miss += ("{0} :: {1}" -f $used[$k], $k) } }
"missing     = " + $miss.Count
$miss | Sort-Object | ForEach-Object { $_ }
