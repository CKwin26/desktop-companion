# Project Scorer Refactor Plan

## 1. Why the current scorer is still not right

当前这版打分已经比最早的 `heuristic summary / next action` 强很多，但本质上还是：

- 先看本地结构化状态
- 再按关键词把项目粗分成几类
- 最后沿用同一套打分公式

这会带来 4 个问题：

1. 它更像“项目类型启发式”，还不是“项目机制评分”。
2. `NeedSearch` 很大程度由关键词桶决定，而不是由项目此刻真正缺什么决定。
3. `phdapply`、`singletrans`、`mainland` 这种本质差异很大的项目，还在被同一套总公式硬算。
4. 外部信号、blocker、deliverable、recent Codex thread 还没有按项目家族分别解释。

一句话：

**现在它是在“给项目分类后打分”，不是“按项目真实工作方式打分”。**

---

## 2. Refactor target

把当前统一 scorer 重构成：

- 一个 `ProjectArchetypeResolver`
- 六个产品主桶，其中前三个深度 scorer 优先落地
- 一个保留的共享 evidence/external pipeline

目标不是让代码更花，而是让：

- `phdapply` 按申请运营逻辑算
- `research / benchmark` 项目按研究证据逻辑算
- `engineering / repo` 项目按执行推进逻辑算

---

## 3. Proposed architecture

### 3.1 New shared layer

新增：

- `ProjectArchetypeResolverService.cs`
- `ProjectArchetype` enum

当前正式 archetype：

- `ApplicationOps`
- `ResearchEvaluation`
- `EngineeringExecution`
- `ProductResearch`
- `OperationsAdmin`
- `LifeEntertainment`
- `General`

`ProjectMemory` 建议新增：

- `ArchetypeLabel`
- `ArchetypeConfidence`
- `ArchetypeReason`

这样后面不是每次重新猜，而是：

- 第一次识别 archetype
- 后续按 archetype 走对应 scorer
- 如果证据变化很大，再允许 reclassify

### 3.2 New scorer interface

新增：

```csharp
public interface IProjectScoringProfile
{
    ProjectArchetype Archetype { get; }
    bool CanScore(ProjectMemory project);
    ProjectStateAssessment Score(
        ProjectMemory project,
        ProjectStateAssessment internalAssessment,
        ProjectExternalSignalSnapshot externalSignals);
}
```

然后实现：

- `ApplicationOpsScoringProfile.cs`
- `ResearchEvaluationScoringProfile.cs`
- `EngineeringExecutionScoringProfile.cs`
- `ProductResearchScoringProfile.cs`
- `OperationsAdminScoringProfile.cs`
- `LifeEntertainmentScoringProfile.cs`

再由一个 orchestrator 负责：

- 根据 `ArchetypeResolver` 选 profile
- 调 profile 输出最终 assessment
- 回填 `ProjectMemory`

当前的 [ProjectStateScorer.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/ProjectStateScorer.cs) 不再直接负责所有项目，而是变成：

- `SharedScoreMath`
- 或者直接拆散进各 profile

---

## 4. The scorer families

## 4.1 `ApplicationOps` scorer

### Target projects

- `phdapply`
- 后续任何学校申请、奖项申请、基金申请、材料投递型项目

### What this scorer actually cares about

不是“这个项目像 research 吗”，而是：

- 申请线程是不是清楚
- 每个目标对象是不是在推进
- 材料是不是成型
- 关键依赖是不是闭环
- 截止时间和提交流程是不是在逼近

### Core dimensions

- `ThreadCoverage`
  - 有多少学校/申请线程被明确维护
- `MaterialReadiness`
  - SOP / CV / Proposal / Recommenders / School-specific 材料是否到位
- `DependencyClosure`
  - 推荐信、提交记录、证明材料、证据链是否闭环
- `DeadlinePressure`
  - 截止/提交节点是否逼近
- `NarrativeStability`
  - 主叙事是否稳定，是否一直在漂

### What should drive scores

`ExecutionHealth`

