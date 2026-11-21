param([string]$Root = "D:\Projects\AGCleaner\src")

$files = Get-ChildItem -LiteralPath $Root -Recurse -Filter *.cs | Where-Object { $_.Name -ne 'Lang.cs' }
function Test-Cjk([string]$s) {
  foreach ($ch in $s.ToCharArray()) { $v = [int]$ch; if ($v -ge 0x4E00 -and $v -le 0x9FA5) { return $true } }
  return $false
}
$total = 0
foreach ($f in $files) {
  $lines = [System.IO.File]::ReadAllLines($f.FullName)
  $changed = $false
  for ($li = 0; $li -lt $lines.Length; $li++) {
    $line = $lines[$li]
    $inStr = $false
    $slash = -1
    for ($i = 0; $i -lt $line.Length; $i++) {
      $ch = $line[$i]
      if ($inStr) {
        if ($ch -eq [char]'\') { $i++ }
        elseif ($ch -eq [char]'"') { $inStr = $false }
      } else {
        if ($ch -eq [char]'"') { $inStr = $true }
        elseif ($ch -eq [char]'/' -and ($i + 1) -lt $line.Length -and $line[$i + 1] -eq [char]'/') { $slash = $i; break }
      }
    }
    if ($slash -ge 0) { $code = $line.Substring(0, $slash); $tail = $line.Substring($slash) }
    else { $code = $line; $tail = "" }
    if ($code.Length -eq 0) { continue }

    $ms = [regex]::Matches($code, '"((?:[^"\\]|\\.)*)"')
    $off = 0
    for ($k = $ms.Count - 1; $k -ge 0; $k--) {
      $m = $ms[$k]
      $content = $m.Groups[1].Value
      if (-not (Test-Cjk $content)) { continue }
      $prev = if ($m.Index -gt 0) { $code[$m.Index - 1] } else { [char]' ' }
      if ($prev -eq [char]'@') { continue }
      $before = $code.Substring(0, [Math]::Max(0, $m.Index - 8))
      $rep = 'Lang.T("' + $content + '")'
      $code = $code.Substring(0, $m.Index) + $rep + $code.Substring($m.Index + $m.Length)
      $off++
      $changed = $true
    }
    $lines[$li] = $code + $tail
    $total += $off
  }
  if ($changed) { [System.IO.File]::WriteAllLines($f.FullName, $lines, (New-Object System.Text.UTF8Encoding($false))) }
}
"wrapped literals = $total"
