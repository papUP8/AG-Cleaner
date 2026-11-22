$ErrorActionPreference = "Continue"
$token = $env:GH_TOKEN
$repo = "papUP8/AG-Cleaner"
$repoPath = "/repos/$repo"
$h = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner"; Accept = "application/vnd.github+json" }
$exe = "D:\Projects\AGCleaner\dist\AG-Cleaner.exe"

$head = Invoke-RestMethod -Uri "https://api.github.com$repoPath/commits/main" -Headers $h
Write-Output ("head: " + $head.sha.Substring(0,7))

# 建 tag v1.7.0
try {
    $b = @{ ref = "refs/tags/v1.7.0"; sha = $head.sha } | ConvertTo-Json
    $ref = Invoke-RestMethod -Method Post -Uri "https://api.github.com$repoPath/git/refs" -Headers $h -Body $b -ContentType "application/json"
    Write-Output ("tag created: " + $ref.ref)
} catch { Write-Output ("tag fail: " + $_.Exception.Message) }

# 建 release
$bodyText = @"
AG Cleaner 1.7.0 — 单文件 Windows 清理工具。

- 下载 `AG-Cleaner.exe`（约 820 KB）双击即可，免安装，依赖 Windows 自带的 .NET Framework 4.8
- 先出清单、勾选后才删除；删除默认进回收站，注册表项删除前导出备份
- 不联网、不上传任何清单；配置与日志只写 `%USERPROFILE%\.config\agcleaner`
- 六种界面语言：中文 / English / Deutsch / Français / Русский / Español
- 许可 GPL-3.0，源码即本仓库

本版变化：
- 浏览器缓存覆盖到每一个 profile 与嵌套布局（User Data / Partitions / browser-data / cache.x64.*），新增 Opera、Yandex、搜狗、QQ、2345、猎豹
- 新增驱动安装包残留、传递优化缓存、诊断数据、LiveKernelReports 等系统级规则
- 回收站默认勾选；清空改为「直删 $R/$I → 交给 shell → 逐盘复核」，清不掉会明确报失败
- 残留清理新增空目录、7 天前 0 字节空文件、HKCU\Software 空注册表键（删除前自动备份）
- 新增 `docs/CLEANUP-RULES.md`，写明清理依据、覆盖清单与红线
"@
$relBody = @{ tag_name = "v1.7.0"; name = "AG Cleaner 1.7.0"; body = $bodyText; draft = $false; prerelease = $false } | ConvertTo-Json
$rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com$repoPath/releases" -Headers $h -Body $relBody -ContentType "application/json; charset=utf-8"
Write-Output ("release: " + $rel.html_url)

$bytes = [System.IO.File]::ReadAllBytes($exe)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(300)
$client.DefaultRequestHeaders.Add("Authorization", "Bearer $token")
$client.DefaultRequestHeaders.Add("User-Agent", "AGCleaner")
$client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
$upUrl = "https://uploads.github.com$repoPath/releases/$($rel.id)/assets?name=AG-Cleaner.exe"
$content = [System.Net.Http.ByteArrayContent]::new($bytes)
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
$resp = $client.PostAsync($upUrl, $content).Result
Write-Output ("upload: " + $resp.StatusCode)
$txt = $resp.Content.ReadAsStringAsync().Result
if ($resp.IsSuccessStatusCode) {
    $o = $txt | ConvertFrom-Json
    Write-Output ("asset: " + $o.name + "  size=" + $o.size + "  " + $o.browser_download_url)
} else { Write-Output $txt }
