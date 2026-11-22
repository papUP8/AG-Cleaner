$ErrorActionPreference = "Continue"
$token = $env:GH_TOKEN
$repo = "papUP8/AG-Cleaner"
$h = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner"; Accept = "application/vnd.github+json" }
$root = "D:\Projects\AGCleaner"
$git = "D:\AI collections\Git\cmd\git.exe"
Push-Location $root
$files = & $git show --pretty=format: --name-only HEAD | Where-Object { $_ -and (Test-Path (Join-Path $root ($_ -replace '/','\')) ) }
Pop-Location
foreach ($f in $files) {
    $local = Join-Path $root ($f -replace '/','\')
    $sha = $null
    try {
        $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/contents/$f" -Headers $h
        $sha = $existing.sha
    } catch { }
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($local))
    $body = @{ message = "v1.6.1 源码同步：$f"; content = $b64 }
    if ($sha) { $body.sha = $sha }
    $json = $body | ConvertTo-Json
    try {
        $r = Invoke-RestMethod -Method Put -Uri "https://api.github.com/repos/$repo/contents/$f" -Headers $h -Body $json -ContentType "application/json; charset=utf-8"
        Write-Output ("OK   $f -> " + $r.commit.sha.Substring(0,7))
    } catch { Write-Output ("FAIL $f : " + $_.Exception.Message) }
}
