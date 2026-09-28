# PTCGL fast startup

[English](README.md)

面向 Windows 版 Pokémon TCG Live 的 Harmony 补丁。Unity Doorstop 加载 `FastStartup\FastStartupBootstrap.dll`，在 `TPCI.RainierClient` 载入后应用补丁。

加载行保留游戏原文，并列出仍在 `Initialize` 中的 manager。`FastStartup.log` 写在可执行文件旁边。

版本号 `1.0.0` 写在 `Directory.Build.props`。两个程序集都使用它，启动时会记入 `FastStartup.log`。在客户端 `1.42.2.1247115`（Unity `6000.3.5`）上验证过。

## 优化了哪些部分

**资源清单。** `AssetBundleSetup` 按顺序下载带日期的清单分桶，各个语言的 source 也是同样串行。补丁会同时启动全部分桶，并让当前语言的 source 和 fallback source 一起加载。名为 `manifest_*_10101_*` 的分桶，以及日期早于当天（UTC）的分桶，会按资源路径和 `asset-bundle-manifest` 的 revision 得到稳定的缓存哈希。之后的启动会复用 Unity 缓存里的这些文件。当天的分桶不写哈希，仍会重新下载。

**本地化 gzip。** `LocalizationConfigManager` 按清单目录逐个下载 gzip，同时只有两个。这个 Mono 版 `HttpClient` 不理会 `MaxConnectionsPerServer`，走的是 `ServicePointManager`，默认同样是每个主机两个连接。补丁把 `GZipQueueSize` 和这个连接上限改成 16。

**商店商品。** 客户端连上之后，`NetworkManager` 会等 commerce、inventory、season rank 和 gifts 都完成，再请求商店商品。商品列表只依赖 commerce setup，所以补丁在 `Commerce.Setup` 完成时就发出 `GetShopOfferingsAsync`。游戏原本的那次调用改为等待同一个任务。

## 安装

需要 Windows、游戏本体和 .NET SDK。

```powershell
.\install.ps1
```

脚本会先生成 `dist\ptcgl-fast-startup-<version>.zip`，再把这份内容复制到 `C:\Users\yangyuhan` 下第一个匹配 `The Pok*\Pok*` 的目录。复制前会结束正在运行的游戏进程。两个项目都引用 Doorstop 下载包里的 `0Harmony.dll`，因此通过这个脚本编译。

## 发布包

```powershell
.\pack.ps1
```

脚本写出 `dist\ptcgl-fast-startup-<version>.zip`。把它解压到游戏可执行文件所在的目录，`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `FastStartup\` 会落在可执行文件旁边。压缩包里已经带上 Doorstop 和 Harmony 依赖，不需要本机安装 .NET SDK 就能启动游戏。

## 关闭优化

在可执行文件旁放置空文件 `FastStartup\optimize.off`，会跳过清单、本地化和提前请求商店商品的补丁。加载行文字和启动计时日志仍然保留。

## 许可证

MIT。见 [LICENSE](LICENSE)。
