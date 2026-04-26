# Implementation Roadmap

这份文档只回答一个问题：

**从现在这版 WPF 桌宠原型，怎么一步步走到真正的多线程 personal portfolio RA？**

重点不是列很多想法，而是把顺序排对。

---

## 1. 先看清现在已经有什么

当前这版已经有 5 个可用骨架：

- `pet shell`
  - WPF 桌宠外壳和对话入口已经成立。
- `workspace understanding`
  - 可以读本地目录、读工作区、做 repo-aware skeleton 理解。
- `sticky active workspace`
  - 项目追问不会立刻掉回普通聊天。
- `project cognition`
  - 能把一堆输入压成项目线、`now / next / later`、轻量项目记忆。
- `execution bridge`
  - 能打开 VS Code，也能把工作交给 Codex。

但离目标还差 4 个真正关键的内核：

- `quantified project state`
  - 现在记住的是项目摘要，不是可量化状态。
- `evidence layer`
  - 现在没有稳定证据图谱，很多判断还只是一次性理解结果。
- `review engine`
  - 现在没有真正的主动巡检引擎，之前的 supervision 只是定时提醒。
- `intervention engine`
  - 现在还不会稳当地指出“你偏了，而且应该怎么收回来”。

所以接下来的顺序必须是：

1. 先把项目状态结构化。
2. 再把观察和评分做起来。
3. 再恢复“主动检查进度”。
4. 最后再做“掰正项目”。

---

## 2. 普通对话在这个产品里到底负责什么

普通对话不能再做主链路，只能做外壳。

它只负责 3 件事：

- 接住用户一句自然语言输入
- 在证据不足时做一句澄清
- 把结构化判断翻译成人话

它**不应该**再负责：

- 读项目
- 判进度
- 判风险
- 判主线
- 判纠偏动作

这些都应该来自后台的 workspace / project engine。

---

## 3. 阶段一：把项目记忆升级成“项目状态”

这是第一优先级，也是下一步最该直接动手的地方。

### 目标

把现在的 `ProjectMemory` 从“轻量摘要卡片”升级成“可计算项目状态”。

### 为什么先做这个

因为后面所有能力都依赖它：

- 没有结构化状态，就没法量化进度
- 没有量化进度，就没法做主动 review
- 没有 review，就谈不上纠偏

### 具体要改什么

先扩模型和存储：

- 改 [ProjectMemory.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Models/ProjectMemory.cs)
- 改 [LocalProjectStore.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/LocalProjectStore.cs)
- 新增 2 到 4 个模型文件，例如：
  - `ProjectProgressSnapshot.cs`
  - `ProjectEvidenceItem.cs`
  - `ProjectMilestone.cs`
  - `ProjectBlocker.cs`

建议第一版至少补这些字段：

- `CurrentMilestone`
- `ExpectedDeliverable`
- `LastEvidenceAt`
- `LastMeaningfulProgressAt`
- `MomentumScore`
- `ClarityScore`
- `RiskScore`
- `DriftScore`
- `ConfidenceScore`
- `PrimaryWorkspacePath`
- `Blockers`
- `EvidenceItems`

### 第一阶段退出条件

做到下面 3 件事就算完成：

- 一个项目不再只是 `summary / next action`
- 项目状态能落盘到 `projects.json`
- 新字段可以由现有工作区理解链填充出第一版值

---

## 4. 阶段二：做观察层和证据层

结构化状态有了以后，下一步不是急着写提醒，而是先让它“持续看见东西”。

### 目标

让桌宠对每个项目的判断都尽量有来源，而不是只靠单次对话。

### 观察源应该来自哪

第一版只接 4 个源，别贪多：

- `workspace scan`
  - 来自 [WorkspaceIngestionService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/WorkspaceIngestionService.cs)
- `repo structure`
  - 来自 [RepoStructureReaderService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/RepoStructureReaderService.cs)
- `project cognition`
  - 来自 [ProjectCognitionService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/ProjectCognitionService.cs)
- `codex thread activity`
  - 来自 [LocalCodexThreadIndexService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/LocalCodexThreadIndexService.cs)

### 具体实现建议

新增一个统一入口，例如：

- `ProjectEvidenceCollectorService.cs`

职责很单纯：

- 收集证据
- 去重
- 给证据打时间戳和来源标签
- 更新 `ProjectMemory.EvidenceItems`

第一版不要做太复杂的全文索引。
先把证据做成“最近 10 到 20 条高价值 observation”就够了。

### 第二阶段退出条件

- 每个项目都能看到“最近证据”
- 每个分数都能追溯到至少一种观察来源
- 用户问“你为什么这么判断”时，能给出证据句子而不是泛解释

---

## 5. 阶段三：做真正的 Review Engine

这一步才是恢复“主动检查进度”，但不是恢复以前那种定时催一句。

### 目标

把现在关掉的 supervision 替换成真正的 `ProjectReviewEngine`。

### 为什么放在第三步

因为主动检查如果没有状态和证据，只会继续变成噪音。

### 具体实现建议

新增两个 service：

- `ProjectStateScorer.cs`
- `ProjectReviewEngine.cs`

`ProjectStateScorer` 负责：

