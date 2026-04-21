# WPF Host Route

这份文档说明为什么当前项目已经正式切到 `WPF-first`，以及当前仓库里的实现状态。

## 为什么选择 WPF 路线

参考现有开源桌宠项目后，方向大致分成三类：

- `WPF / C#`
  - 更偏 Windows 原生桌宠
  - 代表项目：`VPet`
- `Tauri + Web 前端`
  - 更偏跨平台 2D overlay
  - 代表项目：`BongoCat`、`WindowPet`
- `Unity`
  - 更偏 3D 角色和复杂表现
  - 代表项目：`uDesktopMascot`

如果目标是：

- Windows 优先
- 看起来像真正待在桌面上的角色
- 透明无边框
- 小人本体比“应用窗口”更重要

那么 `WPF` 是一条很值得走的宿主路线。

## 当前策略

当前仓库保留两条线：

- `apps/desktop-shell`
  - 现有 `Tauri` 原型
  - 主要保留业务逻辑实验和跨平台参考价值
  - 不再作为正式宿主
- `apps/desktop-shell-wpf`
  - 正式的 Windows-only 桌宠宿主
  - 目标是做成更像 `VPet` 风格的桌面伴侣

## WPF 版当前已落地内容

- `DesktopCompanion.Windows.sln`
- `DesktopCompanion.WpfHost.csproj`
- 透明无边框主窗口
- 启动后吸附到右下角
- 可拖拽
- 小人本体和气泡 UI
- 最小 ViewModel 和情绪轮播占位
- `PetAvatarControl`
- `WindowPlacementService`

## 当前限制

这台机器当前没有 `dotnet` CLI，所以我不能在本机直接运行：

- `dotnet build`
- `dotnet run`

但是项目文件已经按标准 WPF 工程结构手工搭好。你装好 `.NET 8 SDK` 或直接用 Visual Studio 打开后，就可以继续编译验证。

## 推荐下一步

1. 给 WPF 宿主加点击展开的任务面板
2. 给小人加 hover / click / idle 动作
3. 明确业务逻辑是否继续保留 TypeScript，还是逐步转 C#
4. 开始补输入、任务、情绪的真正闭环
