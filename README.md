# Anime-following-widget

Windows 桌面追番小组件。**当前主实现为 Rust**（`anime-widget-rs/`），单文件 EXE、免安装。

原 C# 版保留在 `AnimeWidgetDesktop/`（可继续用 Actions 的 `build-windows` 打包）。

## 功能

- 无边框圆角卡片、可拖动、半透明、始终置顶
- 深色 / 浅色主题 + 强调色（紫 / 青 / 绿 / 粉 / 橙）
- 点击条目打开播放页（优先 AgeDM：`/play/{id}/{season}/{ep}`，否则 `watchUrl`）
- 「今日更新」动态提示（按 `updateWeekday`）
- JSON 数据源：HTTP(S) / 本地文件 / 内置示例
- 设置持久化到 `%AppData%\\AnimeFollowingWidget\\settings.json`

## 下载（Rust 单文件）

1. [Actions → build-rust-windows](https://github.com/Tebio/Anime-following-widget/actions/workflows/build-rust-windows.yml) 下载 Artifact `AnimeWidget-rust-win-x64`
2. 或 [Releases](https://github.com/Tebio/Anime-following-widget/releases) 中的 `AnimeWidget-win-x64.exe`

发版：Actions → **build-rust-windows** → Run workflow → 勾选 `create_release`，tag 如 `v1.2.0`。

## 本地编译（Rust）

需安装 [Rust](https://rustup.rs/)：

```powershell
cd anime-widget-rs
cargo build --release
# 产物：target\\release\\anime-widget.exe
```

## 数据格式

```json
{
  "title": "本周放送列表",
  "updatedAt": "2026-07-26T17:00:00+08:00",
  "items": [
    {
      "title": "凡人修仙传",
      "badge": "New",
      "time": "23:00",
      "episode": "第184集",
      "latestEpisode": 184,
      "platform": "B站",
      "watchUrl": "https://www.agedm.io/play/20200283/1/184",
      "animeId": "20200283",
      "seasonNumber": 1,
      "updateWeekday": 0,
      "notes": "每周日更新"
    }
  ]
}
```

- `updateWeekday`：0=周日 … 6=周六
- 有 `animeId` 时按 AgeDM 模板打开；否则用 `watchUrl`

## 目录

| 路径 | 说明 |
|------|------|
| `anime-widget-rs/` | **Rust 主程序**（eframe/egui） |
| `AnimeWidgetDesktop/` | 旧 C# WinForms 版 |
| `feed.sample.json` / `anime-widget-rs/feed.sample.json` | 示例源 |

## 版本

**1.2.0** — Rust 重写：AgeDM 链接、今日更新、单文件发布。
