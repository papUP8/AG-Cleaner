$ErrorActionPreference = "Stop"
$token = $env:GH_TOKEN
if (-not $token) { Write-Output "缺少 GH_TOKEN"; exit 1 }
$owner = "papUP8"
$oldRepo = "CC-Desk"
$newRepo = "AG-Cleaner"
$ver = "1.6.0"
$exePath = "D:\Projects\CCDesk\dist\AG-Cleaner.exe"

$headers = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner-Publisher"; Accept = "application/vnd.github+json" }

function Api($method, $url, $body) {
    $all = $headers.Clone()
    if ($body -ne $null) { $all["Content-Type"] = "application/json; charset=utf-8"; $json = $body | ConvertTo-Json -Depth 6 }
    if ($body -ne $null) { return Invoke-RestMethod -Method $method -Uri $url -Headers $all -Body $json }
    return Invoke-RestMethod -Method $method -Uri $url -Headers $all
}

# 0 校验令牌
$me = Api Get "https://api.github.com/user" $null
Write-Output ("登录身份: " + $me.login)

# 1 仓库改名 + 描述
try {
    $r = Api Patch "https://api.github.com/repos/$owner/$oldRepo" @{
        name = $newRepo
        description = "One 780 KB exe, no installer, review-before-delete, 6 languages, GPL-3.0"
        homepage = ""
    }
    Write-Output ("仓库已改名: " + $r.full_name + "  -> " + $r.html_url)
} catch {
    Write-Output ("改名失败(可能已是新名): " + $_.Exception.Message)
}
try {
    $topics = @{ names = @("windows","cleaner","winforms","csharp","disk-cleanup","gplv3","portable-app","no-installer") }
    $json = $topics | ConvertTo-Json -Depth 4
    $th = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner-Publisher"; Accept = "application/vnd.github.mercy-preview+json" }
    $tp = Invoke-RestMethod -Method Put -Uri "https://api.github.com/repos/$owner/$newRepo/topics" -Headers $th -Body $json -ContentType "application/json; charset=utf-8"
    Write-Output ("topics: " + ($tp.names -join ", "))
} catch { Write-Output ("topics 失败: " + $_.Exception.Message) }

# 2 推送分支与全部 tag
$git = "D:\AI collections\Git\cmd\git.exe"
Push-Location "D:\Projects\CCDesk"
& $git push ("https://x-access-token:$token@github.com/$owner/$newRepo.git") HEAD:main --tags --force 2>&1 | Select-Object -Last 5
& $git remote set-url origin ("https://github.com/$owner/$newRepo.git")
Pop-Location

# 3 Release
$old = "/repos/$owner/$oldRepo"
$new = "/repos/$owner/$newRepo"
$relBody = @{
    tag_name = "v$ver"
    name = "AG Cleaner $ver"
    body = @"
AG Cleaner $ver — 单文件 Windows 清理工具。

- 下载 `AG-Cleaner.exe`（约 800 KB）双击即可，免安装，依赖 Windows 自带的 .NET Framework 4.8
- 先出清单、勾选后才删除；删除默认进回收站，注册表项删除前导出备份
- 不联网、不上传任何清单；配置与日志只写 `%USERPROFILE%\.config\agcleaner`
- 六种界面语言：中文 / English / Deutsch / Français / Русский / Español
- 许可 GPL-3.0，源码即本仓库
"@
} 
$rel = $null
try { $rel = Api Get "https://api.github.com$new/releases/tags/v$ver" $null } catch { }
if ($rel) {
    Write-Output "Release 已存在，复用"
} else {
    $rel = Api Post "https://api.github.com$new/releases" $relBody
    Write-Output ("Release 创建: " + $rel.html_url)
}

# 4 上传资产（HttpClient 字节体，避免 Bad Content-Length）
$bytes = [System.IO.File]::ReadAllBytes($exePath)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(300)
$client.DefaultRequestHeaders.Add("Authorization", "Bearer $token")
$client.DefaultRequestHeaders.Add("User-Agent", "AGCleaner-Publisher")
$client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
$upUrl = "https://uploads.github.com$new/releases/$($rel.id)/assets?name=AG-Cleaner.exe"
$content = [System.Net.Http.ByteArrayContent]::new($bytes)
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
$resp = $client.PostAsync($upUrl, $content).Result
Write-Output ("上传状态: " + $resp.StatusCode)
$txt = $resp.Content.ReadAsStringAsync().Result
if ($resp.IsSuccessStatusCode) {
    $o = $txt | ConvertFrom-Json
    Write-Output ("资产: " + $o.browser_download_url + "  size=" + $o.size)
} else {
    Write-Output $txt
}
