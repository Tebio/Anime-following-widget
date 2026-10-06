# shared

v3（`csharp-app/`）与 v4（`winui-app/`）共用的数据层，两个 csproj 通过
`<Compile Include="..\shared\*.cs" LinkBase="Shared" />` 引用。改这里会同时影响两条线。

| 文件 | 内容 |
|---|---|
| `Models.cs` | `Entry` / `DaySchedule` / `WeekSchedule` / `AppSettings` 及设置、缓存的读写 |
| `ScheduleService.cs` | AGE 周表抓取（官方 API 优先，HTML 兜底）、解析、定时刷新 |
| `ProxyDetect.cs` | 系统代理检测 |

约束：只能依赖两个项目都有的包（目前是 HtmlAgilityPack），不得引用 WPF / WinUI 类型。
