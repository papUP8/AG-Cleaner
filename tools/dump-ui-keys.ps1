param([string]$Root = "D:\Projects\CCDesk\src")
$out = @()
$files = Get-ChildItem -LiteralPath $Root -Recurse -Filter *.cs |
  Where-Object { $_.Name -ne 'Lang.cs' -and $_.Name -ne 'LangTables.cs' -and ($_.Directory.Name -eq 'Pages' -or $_.Directory.Name -eq 'CCDesk' -or $_.FullName -like '*\src\Pages\*' -or $_.Name -in @('Dlg.cs','MainForm.cs','Controls.cs','Program.cs')) }
foreach ($f in $files) {
  $t = [System.IO.File]::ReadAllText($f.FullName)
  foreach ($m in [regex]::Matches($t, 'Lang\.(?:T|F)\("((?:[^"\\]|\\.)*)"')) {
    $out += ("{0}|{1}" -f $f.Name, $m.Groups[1].Value)
  }
}
$out | Sort-Object -Unique | Out-File -Encoding utf8 D:\Projects\CCDesk\shots\ui_keys.txt
"count=" + ($out | Sort-Object -Unique).Count
