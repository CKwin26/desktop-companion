# Project Repo Structure

这份文档现在回答两个问题：

1. 为什么这个项目正式选择 `WPF`
2. 为什么仍然值得保留 `monorepo` 结构

`Tauri` 仍然保留在仓库里，但它已经降级为原型和参考实现，不再是主宿主路线。

## 1. 先用一句话理解

- `WPF`
  - 用来做 Windows 原生桌面界面
  - 负责透明桌宠窗口、点击交互、桌面悬浮感
- `monorepo`
  - 用来把多个模块放在一个仓库里统一管理
  - 负责代码组织，不负责桌面能力

## 2. 为什么这个项目适合用 WPF

你的产品现在已经明确是：

- Windows-only
- 桌面右下角常驻小人
- 看起来像桌宠，而不是应用窗口

这类产品更看重：

- 透明无边框宿主
- 小人本体表现
- 原生拖拽和桌面贴边
- Windows 桌面上的“存在感”

这些能力 `WPF` 做起来通常比网页壳更自然。

WPF 在这个项目里就是：

- 用 `XAML` 描述桌宠界面
- 用 `C#` 写窗口行为、动画和桌面交互
- 最终打包成 Windows 原生桌面应用

## 3. 为什么这个项目适合用 monorepo

即使我们选了 WPF，仓库仍然不该退化成“只有一个桌面工程”。

这个项目天然还是有多层：

1. `desktop-shell-wpf`
2. `companion logic`
3. `ai-provider`

如果把它们全塞在一个 WPF 工程里，后面也会很快乱掉：

- 窗口代码和监督逻辑混在一起
- AI 接入和情绪状态机耦合
- 后面切换本地模型 / 远程模型会很痛苦
- 开源协作时别人不容易只改宿主或引擎

所以仓库结构仍然应该分层，只是宿主从 `Tauri` 换成了 `WPF`。

## 4. 推荐目录结构

```txt
mainland/
  apps/
    desktop-shell-wpf/
      App.xaml
      MainWindow.xaml
      Controls/
      Models/
      Services/
      ViewModels/
      DesktopCompanion.WpfHost.csproj

    desktop-shell/
      ...
      # 旧 Tauri 原型，保留参考价值，不再作为主宿主

  packages/
    shared-types/
      src/
        index.ts
        task.ts
        reminder.ts
        emotion.ts
        review.ts
        companion.ts
      package.json

    companion-engine/
      src/
        index.ts
        event-bus/
        intent-parser/
        task-state-machine/
        emotion-engine/
        reminder-scheduler/
        review-engine/
        dialog-policy/
        storage/
      package.json

    ai-provider-none/
      src/
        index.ts
        rules.ts
        templates.ts
      package.json

    ai-provider-ollama/
      src/
        index.ts
        client.ts
        prompts.ts
      package.json

    ai-provider-openai/
      src/
        index.ts
        client.ts
        prompts.ts
      package.json

  docs/
    mvp-architecture.md
    repo-structure.md

  package.json
  pnpm-workspace.yaml
  turbo.json
  tsconfig.base.json
  .gitignore
  README.md
```

## 5. 每一层到底干什么

### `apps/desktop-shell-wpf`

这是最终用户真正看到的桌面应用。

它负责：

- 透明桌宠主窗口
- 小人本体 UI
- 展开面板 UI
- 快捷输入入口
- 桌面定位、拖拽、置顶
- 后续系统托盘、通知、开机启动

它不应该负责：

- 任务状态规则
- 情绪推导规则
- AI 解析逻辑

### `packages/shared-types`

这是所有模块共用的数据定义。

它负责：

- `Task`
- `Reminder`
- `EmotionSnapshot`
- `ReviewDigest`
- `CompanionState`
- 公共枚举和接口

它的作用是让所有模块都说同一种“数据语言”。

### `packages/companion-engine`

这是产品的大脑。

它负责：

- 解析用户动作
- 改变任务状态
- 计算当前情绪
- 决定何时提醒
- 生成阶段梳理
- 输出给 Shell 的显示状态

这层最重要。

### `packages/ai-provider-none`

这是默认 provider。

它负责：

- 不调用任何模型
- 靠规则和模板完成基础解析
- 保证离线也能用

它是 MVP 必须先做好的底座。

### `packages/ai-provider-ollama`

这是本地模型增强层。

它负责：

- 本地自然语言解析增强
- 梳理文案增强
- 更自然的角色回复

### `packages/ai-provider-openai`

这是远程模型增强层。

它负责：

- 兼容 OpenAI 风格接口
- 后面也能兼容很多“OpenAI-compatible”服务

### `apps/desktop-shell`

这是旧的 `Tauri` 原型。

它现在的职责是：

- 保留前端业务实验价值
- 作为跨平台思路参考
- 给我们提供早期状态机和交互原型

它不再是主宿主。

## 6. `desktop-shell-wpf` 内部建议结构

`apps/desktop-shell-wpf` 后续建议这样拆：

```txt
apps/desktop-shell-wpf/
  App.xaml
  MainWindow.xaml
  Controls/
    PetAvatarControl.xaml
  Models/
    PetMood.cs
  Services/
    WindowPlacementService.cs
  ViewModels/
    MainWindowViewModel.cs
  Views/
    PanelWindow.xaml
    QuickInputWindow.xaml
```

说明：

- `Views/`
  - 放各个窗口和面板
- `Controls/`
  - 放可复用桌宠组件
- `services/`
  - 放窗口定位、通知、托盘等系统封装
- `ViewModels/`
  - 放状态绑定和命令
- `Models/`
  - 放情绪、任务等基础模型

## 7. WPF 宿主和业务层怎么连接

短期内我们先把桌宠宿主用 `C# / WPF` 做稳。

业务层后面有两种路径：

1. 全部转到 C#
2. 保留现有 TypeScript 引擎设计，后续再做本地桥接

当前更推荐先把宿主和最小逻辑都放在 WPF 里，减少跨语言复杂度。

## 8. 数据流怎么走

最简单的一条链路：

```txt
用户在 quick-input 输入一句话
  -> desktop-shell-wpf 接收输入
  -> 调用 companion-engine
  -> companion-engine 调 provider 解析
  -> 生成任务或提醒
  -> 更新存储
  -> 输出新的 companion state
  -> desktop-shell-wpf 刷新挂件和面板
```

所以职责边界很清楚：

- Shell 负责显示和系统交互
- Engine 负责业务逻辑
- Provider 负责语言理解增强

## 9. MVP 第一版真正要建哪些目录

第一版不要一口气建满，先建这些就够了：

```txt
apps/
  desktop-shell-wpf/

packages/
  shared-types/
  companion-engine/
  ai-provider-none/

docs/
```

也就是说：

- 先不建 `ollama`
- 先不建 `openai`
- 先不拆太细的窗口
- 先把最小闭环跑通

## 10. 适合你的最小开工顺序

1. 建 monorepo 根目录
2. 建 `shared-types`
3. 建 `companion-engine`
4. 建 `ai-provider-none`
5. 建 `desktop-shell-wpf`
6. 先把透明桌宠主窗口跑起来
7. 打通“输入一句话 -> 新任务出现”

## 11. 你可以怎么理解这个仓库

可以把整个仓库想成一个小公司：

- `desktop-shell-wpf`
  - 前台和门面
- `companion-engine`
  - 业务中台和大脑
- `ai-provider-*`
  - 外部顾问
- `shared-types`
  - 全公司的统一表格和字段规范

仓库里还保留 `desktop-shell` 这个 Tauri 原型，但它现在更像旧概念稿，而不是正式宿主。
