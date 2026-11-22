# AG Cleaner 清理规则与安全边界

这份文档说明 AG Cleaner 清理什么、按什么依据清理、哪些东西坚决不碰，以及为什么 360 之类工具经常能扫出好几倍的数字。

## 一、为什么 360 扫得多

360 自己的隐私白皮书列出的「清理垃圾」范围包括：系统临时文件、无效快捷方式、缩略图缓存、内存转储、Windows 错误报告、Windows 预读文件、已下载程序文件、Windows 自动更新时留下的补丁、Office 安装文件、Windows Media Player 临时同步文件、Flash Cookie、日志文件，以及本地磁盘上指定类型的垃圾文件。它的「清理注册表」另外扫无效开始菜单、无效右键菜单、无效 MUI 缓存、无效的帮助、无效的软件信息、无效的应用程序路径、无效的安装程序、无效的卸载程序、无效的防火墙设置。

差距主要来自四块，不在于它有多神秘：

1. **系统级项目要管理员权限。** 更新缓存、Delivery Optimization、CBS 日志、WER、字体缓存、Prefetch、事件日志、其他账号的 Temp，普通权限下一条都列不出来。按非管理员启动时看起来就是「只扫出几十 MB」。
2. **应用缓存的实际布局比想象中深。** Electron / CEF 客户端通常是 `<app>\User Data\<profile>\Cache`、`<app>\Partitions\<part>\Code Cache`、`<app>\browser-data\Default\Cache`、`<app>\cache.x64.*\...`。只按 `<app>\Cache` 找会漏掉大头（微信开发者工具、嘉立创 EDA、豆包客户端都属于这一类，单这一台机器就漏了 1.4 GB）。
3. **它还统计用户数据与「可优化项」。** 聊天记录里的图片视频、下载目录安装包、休眠文件、还原点、WinSxS、大文件，这些体积大，但严格说不属于「垃圾」，删掉是有代价的。
4. **它默认把回收站、注册表冗余、空目录一起算进去。** 这些在我们这里是独立页签、需要用户勾选。

## 二、微软认可的安全边界

- **WinSxS 不能手删。** 微软明确警告：删 WinSxS 里的文件或整个目录可能导致无法开机、无法更新。唯一受支持的方式是 `Dism /Online /Cleanup-Image /StartComponentCleanup`（可加 `/ResetBase`，但加了就不能回滚旧更新）。AG Cleaner 只清 `WinSxS\Temp`，不碰组件存储本体。
- **Prefetch 官方建议不要清。** 微软说明 Prefetch 自我维护、上限 128 项，清空只会让下次启动变慢。所以这项保留但默认不勾。
- **Windows 更新缓存的官方做法**是停 wuauserv/bits 后删 `SoftwareDistribution`，或直接用磁盘清理的「Windows 更新清理」；只删 `SoftwareDistribution\Download` 是安全的。
- **传递优化缓存**微软说明可清（磁盘清理里的「传递优化文件」），会自动重建。
- **缩略图/图标缓存**属于可再生缓存，删掉后资源管理器重建；微软的排障步骤就是删 `thumbcache_*.db` / `iconcache_*.db`。
- **回收站**只能清空，没有「部分清理」的安全做法；清空不可撤销，因此必须显式确认。
- **注册表**：微软不提供官方「注册表清理器」，并说明残留项通常无害。所以只处理能明确判定失效的项（卸载项指向已不存在的程序、空壳键），并且删除前导出 .reg 备份。

## 三、AG Cleaner 现在的规则集

**深度清理（按分类）**

| 分类 | 覆盖 |
| --- | --- |
| 临时文件 | 当前用户 Temp、LocalAppData\Temp，可按天数过滤 |
| 浏览器缓存 | Chrome / Edge / Brave / Vivaldi / Opera / Yandex / 360 / 夸克 / 搜狗 / QQ / 2345 / 猎豹 / Firefox 全部 profile 的 Cache、Code Cache、GPUCache、Service Worker\ScriptCache、ShaderCache；WebView2；自动发现嵌套 `User Data`、`Partitions`、`browser-data`、`cache.x64.*` 布局 |
| 应用缓存 | Discord / Slack / Teams / VS Code / Cursor / Obsidian / Notion / QQ / 飞书 / 剪映 / CapCut / Epic / WPS / Office / Adobe / Steam / 网易云 / 企业微信 / 钉钉 / 腾讯会议 / QQ 音乐 / 百度网盘，加上 Electron 目录自动发现与 UWP LocalCache/TempState |
| 系统痕迹缓存 | thumbcache_*.db、iconcache、Caches、远程桌面缓存、主题缩略图 |
| 系统缓存与日志 | Windows\Temp、SoftwareDistribution\Download、Windows\Logs（含 CBS/DISM/WindowsUpdate）、事件日志 evtx、系统字体缓存、MEMORY.DMP、Minidump、Prefetch、WER、Panther、安装器日志、LogFiles、服务账号 Temp、LiveKernelReports、传递优化缓存与日志、诊断数据、RetailDemo、Defender 扫描历史、驱动仓库 Temp、其他账号 Temp、NVIDIA/AMD/Intel 安装包残留、C:\Drivers |
| 系统深度清理 | Windows.old、$WINDOWS.~BT、$WINDOWS.~WS、WinSxS\Temp、搜索索引、Package Cache、$PatchCache$（默认不勾，需要管理员） |
| 日志与错误报告 | 用户与系统 WER、CrashDumps、MEMORY.DMP、INetCache、Adobe/WPS/腾讯会议/百度网盘/Docker 日志 |
| 使用痕迹（可选） | Recent、通知中心历史 |
| 旧安装包 | 下载目录里 14 天前、大于 5 MB 的 msi/exe/msix/appx |
| 项目产物（可选） | 用户指定目录下的 node_modules/target/dist/build 等 |
| 大体积日志 | 单文件 ≥48 MB、3 天未写入的 log/etl/dmp/cab/evtx/wer |
| 回收站 | 全盘回收站，默认勾选，清空前二次确认，清空不可撤销 |

