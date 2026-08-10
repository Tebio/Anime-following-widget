# Anime-following-widget

Windows 桌面追番小组件。数据直连 [AGE 动漫](https://www.agedm.io/) 周表（官方 App API 优先 + SSR HTML 兜底）。

## 三条线

| 线 | 状态 | 路径 |
|---|---|---|
| **v4.x WinUI 3** | 预览线（实机可用） | `winui-app/` |
| **v3.x C# WPF** | 主力稳定线 | `csharp-app/` |
| v2 Rust / v1 WinForms | 存档 | `anime-widget-rs/` / `AnimeWidgetDesktop/` |

## v4.x — WinUI 3 预览线（当前开发焦点）

.NET 10 + Windows App SDK 2.2，非打包单 exe 文件夹。与 v3 同一套桌面底座语义，WinUI 3 原生实现：

- **三档材质**：透明卡片（分层窗口 alpha）/ 毛玻璃（弱着色重模糊）/ 亚克力（深着色磨砂，DeskBox 同款 composition 控制器接线），背景深浅滑杆三档联动
- **窗口行为全家桶**：全窗口拖拽（InputNonClientPointerSource 原生区域）+ 四边八向缩放、贴边磁吸 16px、出屏 40px 自动回弹、贴边隐藏（缩 6px 细条）
- **悬停显示**：3.16.1 语义——窗口原地 alpha 淡入淡出永不移动；酷呆式桌面门控（WindowFromPoint + 桌面层进程白名单），别的窗口压着时不抢戏
- **僵尸自愈看门狗**：hwnd 被整理软件连坐销毁 → 自动重建；应显示却不可见/飞出屏幕 → 自动拉回
- **托盘**：左键显隐，右键菜单（设置/刷新/退出），任务栏与 Alt+Tab 不占位
- **设置页独立亚克力窗**：透明度/背景深浅/强调色 5 色/点击打开（详情/播放/搜索）/刷新间隔/鼠标穿透/锁定/悬停/贴边/开播提醒，全部即调即生效
- 深色主题强制根元素（Application.RequestedTheme 在 WinUI3 靠不住）

**依赖**：Win10 19041+，需装 WinAppRuntime 2.2（非自包含）。设置持久化 `%AppData%\AnimeFollowingWidget\settings.json`。

下载：[Releases](https://github.com/Tebio/Anime-following-widget/releases) 中 `v4.0.x-preview` 的 `AnimeWidget-winui-x64.zip`。

## v3.x — C# WPF 主力稳定线

.NET 8 + WPF 单文件 exe：亚克力圆角暗色卡片、周几 pill tabs、WorkerW 壁纸层/置底双模式、
系统代理自动检测、双镜像 failover、离线缓存、托盘全功能 + 开机自启、设置窗实时生效、
悬停隐身（Opacity 动画）、贴边隐藏、整理软件冲突自愈看门狗。

Release 双产物：自包含 ~65MB（免运行时）/ framework-dependent ~0.5MB（需 .NET 8 运行时）。

## 功能（共通）

- **周一~周日放送列表**：每部番的放送时间、最新集数、New! / 完结 标记，自动选中今天
- **点击番名跳转**：详情页 / 最新集播放页 / 搜索页（设置里三选一）
- **收藏星**：行内空心/实心星切换，可只看收藏
- **自动刷新**：默认 30 分钟，离线显示上次缓存
- **数据源**：api.agedm.io/v2（官方 App API）优先，SSR HTML 解析兜底

## 操作方式

| 操作 | 效果 |
|------|------|
| 拖空白处 / 标题文字 | 移动卡片（松手磁吸边缘） |
| 拖四边/四角 8px | 缩放卡片（v4） |
| 左键点番名 | 浏览器打开详情/播放/搜索页 |
| 左键星标 | 收藏/取消收藏 |
| ⚙ / 托盘右键→设置 | 设置面板 |
| 托盘左键 | 显示/隐藏 |

## 目录

| 路径 | 说明 |
|------|------|
| `winui-app/` | **v4 WinUI 3 预览线**（partial class 拆分：Behaviors/Tray/Watchdog/Interop） |
| `csharp-app/` | **v3 WPF 主力线** |
| `anime-widget-rs/` | v2 Rust（存档） |
| `AnimeWidgetDesktop/` | v1 WinForms（存档） |

## 版本

**4.0.x-preview** — WinUI 3 重写线：EnableMsixTooling 修复（缺 .pri 全窗 XamlParseException 六连打不开）、三档材质、原生拖拽/缩放区域、悬停 alpha + 酷呆门控、看门狗自愈、架构收编（partial 拆分）。
**3.x** — WPF 主力线：AGEDM API 直连、播放页直达、隐身/贴边/看门狗全套。
**2.x** — Rust 重写（已存档）。
**1.x** — WinForms 初版（已废弃）。
