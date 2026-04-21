# Tuanzi Cognition Assets

这份文档把团子的核心认知资产固定下来，避免它继续散落成零碎 prompt。

目标不是再做一个“人格设定稿”，而是把真正会影响行为的部分拆成可维护资产。

## 1. Persona Prompt

位置：

- [TuanziCognitionProfile.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Cognition/TuanziCognitionProfile.cs:1)

这部分负责：

- 团子的身份定义
- 回答时的语气约束
- AI provider 统一 system prompt

重点不是“像一个可爱角色”，而是：

- 她是桌面搭子
- 她不是周报器
- 她不是项目管理器

## 2. Judgment Framework

同样放在：

- [TuanziCognitionProfile.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Cognition/TuanziCognitionProfile.cs:1)

这里固定了 4 组东西：

- `MentalModels`
- `DecisionHeuristics`
- `AntiPatterns`
- `HonestBoundaries`

也就是说，团子以后不是只靠“说话像不像”来稳定，而是靠：

- 先接住人，再处理事
- 先找主线，再看细节
- 先认项目线，再挂任务
- 信息不够时承认不确定

## 3. Memory Schema

也在：

- [TuanziCognitionProfile.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Cognition/TuanziCognitionProfile.cs:1)

目前明确分成：

- `Conversation Memory`
- `Active Task Memory`
- `Project Cognition Memory`
- `Guidance Memory`

这样后面我们再扩展时，就不会把所有东西都硬塞进 `tasks.json`。

## 4. Quality Checks

位置：

- [ProjectCognitionQualityGuard.cs](C:/Users/austa/OneDrive/Desktop/mainland/apps/desktop-shell-wpf/Cognition/ProjectCognitionQualityGuard.cs:1)

这层负责把模型输出做一次落地前质检：

- 项目数不能失控
- 名称不能为空
- `matchType` 必须规范
- `followUpPrompt` 不能太长
- `keywords / items` 要去重和裁剪

这一步很关键，因为它保证：

- 模型偶尔跑偏时，前台不会直接露出一坨脏 JSON 逻辑
- 团子的“项目认知”更像稳定能力，不像一次性运气

## 5. Why This Matters

如果没有这 4 份资产，团子很容易继续退化成：

- 一堆 prompt 字符串
- 一组随手写的判断 if/else
- 一个越来越重的任务面板

现在这套资产的作用是：

- 让人格、判断、记忆、质检各自有归属
- 让 OpenAI / Ollama 共用同一套底稿
- 让后面继续做“长期记忆”时有稳定接口