**残留清理**

| 分组 | 判定 |
| --- | --- |
| 孤儿安装目录 | 有卸载程序、但已不在已安装列表里、90 天未写入、≥20 MB |
| 残留注册表键 | 卸载项指向的目录/程序都已不存在，删除前导出 .reg |
| 无效卸载注册表项 | Uninstall 项的 UninstallString/InstallLocation 不可执行 |
| 失效快捷方式 | 桌面、开始菜单里指向不存在文件的 .lnk |
| 失效启动项 | Run/RunOnce/计划任务指向不存在的程序 |
| **空目录**（1.7.0 新增） | 桌面、文档、下载、图片、音乐、视频下不含任何文件的目录，含递归空壳 |
| **空文件**（1.7.0 新增） | 上述目录里 7 天前创建、0 字节的文件（跳过 desktop.ini / .gitkeep / thumbs.db） |
| **空注册表键**（1.7.0 新增） | HKCU\Software 下无值无子键的空壳键，删除前自动备份 |

## 四、红线（永远不碰）

- `C:\Windows`、`C:\Program Files`、`C:\Program Files (x86)`、Defender 目录整体受保护，只开放白名单里的具体子路径（Windows\Temp、Logs、SoftwareDistribution\Download、Prefetch、Minidump、Panther、winevt\Logs、Installer 的 .log、INF 的 .log 等）。
- WinSxS 本体、DriverStore 本体、`ProgramData\Package Cache` 里的 MSI 源（删了会让卸载/修复失效）、`Windows\Installer` 本体、注册表 Classes/服务/驱动键。
- 浏览器登录态与密钥：Login Data、Cookies、Web Data、Local Storage、IndexedDB、Session Storage、Service Worker\CacheStorage；.ssh/.gnupg/.aws/.kube/.docker；AI 会话记录目录。
- 用户文档、图片、视频、下载目录里的正常文件（只处理明确判定为空的目录与 0 字节旧文件）。
- 账号数据、聊天记录数据库、游戏存档、虚拟机与容器镜像。

## 五、实测对照（这台机器，2026-11-22）

| 项 | 1.6.1 | 1.7.0 |
| --- | --- | --- |
| 浏览器缓存 | 8 项 / 90.4 MB | 80 项 / 1.47 GB |
| 系统缓存与日志 | 4 项 / 48.9 MB | 8 项 / 63.3 MB |
| 临时文件 | 1 项 / 17.1 MB | 1 项 / 20.2 MB |
| 回收站 | 2.69 MB（默认不勾） | 2.69 MB（默认勾选） |

差额就是被嵌套布局吞掉的部分：微信开发者工具 Partitions 约 900 MB、嘉立创 EDA 缓存约 370 MB、豆包客户端 88 MB。

## 六、参考来源

- 360 隐私白皮书（电脑清理涉及的行为描述、清理注册表范围）：https://weishi.360.cn/privacy.html
- 微软 WinSxS 清理说明：https://learn.microsoft.com/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder
- 微软 Prefetch 说明：https://learn.microsoft.com/answers/questions/2432551/can-i-delete-the-data-in-the-prefetch-folder
- 微软传递优化缓存说明：https://learn.microsoft.com/windows/deployment/do/waas-delivery-optimization-faq
- 微软存储感知与临时文件：https://support.microsoft.com/windows/experience/storage-filemanagement/manage-drive-space-with-storage-sense
- 微软关于注册表清理的答复：https://learn.microsoft.com/answers/questions/5561447/fix-and-scan-registry
- Winapp2.ini 社区规则库：https://github.com/MoscaDotTo/Winapp2
- BleachBit 规则库（CleanerML）：https://github.com/bleachbit/bleachbit/tree/master/cleaners
- MangoDisk 清理规则的安全约束（跳过 7 天内文件、跳过 reparse point、排除 RtBackup）：https://github.com/harry0703/MangoDisk
