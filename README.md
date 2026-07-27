# Anime-following-widget

Windows 桌面追番小组件。**v2.0 Rust 完全重写**：单文件 EXE、免安装、数据直连 AGE 动漫周表。

原 C# 版保留在 `AnimeWidgetDesktop/`（已废弃，仅存档）。

## 功能

- **周一~周日放送列表**：直接抓取 [AGE 动漫](https://www.agedm.io/) 首页「本周放送列表」，显示每部番的放送时间、最新集数、New! / 完结 标记，自动选中今天
- **点击番名跳转搜索**：点「凡人修仙传」→ 浏览器打开 `https://www.agedm.io/search?query=凡人修仙传`
- **真·桌面挂件**：窗口嵌入桌面壁纸层（WorkerW），打开其他窗口会被盖住，Win+D 仍可见，不占任务栏 / Alt-Tab
- **可拖拽 + 边缘磁吸**：拖到屏幕边缘自动吸附（8px 边距），位置记忆
- **鼠标穿透**：开启后点击穿过卡片直达桌面；配合系统托盘图标控制
- **桌面锁定**：锁定位置禁止拖拽
- **外观可调**：整体透明度、背景深浅、5 种强调色（青绿/香槟金/雾紫/樱粉/暖橙）
- **自动刷新**：默认 30 分钟抓一次周表，离线时显示上次缓存
- **单文件**：静态链接，无需 .NET / 运行时，中文字体运行时读系统字体（雅黑），EXE 不内嵌字体

## 操作方式

| 操作 | 效果 |
|------|------|
| 左键拖空白处 / 番名 | 移动卡片（松手自动磁吸边缘） |
| 左键点番名 | 浏览器打开 AGE 搜索页 |
| 右键卡片 | 快捷菜单（锁定 / 穿透 / 刷新 / 退出） |
| ⚙ / 托盘双击 | 设置面板 |
| 托盘右键 | 锁定位置 / 鼠标穿透 / 设置 / 刷新 / 退出 |

设置持久化在 `%AppData%\AnimeFollowingWidget\settings.json`。

## 下载

1. [Actions → build-rust-windows](https://github.com/Tebio/Anime-following-widget/actions/workflows/build-rust-windows.yml) 下载 Artifact `AnimeWidget-rust-win-x64`
2. 或 [Releases](https://github.com/Tebio/Anime-following-widget/releases) 中的 `AnimeWidget-win-x64.exe`

发版：Actions → **build-rust-windows** → Run workflow → 勾选 `create_release`，tag 如 `v2.0.0`。

## 本地编译（Rust）

需安装 [Rust](https://rustup.rs/)：

```powershell
cd anime-widget-rs
cargo build --release
# 产物：target\release\anime-widget.exe
```

测试（含真实 agedm 页面解析回归）：

```powershell
cargo test
```

## 技术要点

- eframe/egui 无边框透明窗口
- Win32：`SetParent` 挂到 WorkerW 壁纸层；`WS_EX_TRANSPARENT` 穿透；`SetLayeredWindowAttributes` 透明度
- 周表解析：scraper 解析 agedm 首页 SSR HTML（`#week-N-pane` 七个分页），无需 API
- TLS：Windows 走系统 Schannel，无 openssl/ring，单文件干净

## 目录

| 路径 | 说明 |
|------|------|
| `anime-widget-rs/` | **Rust 主程序** |
| `anime-widget-rs/tests/fixtures/agedm_home.html` | 真实 agedm 首页样本（解析回归测试） |
| `AnimeWidgetDesktop/` | 旧 C# WinForms 版（存档） |

## 版本

**2.0.0** — Rust 完全重写：agedm 直连周表、桌面壁纸层嵌入、鼠标穿透、边缘磁吸、托盘菜单、设置面板。
**1.x** — 初版（JSON 数据源方案，已废弃）。