- 计算 `MomentumScore`
- 计算 `ClarityScore`
- 计算 `RiskScore`
- 计算 `DriftScore`
- 计算 `ConfidenceScore`

`ProjectReviewEngine` 负责：

- 决定是否需要 review
- 决定 review 触发原因
- 产出 review 结论
- 产出最小下一步建议

触发条件不要再是“每 25 分钟一次”。
第一版只在这些情况下触发：

- 某条主线长时间没有新证据
- 某条主线有 blocker 但没有 owner / next step
- 当前活跃工作和项目目标明显不一致
- 同时开太多线，主线不清
- 用户主动要求盘点

### 代码落点

- 改 [MainWindowViewModel.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/ViewModels/MainWindowViewModel.cs)
- 改 [CompanionPersonaEngine.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/CompanionPersonaEngine.cs)

这里的重点是：

- 定时器可以保留，但只做轻量 wake-up
- 真正是否发声，由 `ProjectReviewEngine` 决定

### 第三阶段退出条件

- 自动 review 不再刷屏
- review 文案背后有明确 trigger reason
- review 结果能落到具体项目和下一步

---

## 6. 阶段四：做“掰正项目”的 Intervention Layer

这是产品真正开始像 RA 的地方。

### 目标

当用户偏了、散了、叙事漂了、项目卡住了时，桌宠不只是看出来，还能把人拉回正轨。

### 第一版 Intervention 只做 4 类动作

- `focus`
  - 告诉用户当前最该推进哪条线
- `freeze`
  - 明确哪些线先暂停，不要继续并行拉扯
- `clarify`
  - 把模糊目标压成 deliverable / next step
- `escalate`
  - 建议把某件事交给 Codex / 打开 VS Code / 继续读工作区

### 具体实现建议

新增：

- `ProjectInterventionService.cs`

它不直接输出大段聊天，而是先产出结构化 intervention：

- `Type`
- `TargetProjectId`
- `Reason`
- `SuggestedAction`
- `Confidence`

然后再由 persona 层翻译成自然语言。

### 第四阶段退出条件

- 它能明确说出“你现在偏在哪里”
- 它能说出“先停什么，先做什么”
- 它的建议能直接变成一个动作，而不是只给一句评价

---

## 7. 阶段五：把多项目组合能力做实

这一步是把单项目理解，真正抬升到 portfolio 级别。

### 目标

让桌宠维护的是“项目组合”，不是一个个孤立的项目摘要。

### 第一版要解决的不是大 dashboard，而是 3 个问题

- 当前哪条是主线
- 哪些项目共享同一批证据或身份叙事
- 哪些项目现在应该静默，不该持续抢注意力

### 建议新增能力

- `portfolio ranking`
- `cross-project drift detection`
- `resume previous thread`

这一步里，`LocalCodexThreadIndexService` 应该从“只读最近项目列表”，升级成“为项目组合提供活动信号”。

### 第五阶段退出条件

- 桌宠能自然地在多项目之间切换
- 它知道哪些线是活跃的，哪些线只是背景上下文
- 它不会把所有项目都当成同等重要

---

## 8. 阶段六：做真实评测，不靠感觉判断

如果不做评测，这个产品最后会退化成“看起来很聪明”。

### 目标

针对你自己的真实项目池，建立一组很小但硬的 eval。

### 第一版评测集就用你已经在跑的项目

- `mainland`
- `EEG`
- `phdapply`
- `singletrans`
- 以及 1 到 2 个更轻的工具型项目

### 每个项目只测 5 类问题

- 它能不能识别主线
- 它能不能说对当前状态
- 它能不能给出可信下一步
- 它能不能发现偏航
- 它能不能在多项目切换后保持上下文

### 成功标准

不是“回答看起来顺”。
而是：

- 结论能不能回到证据
- 建议能不能落到动作
- 多项目切换后会不会失忆

---

## 9. 现在不该先做什么

下面这些东西都很诱人，但都不该排到前面：

- 大型项目面板
- 甘特图
- 很多自动提醒
- 更会聊天的普通对话
- 大量 UI 装饰性迭代
- 复杂自动生成工作流

原因很简单：
这些都可能让产品“更像一个工具”，但不会让它更像真正的 portfolio RA。

---

## 10. 推荐的实际执行顺序

如果只按最务实的路线走，我建议直接按下面 4 个 sprint 来：

### `Sprint 1`

做 `ProjectMemory` 升级和存储迁移。

产出：

- 新状态字段
- 新模型文件
- `projects.json` 兼容迁移

### `Sprint 2`

做 `ProjectEvidenceCollectorService + ProjectStateScorer`。

产出：

- 证据采集
- 项目评分
- 分数来源说明

### `Sprint 3`

做 `ProjectReviewEngine`，替换旧 supervision。

产出：

- 真正的主动 review
- 非刷屏触发逻辑
- review reason

### `Sprint 4`

做 `ProjectInterventionService` 和第一版纠偏动作。

产出：

- focus / freeze / clarify / escalate
- 可执行建议

---

## 11. 如果现在只能做一件事

**先做阶段一：项目状态模型升级。**

因为这是后面所有能力的地基。
不先把项目状态做成可计算对象，后面“主动检查进度”和“掰正项目”都会继续停留在聊天层。
