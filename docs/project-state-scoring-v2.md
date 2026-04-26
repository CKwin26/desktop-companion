# Project State Scoring V2

这份文档回答一个更根本的问题：

**项目状态不该只按本地 heuristic 打分，而应该怎么打？**

这版是对 `Sprint 1` 打分逻辑的修正方向。

---

## 1. 先说结论

你提的方向是对的：

- 不能只看本地目录结构和几条文本线索就硬打分
- 应该先有桌宠自己的思考链路
- 再根据它自己对这个项目“知道多少、不知道多少”，决定要不要去外部找证据
- 最后把“内部判断”和“外部评价”分开，再合成状态

但有一点要钉死：

**人格应该影响“怎么思考”和“怎么干预”，不应该直接把分数打歪。**

也就是说：

- 人格决定它会优先问什么问题
- 人格决定它更像一个什么风格的 RA
- 但原始分数还是要落回证据、外部反馈和不确定性

否则就会变成：

**分数不是项目状态，而是桌宠情绪。**

这不对。

---

## 2. 为什么要改

当前 `Sprint 1` 的分数主要来自：

- 本地结构
- 文本摘要
- 显式 blocker 词
- 是否有 `NextAction`

它的问题不是“完全没用”，而是：

- 太静态
- 太像表面健康度
- 对隐性风险不敏感
- 不会因为“自己其实不懂”而主动补证据

这跟最近几篇 agent research 的结论是对齐的：