- 材料线程是否有明确 next step
- 是否存在 school-specific deliverable
- 最近是否真有推进证据

`Risk`

- 有没有缺关键材料
- 有没有未闭环依赖
- 有没有 deadline pressure
- 有没有 blocker 无 owner

`Confidence`

- 当前判断是否基于真实材料文件和提交记录
- 是否已经采集到学校/项目官方要求

`Drift`

- 当前动作是否还服务于申请主叙事
- 是否出现“做了很多，但没有明确投递对象”的漂移

### Search policy

申请类项目不该简单按关键词高搜，而该按下面情况触发：

- 新学校线程出现，但没有官方要求证据
- 材料叙事改动大，但没有外部要求校准
- 某条申请线接近提交，却没有最新 requirement evidence

### Good output style

它最后应该像这样说：

- 你现在主推进的是哪几个学校线程
- 哪份材料最缺
- 哪个依赖没闭环
- 先补哪一个洞，而不是泛泛说“风险偏高”

---

## 4.2 `ResearchEvaluation` scorer

### Target projects

- `singletrans` 里 benchmark / evaluation / blind set / ablation 这类线
- `learned_slot`
- 任何以 benchmark、论文、实验、分析、评估为核心的项目

### What this scorer actually cares about

不是“这个 repo 像研究”，而是：

- 研究问题是否明确
- 评估设置是否可靠
- 证据链是否站得住
- 指标和结论是否匹配
- 是否存在 hidden failure mode

### Core dimensions

- `QuestionClarity`
  - 问题、假设、目标是否清楚
- `EvaluationIntegrity`
  - benchmark、blind set、ablation、comparison 是否成型
- `EvidenceStrength`
  - 是否有真实实验输出、结果表、图、日志、论文证据
- `Reproducibility`
  - 是否可复现，配置/脚本/参数是否明确
- `InterpretationStability`
  - 当前结论是不是经得起外部标准和文献校对

### What should drive scores

`ExecutionHealth`

- 是否有明确实验下一步
- 是否有最近新增证据
- 是否已经跑出核心评估结果

`Risk`

- benchmark 不完整
- blind set / ablation 缺失
- 结果和解释脱节
- freshness 太差，说明结论可能已经过时

`Confidence`

- 本地 evidence 强不强
- 外部 benchmark / literature / best practice 是否已对齐

`Drift`

- 最近动作是在补证据，还是在绕开真正评估问题
- 是否从“研究问题”漂成了“做很多工程杂活”

### Search policy

research scorer 的外搜不该只是搜项目名，而该搜：

- benchmark conventions
- failure modes
- comparable papers / baselines
- external critique / best practice

### Good output style

它最后应该更像：

- 当前问题是什么
- 缺哪类证据
- 现有结果还能不能支撑结论
- 下一步是补实验、补对照、还是收束叙事

---

## 4.3 `EngineeringExecution` scorer

### Target projects

- `mainland`
- `image_process`
- 一般 repo / app / service / tooling 项目

### What this scorer actually cares about

不是“有没有 repo 结构”，而是：

- 当前工作线是否清楚
- 改动是不是落在真实文件和线程上
- blocker 是否明确
- repo 当前有没有推进信号
- 是否需要外部标准来校准实现方向

### Core dimensions

- `WorkstreamClarity`
  - 当前主线、入口、模块边界是否明确
- `ExecutionSignal`
  - 最近 Codex thread、文件改动、next step 是否成型
- `BlockerSharpness`
  - 阻塞是否明确，而不是模糊卡住
- `RepoGroundedness`
  - 判断是否真的基于 repo structure / files / thread evidence
- `ExternalNeed`
  - 这个工程问题是否真的需要外部资料，而不是能在本地 repo 里解决

### What should drive scores

`ExecutionHealth`

- 最近线程是不是活跃
- next action 是否清晰
- repo evidence 是否足够

`Risk`

- blocker 数
- 没有 next step
- workspace stale
- 外部 required 但还没补

`Confidence`

- repo structure + local docs + recent Codex threads 是否同时存在
- 当前判断有没有具体文件依据

