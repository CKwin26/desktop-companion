using System.IO;
using System.Text.RegularExpressions;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class ProjectArchetypeResolverService
{
    private static readonly string[] ApplicationSignals =
    [
        "phd",
        "phdapply",
        "application",
        "admission",
        "school",
        "university",
        "statement of purpose",
        "sop",
        "proposal",
        "cv",
        "recommender",
        "recommendation",
        "submission",
        "deadline",
        "faculty",
        "lab"
    ];

    private static readonly string[] ResearchSignals =
    [
        "research",
        "paper",
        "benchmark",
        "evaluation",
        "eval",
        "experiment",
        "geovisual",
        "learned_slot",
        "vectra",
        "method",
        "dataset",
        "annotation",
        "corpus",
        "metric",
        "ablation",
        "baseline",
        "result",
        "analysis",
        "misp",
        "alimeeting",
        "cpcer",
        "方法",
        "评估",
        "实验",
        "基准"
    ];

    private static readonly string[] EngineeringSignals =
    [
        "repo",
        "code",
        "build",
        "bug",
        "fix",
        "wpf",
        "desktop",
        "shell",
        "ui",
        "ux",
        "frontend",
        "backend",
        "api",
        "service",
        "plugin",
        "release",
        "prototype",
        "implementation",
        "architecture"
    ];

    private static readonly string[] ProductSignals =
    [
        "eeg",
        "sleep",
        "pillow",
        "sensor",
        "contact",
        "ergonomic",
        "ergonomics",
        "geometry",
        "mechanical",
        "hardware",
        "fit",
        "wearable",
        "industrial design",
        "human factors",
        "compliance"
    ];

    private static readonly string[] OperationsSignals =
    [
        "admin",
        "ops",
        "operation",
        "cleanup",
        "clean up",
        "disk",
        "partition",
        "move",
        "migrate",
        "export",
        "package",
        "deliver",
        "delivery",
        "report",
        "ppt",
        "pptx",
        "pdf",
        "archive",
        "inventory",
        "batch",
        "整理",
        "清理",
        "搬盘",
        "迁移",
        "分区",
        "合并",
        "c盘",
        "e盘",
        "g盘",
        "盘",
        "导出",
        "报告",
        "交付",
        "事务"
    ];

    private static readonly string[] LifeEntertainmentSignals =
    [
        "life",
        "personal",
        "habit",
        "hobby",
        "movie",
        "music",
        "game",
        "anime",
        "novel",
        "travel",
        "food",
        "relax",
        "entertainment",
        "leisure",
        "生活",
        "文娱",
        "娱乐",
        "电影",
        "音乐",
        "游戏",
        "小说",
        "旅行",
        "放松"
    ];

    public ProjectArchetypeResolution Resolve(ProjectMemory project)
    {
        var signalText = BuildSignalText(project);
        var engineeringReasons = new List<string>();
        var researchReasons = new List<string>();
        var applicationReasons = new List<string>();
        var productReasons = new List<string>();
        var operationsReasons = new List<string>();
        var lifeReasons = new List<string>();

        var engineeringScore = 10;
        var researchScore = 10;
        var applicationScore = 10;
        var productScore = 10;
        var operationsScore = 10;
        var lifeScore = 10;

        ApplySignalScore(signalText, EngineeringSignals, 6, 36, ref engineeringScore, engineeringReasons, "最近信号明显偏实现与工程");
        ApplySignalScore(signalText, ResearchSignals, 7, 49, ref researchScore, researchReasons, "最近信号明显偏研究评测");
        ApplySignalScore(signalText, ApplicationSignals, 8, 56, ref applicationScore, applicationReasons, "最近信号明显偏申请材料");
        ApplySignalScore(signalText, ProductSignals, 7, 42, ref productScore, productReasons, "最近信号明显偏产品设计与人因");
        ApplySignalScore(signalText, OperationsSignals, 7, 42, ref operationsScore, operationsReasons, "最近信号明显偏事务推进与交付整理");
        ApplySignalScore(signalText, LifeEntertainmentSignals, 8, 48, ref lifeScore, lifeReasons, "最近信号明显偏生活文娱");

        if (MatchesKeyword(signalText, "phdapply"))
        {
            applicationScore += 28;
            applicationReasons.Add("当前工作区本身就是 phdapply");
        }

        if (MatchesKeyword(signalText, "singletrans"))
        {
            researchScore += 18;
            researchReasons.Add("当前工作区本身就是 singletrans");
        }

        if (MatchesKeyword(signalText, "learned_slot")
            || MatchesKeyword(signalText, "vectra")
            || ContainsAny(signalText, "geovisual", "ransac", "找圆", "视觉", "算法线路"))
        {
            researchScore += 28;
            researchReasons.Add("当前工作区明显是视觉方法或算法研究线");
        }

        if (MatchesKeyword(signalText, "cpcer") || MatchesKeyword(signalText, "misp"))
        {
            researchScore += 20;
            researchReasons.Add("当前工作区明显指向 benchmark 评测线");
        }

        if (MatchesKeyword(signalText, "mainland"))
        {
            engineeringScore += 18;
            engineeringReasons.Add("当前工作区本身就是 mainland");
        }

        if (LooksLikeEegWorkspace(project))
        {
            productScore += 26;
            productReasons.Add("当前工作区明显就是 EEG 产品线");
        }

        if (LooksLikeOperationsWorkspace(project))
        {
            operationsScore += 28;
            operationsReasons.Add("当前工作区更像机器整理或交付事务");
        }

        if (LooksLikeLifeEntertainmentProject(project))
        {
            lifeScore += 28;
            lifeReasons.Add("当前项目更像生活或文娱偏好线");
        }

        if (!string.IsNullOrWhiteSpace(project.WorkspaceKindLabel)
            && ContainsAny(project.WorkspaceKindLabel, ".net", "wpf", "python", "frontend", "backend", "service", "api", "repo"))
        {
            engineeringScore += 10;
            engineeringReasons.Add("工作区类型本身就偏代码实现");
        }

        if (project.EvidenceItems.Any(item => item.SourceType.Equals("repo-structure", StringComparison.OrdinalIgnoreCase)))
        {
            engineeringScore += 10;
            engineeringReasons.Add("已经拿到了 repo 结构证据");
        }

        if (project.EvidenceItems.Any(item => item.SourceType.Equals("workspace-docs", StringComparison.OrdinalIgnoreCase)))
        {
            researchScore += 6;
            applicationScore += 6;
            productScore += 6;
            researchReasons.Add("工作区文档补强了研究语义");
            applicationReasons.Add("工作区文档补强了申请语义");
            productReasons.Add("工作区文档补强了产品语义");
        }

        if (!string.IsNullOrWhiteSpace(project.PrimaryWorkspacePath))
        {
            engineeringScore += 4;
            engineeringReasons.Add("已经绑定了具体工作区路径");
        }

        if (!string.IsNullOrWhiteSpace(project.CodexWorkspacePath))
        {
            engineeringScore += 3;
            engineeringReasons.Add("已经挂上当前 Codex 工作区");
        }

        if (LooksLikeActionableWorkspace(project))
        {
            engineeringScore += 10;
            engineeringReasons.Add("最近线程看起来是可执行的仓库工作");
        }

        if (ContainsAny(signalText, "baseline", "benchmark", "eval", "evaluation", "gate_eval"))
        {
            researchScore += 10;
            researchReasons.Add("最近线程和证据明显偏评测");
        }

        if (ContainsAny(project.ExpectedDeliverable, "paper", "benchmark", "report", "experiment", "evaluation", "result"))
        {
            researchScore += 12;
            researchReasons.Add("当前交付物更像实验或评测产物");
        }

        if (ContainsAny(project.ExpectedDeliverable, "proposal", "sop", "cv", "statement", "application"))
        {
            applicationScore += 14;
            applicationReasons.Add("当前交付物更像申请材料包");
        }

        if (ContainsAny(project.ExpectedDeliverable, "prototype", "demo", "ui", "shell"))
        {
            engineeringScore += 10;
            engineeringReasons.Add("当前交付物更像可运行原型");
        }

        if (ContainsAny(project.ExpectedDeliverable, "prototype", "hardware", "device", "mechanical", "ergonomic", "pillow", "sensor"))
        {
            productScore += 12;
            productReasons.Add("当前交付物更像实体产品方案");
        }

        if (ContainsAny(project.ExpectedDeliverable, "ppt", "pptx", "pdf", "report", "export", "archive", "cleanup", "migration", "delivery", "package"))
        {
            operationsScore += 12;
            operationsReasons.Add("当前交付物更像整理、导出或包装交付");
        }

        if (ContainsAny(project.ExpectedDeliverable, "movie", "music", "game", "travel", "food", "relax", "leisure"))
        {
            lifeScore += 12;
            lifeReasons.Add("当前交付物更像生活文娱安排");
        }

        var candidates = new[]
        {
            CreateCandidate(ProjectArchetype.EngineeringExecution, engineeringScore, engineeringReasons),
            CreateCandidate(ProjectArchetype.ResearchEvaluation, researchScore, researchReasons),
            CreateCandidate(ProjectArchetype.ApplicationOps, applicationScore, applicationReasons),
            CreateCandidate(ProjectArchetype.ProductResearch, productScore, productReasons),
            CreateCandidate(ProjectArchetype.OperationsAdmin, operationsScore, operationsReasons),
            CreateCandidate(ProjectArchetype.LifeEntertainment, lifeScore, lifeReasons)
        }
        .OrderByDescending(candidate => candidate.Score)
        .ToArray();

        var best = candidates[0];
        var second = candidates[1];

        if (best.Score < 28 || (best.Score - second.Score < 6 && best.Score < 44))
        {
            return new ProjectArchetypeResolution
            {
                Archetype = ProjectArchetype.General,
                Confidence = Math.Max(28, best.Score),
                Reason = "当前证据还不够稳，我先把它放在“暂未定类”。"
            };
        }

        var confidence = Math.Max(
            38,
            Math.Min(
                94,
                24 + best.Score + Math.Min(18, (best.Score - second.Score) * 2)));

        var bucketLabel = ProjectArchetypes.ToDisplayLabel(best.Archetype);
        var reason = best.Reasons.Count == 0
            ? $"当前更像“{bucketLabel}”。"
            : $"当前更像“{bucketLabel}”，因为 {string.Join("，", best.Reasons.Take(3))}。";

        return new ProjectArchetypeResolution
        {
            Archetype = best.Archetype,
            Confidence = confidence,
            Reason = reason
        };
    }

    private static string BuildSignalText(ProjectMemory project)
    {
        return string.Join(
                " ",
                [
                    project.Name,
                    project.Summary,
                    project.KindLabel,
                    project.ExpectedDeliverable,
                    project.WorkspaceKindLabel,
                    project.PrimaryWorkspaceLabel,
                    project.PrimaryWorkspacePath,
                    project.CodexWorkspaceLabel,
                    .. project.Keywords,
                    .. project.RecentItems,
                    .. project.RecentCodexThreadTitles
                ])
            .ToLowerInvariant();
    }

    private static string BuildNonPathSignalText(ProjectMemory project)
    {
        return string.Join(
                " ",
                [
                    project.Name,
                    project.Summary,
                    project.KindLabel,
                    project.ExpectedDeliverable,
                    project.WorkspaceKindLabel,
                    project.PrimaryWorkspaceLabel,
                    project.CodexWorkspaceLabel,
                    .. project.Keywords,
                    .. project.RecentItems,
                    .. project.RecentCodexThreadTitles
                ])
            .ToLowerInvariant();
    }

    private static void ApplySignalScore(
        string signalText,
        IReadOnlyCollection<string> keywords,
        int weightPerHit,
        int cap,
        ref int score,
        List<string> reasons,
        string reasonText)
    {
        var hits = CountHits(signalText, keywords);
        if (hits <= 0)
        {
            return;
        }

        score += Math.Min(cap, hits * weightPerHit);
        reasons.Add(reasonText);
    }

    private static int CountHits(string signalText, IEnumerable<string> keywords)
    {
        return keywords.Count(keyword => MatchesKeyword(signalText, keyword));
    }

    private static bool MatchesKeyword(string signalText, string keyword)
    {
        var normalizedKeyword = (keyword ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return false;
        }

        return Regex.IsMatch(
            signalText,
            $@"(?<![a-z0-9]){Regex.Escape(normalizedKeyword)}(?![a-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsAny(string input, params string[] keywords)
    {
        return keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeEegWorkspace(ProjectMemory project)
    {
        if (string.Equals(project.Name, "EEG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.PrimaryWorkspaceLabel, "EEG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.CodexWorkspaceLabel, "EEG", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (project.PrimaryWorkspacePath ?? string.Empty).EndsWith($"{Path.DirectorySeparatorChar}EEG", StringComparison.OrdinalIgnoreCase)
               || (project.CodexWorkspacePath ?? string.Empty).EndsWith($"{Path.DirectorySeparatorChar}EEG", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeActionableWorkspace(ProjectMemory project)
    {
        var workspacePath = !string.IsNullOrWhiteSpace(project.CodexWorkspacePath)
            ? project.CodexWorkspacePath
            : project.PrimaryWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }

        if (Regex.IsMatch(project.CodexWorkspaceLabel ?? string.Empty, "^[A-Za-z]:$"))
        {
            return false;
        }

        return project.RecentCodexThreadTitles.Count > 0
               || project.RecentItems.Count > 0;
    }

    private static bool LooksLikeOperationsWorkspace(ProjectMemory project)
    {
        var names = new[]
        {
            project.Name,
            project.PrimaryWorkspaceLabel,
            project.CodexWorkspaceLabel
        };

        if (names.Any(name => string.Equals(name, "C:", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Ooni", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "PA", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ContainsAny(
            BuildNonPathSignalText(project),
            "disk",
            "cleanup",
            "move",
            "robocopy",
            "ppt",
            "pdf",
            "report export",
            "文件搬运",
            "磁盘",
            "分区",
            "迁移",
            "搬盘",
            "c盘",
            "e盘",
            "g盘",
            "盘",
            "合并",
            "清理",
            "导出");
    }

    private static bool LooksLikeLifeEntertainmentProject(ProjectMemory project)
    {
        return ContainsAny(
            BuildSignalText(project),
            "生活",
            "文娱",
            "娱乐",
            "放松",
            "movie",
            "music",
            "game",
            "anime",
            "novel",
            "travel");
    }

    private static ArchetypeCandidate CreateCandidate(
        ProjectArchetype archetype,
        int score,
        IReadOnlyList<string> reasons)
    {
        return new ArchetypeCandidate(
            archetype,
            Math.Max(0, Math.Min(100, score)),
            reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private sealed record ArchetypeCandidate(ProjectArchetype Archetype, int Score, IReadOnlyList<string> Reasons);
}
