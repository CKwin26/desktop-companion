# Desktop Companion MVP Architecture

这是“桌面常驻的情绪化任务伴侣”第一阶段的架构草案。

目标：

- 先做成可开源、可本地运行、可扩展的桌面产品
- 先保证没有 AI 也能工作
- AI 只做增强层，不做唯一依赖

## 1. MVP 范围

第一阶段只做以下闭环：

1. 用户通过自然语言或快速表单添加任务
2. 任务进入状态机流转：待办 / 进行中 / 阻塞 / 完成 / 延后
3. 角色根据任务变化产生情绪反馈
4. 调度器按节奏发提醒
5. 梳理引擎定期生成阶段总结
6. Shell 把这些结果展示为桌面挂件、小窗和通知

## 2. 模块分层

### Shell

Shell 负责“活在桌面上”。

- `wpf-host`
  - Windows 原生透明宿主
  - 负责桌面存在感和窗口交互
- `widget-window`
  - 桌面常驻小挂件
  - 展示角色头像、情绪、当前状态文案
- `panel-window`
  - 点击挂件后展开的主面板
  - 展示任务列表、梳理结果、历史记录
- `quick-input-window`
  - 像输入法一样的快速输入条
  - 用一句话录入任务和命令
- `tray`
  - 系统托盘图标
  - 提供显示/隐藏、专注模式、退出等菜单
- `window-manager`
  - 吸边、置顶、位置记忆、展开收起、拖拽
- `notification-adapter`
  - 系统通知
  - 任务提醒、专注结束、阶段梳理提示
- `shell-bridge`
  - WPF ViewModel 和业务层同步桥
  - 接收 Engine 输出并驱动 UI 更新

### Companion Engine

Companion Engine 负责“像个有性格的监督员”。

- `task-model`
  - 任务、提醒、梳理结果、角色状态的数据定义
- `task-state-machine`
  - 任务状态流转规则
- `intent-parser`
  - 将自然语言转为结构化命令
- `emotion-engine`
  - 根据事件和上下文决定角色情绪
- `reminder-scheduler`
  - 管理提醒时间、节奏、安静时段、延后逻辑
- `review-engine`
  - 生成阶段梳理和今日总结
- `dialog-policy`
  - 决定说什么、什么时候说、语气轻重
- `storage`
  - 本地持久化
- `event-bus`
  - 模块间统一事件流

### AI Provider

AI Provider 负责“更会听懂人话、更会梳理”。

- `none`
  - 规则解析
  - 模板回复
  - 保证无模型也可用
- `ollama`
  - 本地模型增强
- `openai-compatible`
  - 兼容 OpenAI 风格 API 的远程模型

所有 Provider 必须实现统一接口。

## 3. MVP 模块图

```txt
User
  |
  v
Shell
  |- wpf-host
  |- widget-window
  |- panel-window
  |- quick-input-window
  |- tray
  |- notification-adapter
  |
  v
shell-bridge
  |
  v
Companion Engine
  |- intent-parser
  |- task-state-machine
  |- reminder-scheduler
  |- emotion-engine
  |- review-engine
  |- dialog-policy
  |- storage
  |- event-bus
  |
  +--> AI Provider
         |- none
         |- ollama
         |- openai-compatible
```

## 4. 事件流

### 4.1 添加任务

```txt
用户输入一句话
  -> quick-input-window
  -> shell-bridge.submitInput(text)
  -> intent-parser.parse(text)
  -> 生成 TaskCreateCommand
  -> task-state-machine.createTask()
  -> storage.saveTask()
  -> event-bus.emit(task.created)
  -> emotion-engine.evaluate(task.created)
  -> dialog-policy.compose()
  -> Shell 更新挂件 / 面板 / 通知
```

### 4.2 状态更新

```txt
用户点击“开始 / 阻塞 / 完成 / 延后”
  -> panel-window
  -> shell-bridge.dispatch(action)
  -> task-state-machine.transition(taskId, action)
  -> storage.saveTask()
  -> event-bus.emit(task.updated)
  -> emotion-engine.evaluate(task.updated)
  -> reminder-scheduler.reschedule(taskId)
  -> Shell 刷新 UI
```

### 4.3 定时提醒

```txt
reminder-scheduler.tick()
  -> 找到到期提醒
  -> event-bus.emit(reminder.due)
  -> emotion-engine.evaluate(reminder.due)
  -> dialog-policy.compose()
  -> notification-adapter.notify()
  -> widget-window 显示催促状态
```

### 4.4 阶段梳理

```txt
review-engine.run()
  -> 读取 tasks / reminders / recent events
  -> 生成 review digest
  -> event-bus.emit(review.created)
  -> emotion-engine.evaluate(review.created)
  -> dialog-policy.compose()
  -> panel-window 展示梳理卡片
  -> 可选发送系统通知
```

## 5. 任务状态机

MVP 状态：

- `todo`
- `doing`
- `blocked`
- `done`
- `snoozed`

