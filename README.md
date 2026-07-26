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

## 项目结构

- `AnimeWidgetDesktop/` ：Windows 桌面程序源码（.NET 8 WinForms）
- `feed.sample.json` ：示例更新源
- `.github/workflows/build-windows.yml` ：Windows 单文件 EXE 打包工作流

## 下载 EXE

1. 打开仓库 [Actions](https://github.com/Tebio/Anime-following-widget/actions) 页面
2. 选择最新成功的 **build-windows** 工作流运行
3. 在 Artifacts 中下载 `AnimeWidgetDesktop-win-x64`
4. 解压后运行 `AnimeWidgetDesktop.exe`（自包含，无需安装 .NET）

也可在本机打包（需安装 .NET 8 SDK）：

```powershell
cd AnimeWidgetDesktop
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:EnableCompressionInSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

生成的 EXE 在 `bin\Release\net8.0-windows\win-x64\publish\`。

## 更新源格式

`feed.sample.json` 结构示例：

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

程序默认读取仓库里的 `feed.sample.json`。可在设置里改成你自己的 API、RSS 转换结果，或本地 JSON 路径；留空则使用内置示例数据。

设置文件位置：`%AppData%\AnimeFollowingWidget\settings.json`

## 版本

当前 **1.1.0**（优化 UI、网络请求、卡片强调色条、关闭按钮、窗口圆角等）。
