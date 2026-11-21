param(
    [Parameter(Mandatory = $true)][string]$Token,
    [string]$Repo = "AG Cleaner",
    [switch]$Public,
    [string]$Tag = "v1.5.3"
)

# 发布 AG Cleaner 到 GitHub：建仓库 -> 推送全部分支与标签 -> 建 Release 并附 exe
# 令牌只在本进程内存中使用，不写入 .git/config、不落盘。

$ErrorActionPreference = "Stop"
$git = "D:\AI collections\Git\cmd\git.exe"
$root = Split-Path -Parent $PSScriptRoot
$api = "https://api.github.com"
$head = @{ Authorization = "Bearer $Token"; Accept = "application/vnd.github+json"; "User-Agent" = "agcleaner-publish" }

function Invoke-GitHub([string]$Method, [string]$Url, $Body) {
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $head
    }
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $head -Body ($Body | ConvertTo-Json -Depth 6) -ContentType "application/json"
}

Write-Host "1/5 check token"
$me = Invoke-GitHub "GET" "$api/user" $null
$owner = $me.login
Write-Host ("    logged in as " + $owner)

Write-Host "2/5 create repository"
$vis = if ($Public) { "public" } else { "private" }
$existing = $null
try { $existing = Invoke-GitHub "GET" "$api/repos/$owner/$Repo" $null } catch { $existing = $null }
if ($existing) {
    Write-Host ("    already exists: " + $existing.html_url)
} else {
    $body = @{
        name        = $Repo
        description = "AG Cleaner - Windows cleanup and tune-up tool (GPL-3.0, single 700 KB exe, UI in 6 languages)"
        homepage    = ""
        private     = (-not $Public)
        has_issues  = $true
        has_wiki    = $false
    }
    $created = Invoke-GitHub "POST" "$api/user/repos" $body
    Write-Host ("    created " + $vis + ": " + $created.html_url)
}

Write-Host "3/5 push history and tags"
Push-Location $root
try {
    $pushUrl = "https://x-access-token:$Token@github.com/$owner/$Repo.git"
    & $git push $pushUrl "refs/heads/main:refs/heads/main" --force
    if ($LASTEXITCODE -ne 0) { throw "push branch failed" }
    & $git push $pushUrl --tags --force
    if ($LASTEXITCODE -ne 0) { throw "push tags failed" }
    & $git remote remove origin 2>$null | Out-Null
    & $git remote add origin "https://github.com/$owner/$Repo.git"
    Write-Host "    pushed; origin set to clean url"
} finally {
    Pop-Location
}

Write-Host "4/5 create release"
$exe = Join-Path $root "dist\AG-Cleaner.exe"
if (-not (Test-Path $exe)) { throw "missing dist\AG-Cleaner.exe" }
$relBody = @{
    tag_name = $Tag
    name     = "AG Cleaner " + $Tag.TrimStart('v')
    body     = "Single-file Windows cleanup tool. Download AG-Cleaner.exe and run it - no install, no extra files. Requires the .NET Framework 4.x that ships with Windows 10/11. Docs, changelog, credits and license are embedded in the exe (Settings -> About -> Docs & license)."
    draft    = $false
    prerelease = $false
}
$release = $null
try { $release = Invoke-GitHub "GET" "$api/repos/$owner/$Repo/releases/tags/$Tag" $null } catch { $release = $null }
if ($release) {
    Write-Host ("    release exists: " + $release.html_url)
} else {
    $release = Invoke-GitHub "POST" "$api/repos/$owner/$Repo/releases" $relBody
    Write-Host ("    release created: " + $release.html_url)
}

Write-Host "5/5 upload exe"
$name = "AG-Cleaner.exe"
$assets = Invoke-GitHub "GET" ($release.upload_url.Split('{')[0] + "?name=$name") $null
$already = @($assets | Where-Object { $_.name -eq $name })
if ($already.Count -gt 0) {
    Write-Host "    asset already uploaded, skip"
} else {
    $uploadUrl = $release.upload_url.Split('{')[0] + "?name=$name"
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $upHead = @{ Authorization = "Bearer $Token"; "User-Agent" = "agcleaner-publish"; "Content-Type" = "application/octet-stream" }
    $res = Invoke-RestMethod -Method "POST" -Uri $uploadUrl -Headers $upHead -Body $bytes -TimeoutSec 300
    Write-Host ("    uploaded: " + $res.browser_download_url)
}

Write-Host ""
Write-Host ("repo    : https://github.com/$owner/$Repo")
Write-Host ("release : https://github.com/$owner/$Repo/releases/tag/$Tag")
