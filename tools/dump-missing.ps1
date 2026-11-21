param([string]$Root = "D:\Projects\AGCleaner\src")

$langText = [System.IO.File]::ReadAllText((Join-Path $Root "Lang.cs"))
$en = @{}
foreach ($m in [regex]::Matches($langText, '\{\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\},')) {
  $en[$m.Groups[1].Value] = $m.Groups[2].Value
}
$tablesText = [System.IO.File]::ReadAllText((Join-Path $Root "LangTables.cs"))
$have = @{}
foreach ($m in [regex]::Matches($tablesText, 'Add\(d, "((?:[^"\\]|\\.)*)"')) {
  $have[$m.Groups[1].Value] = 1
}
"En keys = " + $en.Count
"translated keys = " + $have.Count
$missing = @()
foreach ($k in $en.Keys) { if (-not $have.ContainsKey($k)) { $missing += $k } }
"missing = " + $missing.Count
$sb = New-Object System.Text.StringBuilder
foreach ($k in ($missing | Sort-Object)) {
  [void]$sb.AppendLine(($k -replace "`r?`n", "\\n") + " >>> " + ($en[$k] -replace "`r?`n", "\\n"))
}
[System.IO.File]::WriteAllText("D:\Projects\AGCleaner\shots\missing_keys.txt", $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