- `Agentic Uncertainty Reveals Agentic Overconfidence` 指出，agent 会系统性过度自信，光靠自己评估自己不够。[OpenReview](https://openreview.net/forum?id=yz28r69xU8)
- `Agentic Confidence Calibration` 提出，agent 的置信度应该按整个 trajectory/process 来看，而不是只看一个静态输出。[OpenReview](https://openreview.net/forum?id=6YMFsGFabM)
- `SeekBench` 认为评估 search agent 不能只看最终答案，要看 `groundedness / recovery / calibration` 三种 epistemic competence。[OpenReview](https://openreview.net/forum?id=r0L9GwlnzP)
- `What large language models know and what people think they know` 指出模型内部知道多少，和人从它的话里感觉它知道多少，这中间有明显 gap。[Nature Machine Intelligence](https://www.nature.com/articles/s42256-024-00976-7)

所以对这个桌宠来说，正确方向不是“把 heuristic 再调细一点”，而是：

**把打分拆成：内部思考、外部搜证、最终合成。**

---

## 3. 新打分逻辑应该分 4 层

## 3.1 `Internal Reasoning Layer`

先让桌宠按自己的人格和 RA 角色做一次内部判断。

这里的人格不是情绪，而是：

- 它是一个多线程 personal portfolio RA
- 它会优先保护主线
- 它会优先看证据和阻塞
- 它会先问“下一步是什么”“有没有漂”

所以它的内部链路应该先产出这些中间量：

- `WhatIsThisProject`
- `WhatDoIThinkTheMainlineIs`
- `WhatDoIThinkTheCurrentMilestoneIs`
- `WhatEvidenceDoIAlreadyHave`
- `WhatAmINotSureAbout`
- `WhatCouldBeWrongWithMyCurrentView`

也就是：

**先有一个自我审视步骤。**

---

## 3.2 `Search Decision Layer`

下一步不是马上打最终分，而是先判断：

**我现在知道得够不够？要不要上网搜？**

这层的输入应该是：

- `CognitionConfidence`
  - 我对这个项目的理解置信度
- `EvidenceCoverage`
  - 我手里的证据覆盖够不够
- `FreshnessRisk`
  - 这件事是不是时间敏感
- `ExternalRelevance`
  - 这个项目值不值得看外部评价
- `DecisionStakes`
  - 这次判断会不会影响重要决策

建议把 `NeedSearchScore` 先这样算：

```text
NeedSearchScore =
0.35 * (100 - CognitionConfidence)
+ 0.20 * (100 - EvidenceCoverage)
+ 0.20 * FreshnessRisk
+ 0.15 * ExternalRelevance
+ 0.10 * DecisionStakes
```

然后设一个简单门槛：

- `>= 60`：必须外搜
- `40-59`：建议外搜
- `< 40`：先用本地判断

这才符合你说的：

**先根据自己认知程度，决定要不要上网。**

---

## 3.3 `External Evaluation Layer`

如果触发外搜，就不能只搜“有没有这个名字”，而要搜：

**别人会怎么评价这类东西。**

这里要注意：

“别人怎么评价”不是一个单一来源，而是按项目类型分流。

### `Research / application project`

像 `phdapply` 这种，不是去搜“这个文件夹别人评价如何”，而是搜：

- 学校要求
- deadline 和 submission norms
- SOP / proposal / recommendation 的外部标准
- 这个叙事在申请语境下常见的优缺点

### `Engineering / code project`

像 `mainland`、`singletrans` 这种，更应该看：

- 官方文档
- benchmark
- issue / discussion / PR review
- 同类项目的外部反馈

### `Product / design project`

更适合看：

- 竞品评价
- 用户反馈
- 设计 critique
- 社区讨论

也就是说，外搜的结果不该直接混成“一个 sentiment 分”，而应该拆成：

- `ExternalSupport`
  - 外部标准是否支持当前方向
- `ExternalConcern`
  - 外部有没有明显反例或批评
- `ExternalBenchmarkGap`
  - 跟外部 best practice 差多少
- `ExternalConsensusStrength`
  - 外部评价是否一致

---

## 3.4 `Final Synthesis Layer`

最后再合成项目状态。

但这里我不建议只保留一个总分。

应该至少保留 6 个面向：

- `InternalUnderstanding`
  - 我自己是否真的看懂这条项目线
- `EvidenceStrength`
  - 我手里的证据够不够硬
- `ExternalSupport`
  - 外部标准和评价是否支持当前方向
- `ExecutionHealth`
  - 当前推进是不是顺
- `Risk`
  - 当前决策和推进风险多高
- `Confidence`
  - 我对以上判断的总体把握度

如果一定要合一个总分，也应该是：

```text
StateScore =
0.20 * InternalUnderstanding
+ 0.20 * EvidenceStrength
+ 0.15 * ExternalSupport
+ 0.20 * ExecutionHealth
+ 0.25 * (100 - Risk)
```

而 `Confidence` 不要混进 `StateScore`，它应该单独显示。

因为：

**项目状态好不好** 和 **我对判断有多确定** 是两码事。

---

## 4. 这套逻辑里，“人格”到底该进哪

你说“根据人格来”，我认同，但我会把它放在这几个地方：

### `人格应该决定`

- 提问顺序
- 风险敏感度
- 主线收束偏好
- 触发 search 的阈值
- 干预语气和干预强度

### `人格不应该直接决定`

- 证据是不是存在
- 外部评价是不是支持
- blocker 有没有出现
- 项目到底推进得顺不顺

不然就会出现一个坏情况：

同一个项目，只因为桌宠“今天更激进”或“今天更保守”，分数就飘。

这在工程上是不可靠的。

---

## 5. 现在应该把旧的 5 个分怎么替换

当前 `Sprint 1` 的：

- `Momentum`
- `Clarity`
- `Risk`
- `Drift`
- `Confidence`

可以先过渡成下面这版：

### `1. InternalUnderstanding`

替代现在偏 heuristic 的 `Clarity`。

看的是：

- 它是否看懂项目是什么
- 当前 milestone 是否明确
- deliverable 是否明确

### `2. EvidenceStrength`

新增。

看的是：

- 本地证据强不强
- 证据是否多源
- 有没有真的读过 workspace

### `3. ExternalSupport`

新增。

看的是：

- 外部标准是否支持当前方向
- 外部评价是否偏正 / 偏负

### `4. ExecutionHealth`

替代现在的 `Momentum + 部分 Drift`。

看的是：

- 当前推进是否顺
- next step 是否明确
- blocker 是否在积累

### `5. Risk`

保留，但要升级。

要纳入：

- blocker
- freshness
- deadline pressure
- external concern

### `6. Confidence`

保留，但要改含义。

不再是“信息多就高”，而是：

**我对当前判断到底有多把握。**

---

## 6. 对 `phdapply` 这种项目，这套新逻辑会怎么变

如果按你说的逻辑去打 `phdapply`：

### 第一步：内部判断

桌宠会先想：

- 这是单研究课题吗？不是。
- 这是申请运营型 workspace 吗？是。
- 我看到了什么？目录结构、材料类型、tracker、portfolio。
- 我没看到什么？真实 deadline 压力、推荐信推进、哪些学校版本已经 ready。

### 第二步：决定要不要搜

这时 `NeedSearchScore` 应该高。

因为：

- 这是高 stakes 项目
- 很多判断是时间敏感的
- 外部标准非常重要
- 当前本地证据不足以判断“这套申请包是否真的强”

所以它应该主动去搜：

- 学校页面
- 申请材料要求
- 相关项目叙事在申请语境下的常见评价

### 第三步：外部评价进入

然后 `Risk` 和 `Confidence` 才会更像真的：

- 如果学校要求比现有材料更细，`Risk` 上升
- 如果你当前叙事与常见强申请叙事一致，`ExternalSupport` 上升
- 如果外部要求和你现有材料冲突，`Confidence` 下降

这就比当前版本强很多。

---

## 7. 我建议的实现顺序

如果按工程顺序落地，我建议这样拆：

### `Step 1`

先保留 `Sprint 1` 的本地状态模型，不推翻。

### `Step 2`

新增一个：

- `InternalProjectAssessmentService`

负责：

- 先做人格驱动的内部审视
- 产出 `CognitionConfidence / EvidenceCoverage / NeedSearchScore`

### `Step 3`

新增一个：

- `ExternalSignalCollectorService`

负责：

- 根据项目类型决定搜什么
- 把外部证据收成结构化信号

### `Step 4`

新增一个真正的：

- `ProjectStateScorer`

负责：

- 合成 `InternalUnderstanding / EvidenceStrength / ExternalSupport / ExecutionHealth / Risk / Confidence`

也就是说：

**以后 `LocalProjectStore` 和 `ProjectCognitionService` 不该继续直接算最终分。**

它们只该提供原始信号。

---

## 8. 一句话总结

你说得对，新的打分逻辑应该是：

**先让桌宠按自己的人格做内部思考，再根据它自己认知是否充分来决定要不要外搜，最后把内部判断和外部评价分开合成项目状态。**

而不是：

**只根据本地结构和文本表面特征，直接给项目打一个看起来很像那么回事的分。**

---

## 9. 参考来源

- [Agentic Confidence Calibration](https://openreview.net/forum?id=6YMFsGFabM)
- [Agentic Uncertainty Reveals Agentic Overconfidence](https://openreview.net/forum?id=yz28r69xU8)
- [SeekBench: Evaluating Epistemic Competence in Information-Seeking Agents](https://openreview.net/forum?id=r0L9GwlnzP)
- [What large language models know and what people think they know](https://www.nature.com/articles/s42256-024-00976-7)