`Drift`

- 最近线程和 deliverable 是否一致
- 是否长期在改边角而不是主线

### Search policy

工程项目默认不应高频外搜。  
只有这些情况才该明显加大外搜权重：

- 涉及外部标准、规则、API、最新产品要求
- 本地 repo 无法回答“正确做法是什么”
- 用户在问“别人怎么评价 / 最佳实践 / 官方要求”

### Good output style

它最后应该像：

- 当前主线是什么
- 最近线程在做什么
- repo 里哪个模块最关键
- 下一步应该继续读文件、开 VS Code，还是交给 Codex

---

## 5. How current projects should map

按你现在这台机器上的真实项目，我建议第一版这样映射：

- `phdapply` -> `ApplicationOps`
- `singletrans` 的 benchmark / blind-set / ablation 线程 -> `ResearchEvaluation`
- `learned_slot` -> `ResearchEvaluation`
- `mainland` -> `EngineeringExecution`
- `image_process` -> `EngineeringExecution`
- `EEG`
  - 如果在做机械结构、产品定义、专利/方案整理 -> 先落 `ProductResearch`
  - 如果在做实验验证、对比分析、研究叙事 -> 再允许切 `ResearchEvaluation`
- `C:` / `Ooni` / `PA` 这类清理、搬盘、报告导出、包装交付 -> `OperationsAdmin`
- 轻提醒、兴趣、放松、文娱偏好 -> `LifeEntertainment`

也就是说：

**第一版不要试图完美支持 hybrid。先允许一个主 archetype，再支持人工切换。**

---

## 6. What should stay shared

不是所有东西都要拆。

下面这些可以继续共享：

- [RepoStructureReaderService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/RepoStructureReaderService.cs)
- workspace ingestion
- Codex thread sync
- evidence item 存储
- external reference 抓取器

要变的是：

- internal assessment 的解释逻辑
- need-search 的触发逻辑
- final score synthesis

---

## 7. Minimal migration path

### Step 1

新增：

- `ProjectArchetype.cs`
- `ProjectArchetypeResolverService.cs`

并在 `ProjectMemory` 上落：

- `ArchetypeLabel`
- `ArchetypeConfidence`
- `ArchetypeReason`

### Step 2

把 [InternalProjectAssessmentService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/InternalProjectAssessmentService.cs) 里的：

- `CalculateExternalRelevance`
- `CalculateDecisionStakes`

先抽到 profile 层，不再统一按关键词硬算。

### Step 3

把 [ProjectStateScorer.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/ProjectStateScorer.cs) 拆成：

- `ApplicationOpsScoringProfile`
- `ResearchEvaluationScoringProfile`
- `EngineeringExecutionScoringProfile`

### Step 4

把 [ExternalSignalCollectorService.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Services/ExternalSignalCollectorService.cs) 的 query 生成改成：

- profile-specific query builder
- profile-specific authoritative source rules

### Step 5

再把 UI 和回复里展示的“评分依据”改成 profile-aware 说明。

比如：

- `phdapply`
  - “风险高，因为学校要求证据不全 + 关键材料未闭环”
- `mainland`
  - “风险中等，因为主线明确，但最近线程分散，外部标准还没完全补齐”

---

## 8. What we should do next

最合理的下一步不是立刻重写所有 scorer，而是：

1. 先做 `ProjectArchetypeResolverService`
2. 先把每个项目明确落到 6 个产品主桶之一
3. 然后只先重写一个 scorer

推荐顺序：

1. 先重写 `EngineeringExecution`
   - 因为 `mainland` 就能立刻拿来测
2. 再重写 `ResearchEvaluation`
   - 用 `singletrans / learned_slot`
3. 最后做 `ApplicationOps`
   - 用 `phdapply`

如果只按性价比来，我建议第一刀就切：

**先做 archetype resolver + engineering scorer。**

因为这会立刻改善 `mainland` 自己的项目判断，不再让桌宠还没把自己看明白，就去假装会评价别的项目。
