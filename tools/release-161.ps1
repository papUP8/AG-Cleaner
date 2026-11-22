$ErrorActionPreference = "Continue"
$token = $env:GH_TOKEN
$repo = "papUP8/AG-Cleaner"
$repoPath = "/repos/$repo"
$h = @{ Authorization = "Bearer $token"; "User-Agent" = "AGCleaner"; Accept = "application/vnd.github+json" }
$exe = "D:\Projects\AGCleaner\dist\AG-Cleaner.exe"

$head = Invoke-RestMethod -Uri "https://api.github.com$repoPath/commits/main" -Headers $h
Write-Output ("head: " + $head.sha.Substring(0,7))

# 建 tag v1.6.1
try {
    $b = @{ ref = "refs/tags/v1.6.1"; sha = $head.sha } | ConvertTo-Json
    $ref = Invoke-RestMethod -Method Post -Uri "https://api.github.com$repoPath/git/refs" -Headers $h -Body $b -ContentType "application/json"
    Write-Output ("tag created: " + $ref.ref)
} catch { Write-Output ("tag fail: " + $_.Exception.Message) }

# 建 release
$bodyText = @"
AG Cleaner 1.6.1 — 单文件 Windows 清理工具。

- 下载 `AG-Cleaner.exe`（约 800 KB）双击即可，免安装，依赖 Windows 自带的 .NET Framework 4.8
- 先出清单、勾选后才删除；删除默认进回收站，注册表项删除前导出备份
- 不联网、不上传任何清单；配置与日志只写 `%USERPROFILE%\.config\agcleaner`
- 六种界面语言：中文 / English / Deutsch / Français / Русский / Español
- 许可 GPL-3.0，源码即本仓库

本版变化：
- 启动项页的自动插件、应用软件服务、系统关键服务可以直接禁用与还原（插件停用前导出 .reg 备份，服务记录原启动类型）
- 启动项三列（分类、状态、操作）垂直居中，状态不再比按钮高出一截
- 启动项页底部状态栏留白，不再贴左边缘
- 语言选择弹窗右上角有关闭按钮，Esc 或点击窗外也可关闭
- 深度清理扫描：回收站体积改为逐固定盘查询并加 8 秒超时，不再卡在「检查回收站」；``--scan`` 输出各分类耗时
"@
$relBody = @{ tag_name = "v1.6.1"; name = "AG Cleaner 1.6.1"; body = $bodyText; draft = $false; prerelease = $false } | ConvertTo-Json
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