允许流转：

```txt
todo    -> doing | blocked | done | snoozed
doing   -> todo | blocked | done | snoozed
blocked -> todo | doing | done | snoozed
snoozed -> todo | doing
done    -> todo
```

状态含义：

- `todo`
  - 已存在，但未正式开工
- `doing`
  - 当前主线任务，角色重点盯
- `blocked`
  - 明确卡住，需要解释或拆解
- `done`
  - 已完成
- `snoozed`
  - 被明确延后，暂时不催

## 6. 情绪状态机

MVP 情绪：

- `idle`
- `happy`
- `focused`
- `concerned`
- `urgent`
- `sleepy`

推荐映射：

- 新建任务
  - `focused`
- 有任务推进
  - `focused`
- 完成任务
  - `happy`
- 出现阻塞
  - `concerned`
- 任务逾期或连续未更新
  - `urgent`
- 长时间无事件
  - `sleepy`

情绪输出结构包含：

- 当前情绪标识
- 强度等级
- 推荐文案风格
- 是否允许系统通知

## 7. AI Provider 接口

```ts
export interface AIProvider {
  name: string;
  isAvailable(): Promise<boolean>;
  parseIntent(input: IntentInput): Promise<IntentParseResult>;
  generateReply(input: ReplyInput): Promise<ReplyResult>;
  buildReview(input: ReviewInput): Promise<ReviewResult>;
}
```

### `none` Provider

职责：

- 正则和关键词识别
- 时间短语解析
- 固定模板生成角色发言

示例：

- “半小时后提醒我继续写周报”
  - 解析为 `create reminder + bind task`
- “这个先放一放”
  - 解析为 `snooze task`
- “我卡住了”
  - 解析为 `mark blocked`

## 8. 数据结构定义

### 8.1 Task

```ts
export type TaskStatus = "todo" | "doing" | "blocked" | "done" | "snoozed";
export type TaskPriority = "low" | "medium" | "high";

export interface Task {
  id: string;
  title: string;
  description?: string;
  status: TaskStatus;
  priority: TaskPriority;
  createdAt: string;
  updatedAt: string;
  dueAt?: string;
  snoozedUntil?: string;
  estimatedMinutes?: number;
  category?: string;
  tags: string[];
  latestNote?: string;
  source: "quick-input" | "panel" | "ai";
}
```

### 8.2 Reminder

```ts
export interface Reminder {
  id: string;
  taskId?: string;
  title: string;
  remindAt: string;
  repeatRule?: string;
  channel: "widget" | "system" | "both";
  dismissedAt?: string;
  createdAt: string;
}
```

### 8.3 Emotion Snapshot

```ts
export type EmotionName =
  | "idle"
  | "happy"
  | "focused"
  | "concerned"
  | "urgent"
  | "sleepy";

export interface EmotionSnapshot {
  name: EmotionName;
  intensity: 1 | 2 | 3;
  reason: string;
  updatedAt: string;
}
```

### 8.4 Review Digest

```ts
export interface ReviewDigest {
  id: string;
  createdAt: string;
  headline: string;
  summary: string;
  bullets: string[];
  topTaskId?: string;
  riskTaskIds: string[];
}
```

### 8.5 Companion State

```ts
export interface CompanionState {
  petName: string;
  emotion: EmotionSnapshot;
  activeTaskId?: string;
  reminderMinutes: number;
  reviewMinutes: number;
  focusMinutes: number;
  quietHours?: {
    start: string;
    end: string;
  };
  provider: "none" | "ollama" | "openai-compatible";
}
```

## 9. 建议的 Monorepo 目录

```txt
apps/
  desktop-shell-wpf/
    App.xaml
    MainWindow.xaml
    Controls/
    Models/
    Services/
    ViewModels/
  desktop-shell/
    ...
    # 旧 Tauri 原型，保留参考价值
packages/
  companion-engine/
    src/
      event-bus/
      task-state-machine/
      reminder-scheduler/
      emotion-engine/
      review-engine/
      dialog-policy/
      intent-parser/
      storage/
  ai-provider-none/
    src/
  ai-provider-ollama/
    src/
  ai-provider-openai/
    src/
  shared-types/
    src/
docs/
  mvp-architecture.md
```

## 10. 第一阶段开发顺序

1. `shared-types`
2. `companion-engine`
3. `ai-provider-none`
4. `desktop-shell-wpf` 的单窗口版本
5. 通知、托盘、快速输入条
6. `ollama` Provider

## 11. 第一阶段不做

先不要做这些：

- 多角色切换
- 云同步
- 多用户
- 复杂插件市场
- 远程协作
- 语音输入
- 3D Live2D 形象

这些很有吸引力，但都会把 MVP 拉散。

## 12. 一句话定义

第一阶段的产品不是“万能 AI 助手”。

第一阶段是：

**一个能挂在桌面上、听得懂自然语言、会用情绪反馈监督你推进任务的原创桌面伴侣。**
