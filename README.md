# Anime-following-widget

一个 Windows 桌面番剧更新卡片小组件。

功能：
- 置顶、半透明、圆角卡片感
- 主题切换
- 强调色切换
- 点击番剧条目直接打开播放链接
- 自动从 JSON 更新源拉取最新列表

## 项目结构

- `AnimeWidgetDesktop/`：Windows 桌面程序源码
- `feed.sample.json`：示例更新源
- `.github/workflows/build-windows.yml`：Windows 打包工作流

## 本地打包

在 Windows 上安装 .NET 8 SDK 后执行：

```powershell
cd AnimeWidgetDesktop
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

生成的 EXE 会在 `bin\Release\net8.0-windows\win-x64\publish\` 里。

## 更新源格式

`feed.sample.json` 的结构如下：

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
      "watchUrl": "https://example.com"
    }
  ]
}
```

程序默认读取仓库里的 `feed.sample.json`，你后面可以把它改成你自己的 API、RSS 转换结果，或者本地文件路径。
