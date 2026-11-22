$ErrorActionPreference = "Stop"
$token = $env:GH_TOKEN
$repo = "papUP8/AG-Cleaner"
$repoPath = "/repos/$repo"
$h = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner"; Accept = "application/vnd.github+json" }
$exe = "D:\Projects\AGCleaner\dist\AG-Cleaner.exe"

# 当前提交
$head = Invoke-RestMethod -Uri "https://api.github.com$repoPath/commits/main" -Headers $h
Write-Output ("head: " + $head.sha.Substring(0,7))

# 把 v1.6.0 指向最新提交
try {
    $b = @{ sha = $head.sha; force = $true } | ConvertTo-Json
    $ref = Invoke-RestMethod -Method Patch -Uri "https://api.github.com$repoPath/git/refs/tags/v1.6.0" -Headers $h -Body $b -ContentType "application/json"
    Write-Output ("tag -> " + $ref.object.sha.Substring(0,7))
} catch { Write-Output ("tag fail: " + $_.Exception.Message) }

$rel = Invoke-RestMethod -Uri "https://api.github.com$repoPath/releases/tags/v1.6.0" -Headers $h
Write-Output ("release id: " + $rel.id)

# 删除旧命名资产
foreach ($a in $rel.assets) {
    if ($a.name -match "AG[ .-]Cleaner\.exe") {
        Invoke-RestMethod -Method Delete -Uri "https://api.github.com$repoPath/releases/assets/$($a.id)" -Headers $h
        Write-Output ("deleted asset: " + $a.name)
    }
}

# 更新说明正文与标题
$bodyText = @"
AG Cleaner $($rel.tag_name.TrimStart('v')) — 单文件 Windows 清理工具。

- 下载 `AG-Cleaner.exe`（约 800 KB）双击即可，免安装，依赖 Windows 自带的 .NET Framework 4.8
- 先出清单、勾选后才删除；删除默认进回收站，注册表项删除前导出备份
- 不联网、不上传任何清单；配置与日志只写 `%USERPROFILE%\.config\agcleaner`
- 六种界面语言：中文 / English / Deutsch / Français / Русский / Español
- 许可 GPL-3.0，源码即本仓库
"@
$patch = @{ name = "AG Cleaner 1.6.0"; body = $bodyText } | ConvertTo-Json
$rel = Invoke-RestMethod -Method Patch -Uri "https://api.github.com$repoPath/releases/$($rel.id)" -Headers $h -Body $patch -ContentType "application/json; charset=utf-8"
Write-Output ("release: " + $rel.html_url)

# 上传新资产（名字含空格，URL 里编码成 %20）
$bytes = [System.IO.File]::ReadAllBytes($exe)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(300)
$client.DefaultRequestHeaders.Add("Authorization", "Bearer $token")
$client.DefaultRequestHeaders.Add("User-Agent", "AGCleaner")
$client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
$name = "AG-Cleaner.exe"
$upUrl = "https://uploads.github.com$repoPath/releases/$($rel.id)/assets?name=$name"
$content = [System.Net.Http.ByteArrayContent]::new($bytes)
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
$resp = $client.PostAsync($upUrl, $content).Result
Write-Output ("upload: " + $resp.StatusCode)
$txt = $resp.Content.ReadAsStringAsync().Result
if ($resp.IsSuccessStatusCode) {
    $o = $txt | ConvertFrom-Json
    Write-Output ("asset: " + $o.name + "  size=" + $o.size + "  " + $o.browser_download_url)
} else { Write-Output $txt }
