$ErrorActionPreference = "Stop"
$token = $env:GH_TOKEN
$repo = "papUP8/AG-Cleaner"
$h = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner"; Accept = "application/vnd.github+json" }
$root = "D:\Projects\AGCleaner"
$files = @("CHANGES.md","README.en.md","README.md","install.ps1","src/Core/Native.cs","src/DocForm.cs","src/Lang.cs","src/LangTables.cs","src/build.ps1","tools/publish.ps1","tools/publish_ag.ps1")
foreach ($f in $files) {
    $local = Join-Path $root ($f -replace '/','\')
    $sha = $null
    try {
        $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/contents/$f" -Headers $h
        $sha = $existing.sha
    } catch { }
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($local))
    $body = @{ message = "改名写法统一为 AG Cleaner.exe（文件名带空格）"; content = $b64 }
    if ($sha) { $body.sha = $sha }
    $json = $body | ConvertTo-Json
    try {
        $r = Invoke-RestMethod -Method Put -Uri "https://api.github.com/repos/$repo/contents/$f" -Headers $h -Body $json -ContentType "application/json; charset=utf-8"
        Write-Output ("OK  $f -> " + $r.commit.sha.Substring(0,7))
    } catch { Write-Output ("FAIL $f : " + $_.Exception.Message) }
}
