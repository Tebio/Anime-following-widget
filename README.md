# Anime-following-widget

一个 Windows 桌面番剧更新卡片小组件。

## 功能

- 无边框圆角卡片、可拖动、半透明
- 始终置顶 / 主题切换（深色 / 浅色）
- 强调色切换（紫 / 青 / 绿 / 粉 / 橙）
- 点击番剧条目直接打开播放链接
- 自动从 JSON 更新源拉取最新列表（支持 HTTP(S) 与本地文件）
- 窗口位置 / 大小 / 透明度自动记忆
- 右上角关闭按钮

## 下载（单文件 / 免安装）

构建产物是 **单文件自包含 EXE**：内嵌 .NET 运行时，**无需安装 .NET**，下载后双击即可运行。

### 方式一：Releases（推荐）

打开 [Releases](https://github.com/Tebio/Anime-following-widget/releases) ，下载：

- `AnimeWidgetDesktop-win-x64.exe` — 单文件可执行程序
- 或 `AnimeWidgetDesktop-win-x64-singlefile.zip` — 同上的压缩包

### 方式二：Actions Artifact

1. 打开 [Actions](https://github.com/Tebio/Anime-following-widget/actions)
2. 选最新成功的 **build-windows**
3. 下载 Artifact：`AnimeWidgetDesktop-win-x64-singlefile`

### 发布新版本到 Releases

**手动触发（推荐）：**

1. Actions → **build-windows** → **Run workflow**
2. 勾选 `create_release`
3. `release_tag` 填例如 `v1.1.0`
4. 运行完成后到 [Releases](https://github.com/Tebio/Anime-following-widget/releases) 查看

**或推送 tag：**

```bash
git tag v1.1.0
git push origin v1.1.0
```

推送 `v*` tag 后会自动构建并创建 GitHub Release，附带单文件 EXE。

### 本地打包

需安装 .NET 8 SDK：

```powershell
cd AnimeWidgetDesktop
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:EnableCompressionInSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

生成目录：`bin\Release\net8.0-windows\win-x64\publish\`

## 项目结构

- `AnimeWidgetDesktop/` ：Windows 桌面程序源码（.NET 8 WinForms）
- `feed.sample.json` ：示例更新源
- `.github/workflows/build-windows.yml` ：单文件 EXE 打包 + Release 工作流

## 更新源格式

```json
{
  "title": "本周放送列表",
  "updatedAt": "2026-07-26T17:00:00+08:00",
  "items": [
    {
      "title": "番剧名称",
      "badge": "New",
      "time": "23:00",
      "episode": "第04集",
      "platform": "B站",
      "watchUrl": "https://example.com",
      "notes": "可选备注"
    }
  ]
}
```

程序默认读取仓库 `feed.sample.json`。可在设置里改成自己的 API / 本地 JSON；留空用内置示例。

设置文件：`%AppData%\AnimeFollowingWidget\settings.json`

## 版本

当前 **1.1.0**（单文件免安装、UI 优化、关闭按钮、强调色条、圆角等）。
