using DesktopCompanion.WpfHost.Models;
using DesktopCompanion.WpfHost.Services;

namespace DesktopCompanion.WpfHost.Cognition;

public static class UserWorkingStyleProfile
{
    private static IReadOnlyList<string> DefaultStableTraits { get; } =
    [
        "这是一个会跨研究、产品和商业化三条线切换的人。",
        "比起只做模型，她更在意整条 workflow 能不能跑通、验证、复现和交付。",
        "她经常把模糊想法快速推进成脚手架、原型、评估流程或可演示系统。",
        "她天然偏好多学科问题：AI、视觉、几何、实验、制造、光学、自动化可以混在同一张桌子上。",
        "她对 benchmark、debug artifacts、guardrails、ablation 和真实评估非常敏感。"
    ];

    private static IReadOnlyList<string> DefaultKnownWorkLanes { get; } =
    [
        "AI 与自主研究工作流",
        "工业视觉、测量与结构化几何",
        "计算光学、仿真与模组设计",
        "语音、时序建模与 benchmark 设计",
        "实验室自动化与仪器产品化",
        "商业材料、pitch、专利与样品推进",
        "申请材料、研究叙事与项目归档"
    ];

    private static IReadOnlyList<string> DefaultLikelyFailureModes { get; } =
    [
        "容易同时打开太多项目线，导致主线被稀释。",
        "容易因为会搭系统而把结构做得过重，先于真实推进。",
        "研究、商业和申请任务混在一起时，容易暂时失去明确顺位。",
        "如果没有明确输出物和 checkpoint，事情会停在思路层。"
    ];

    private static IReadOnlyList<string> DefaultBestSupportStyle { get; } =
    [
        "优先帮她识别现在是在推进哪条项目线，而不是先建复杂看板。",
        "优先给 deliverable、next action、checkpoint，而不是泛泛鼓励。",
        "当她一口气抛很多事时，先按项目线归并，再排 now、next、later。",
        "遇到卡点时，提醒她回到验证路径：先做哪个最小可证明动作。",
        "在需要对外表达时，帮她把技术工作翻成可讲述的项目叙事。"
    ];

    private static IReadOnlyList<string> DefaultPrivacyBoundaries { get; } =
    [
        "不要在长期记忆里保存证件、地址、邮箱、电话号码、账号、证书图片等敏感信息。",
        "不要默认记住精确日期、机构细节、奖项细节或申请材料原文。",
        "只保留稳定的工作风格、常见项目类型、推进偏好和常见卡点。"
    ];

    public static IReadOnlyList<string> StableTraits =>
        Merge(DefaultStableTraits, LoadLocalProfile()?.StableTraits);

    public static IReadOnlyList<string> KnownWorkLanes =>
        Merge(DefaultKnownWorkLanes, LoadLocalProfile()?.KnownWorkLanes);

    public static IReadOnlyList<string> LikelyFailureModes =>
        Merge(DefaultLikelyFailureModes, LoadLocalProfile()?.LikelyFailureModes);

    public static IReadOnlyList<string> BestSupportStyle =>
        Merge(DefaultBestSupportStyle, LoadLocalProfile()?.BestSupportStyle);

    public static IReadOnlyList<string> PrivacyBoundaries =>
        Merge(DefaultPrivacyBoundaries, LoadLocalProfile()?.PrivacyBoundaries);

    public static string BuildCompanionAddendum()
    {
        var localProfile = LoadLocalProfile();

        return string.Join(
            "\n",
            [
                "这是你对当前用户的隐私安全工作画像，只能把它当作支持线索：",
                ..BuildSummaryLines(localProfile),
                ..StableTraits.Select(item => $"- {item}"),
                string.Empty,
                "她常见的项目线：",
                ..KnownWorkLanes.Select(item => $"- {item}"),
                string.Empty,
                "她常见的失速方式：",
                ..LikelyFailureModes.Select(item => $"- {item}"),
                string.Empty,
                "最适合她的帮助方式：",
                ..BestSupportStyle.Select(item => $"- {item}"),
                string.Empty,
                "隐私边界：",
                ..PrivacyBoundaries.Select(item => $"- {item}")
            ]);
    }

    public static string BuildProjectCognitionAddendum()
    {
        var localProfile = LoadLocalProfile();

        return string.Join(
            "\n",
            [
                "当前用户的项目认知偏好：",
                ..BuildSummaryLines(localProfile),
                ..StableTraits.Select(item => $"- {item}"),
                string.Empty,
                "归并项目时优先考虑这些常见项目线：",
                ..KnownWorkLanes.Select(item => $"- {item}"),
                string.Empty,
                "排序和追问时注意这些支持偏好：",
                ..BestSupportStyle.Select(item => $"- {item}"),
                string.Empty,
                "隐私边界：",
                ..PrivacyBoundaries.Select(item => $"- {item}")
            ]);
    }

    private static DistilledUserProfile? LoadLocalProfile()
    {
        return new LocalUserProfileStore().LoadProfile();
    }

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string> defaults,
        IReadOnlyList<string>? localValues)
    {
        return defaults
            .Concat(localValues ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IEnumerable<string> BuildSummaryLines(DistilledUserProfile? localProfile)
    {
        if (localProfile is null)
        {
            return [];
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(localProfile.Summary))
        {
            lines.Add($"- 最新本地蒸馏画像：{localProfile.Summary}");
        }

        if (localProfile.SourceLabels.Count > 0)
        {
            lines.Add($"- 蒸馏来源标签：{string.Join("、", localProfile.SourceLabels.Take(6))}");
        }

        return lines;
    }
}
