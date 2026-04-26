using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class ProjectCognitionService
{
    private static readonly Regex NumberedItemRegex = new(
        @"(?m)^\s*(?:\d+[\.、\)]|[-*•])\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly string[] VerbPrefixes =
    [
        "做一个",
        "做新",
        "做",
        "重做",
        "封装",
        "申请",
        "生产",
        "总结一下",
        "总结",
        "催一下",
        "探讨",
        "强化",
        "进一步强化",
        "自动",
        "整理",
        "梳理",
        "完成",
        "推进",
        "搭",
        "补"
    ];

    private static readonly string[] PlanningKeywords =
    [
        "梳理",
        "整理",
        "理一下",
        "排一下",
        "排优先级",
        "优先级",
        "轻重缓急",
        "主线",
        "我现在要做什么",
        "帮我排",
        "盘一下",
        "这堆事"
    ];

    private static readonly string[] StopKeywords =
    [
        "我们",
        "一个",
        "需要",
        "可以",
        "就是",
        "然后",
        "整体",
        "东西",
        "软件",
        "系统",
        "项目",
        "方案",
        "一下子",
        "之前",
        "现在"
    ];

    public bool LooksLikeProjectDump(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var matches = NumberedItemRegex.Matches(input);
        if (matches.Count >= 3)
        {
            return true;
        }

        var lines = input
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count >= 4)
        {
            return true;
        }

        var separatorCount = input.Count(character => character is '，' or '；' or ';' or '、');
        if (separatorCount >= 4)
        {
            return true;
        }

        var containsPlanningIntent = PlanningKeywords.Any(keyword =>
            input.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        return containsPlanningIntent && (separatorCount >= 2 || lines.Count >= 2 || input.Length >= 28);
    }

    public ProjectCognitionDigest CreateFallbackDigest(string input, IReadOnlyList<ProjectMemory> knownProjects)
    {
        var items = ExtractItems(input);
        if (items.Count == 0)
        {
            return new ProjectCognitionDigest
            {
                Summary = "这次我还没拆出明确的项目线。",
                FollowUpPrompt = "你可以再按行发给我，我来替你分组。"
            };
        }

        var matchedProjects = new List<ProjectDigestProject>();

        foreach (var item in items)
        {
            var matched = FindBestKnownProject(item, knownProjects);
            if (matched is not null)
            {
                var existing = matchedProjects.FirstOrDefault(project =>
                    Normalize(project.Name) == Normalize(matched.Name));

                if (existing is null)
                {
                    existing = new ProjectDigestProject
                    {
                        Name = matched.Name,
                        MatchType = "existing",
                        Summary = matched.Summary,
                        Priority = string.IsNullOrWhiteSpace(matched.PriorityLabel)
                            ? "next"
                            : NormalizePriorityLabel(matched.PriorityLabel),
                        NextAction = matched.NextAction,
                        Keywords = matched.Keywords.ToList()
                    };
                    matchedProjects.Add(existing);
                }

                existing.Items.Add(item);
                existing.Keywords = MergeStrings(existing.Keywords, ExtractKeywords(item, matched.Keywords));
                if (string.IsNullOrWhiteSpace(existing.NextAction))
                {
                    existing.NextAction = item;
                }

                continue;
            }

            matchedProjects.Add(new ProjectDigestProject
            {
                Name = InferProjectName(item),
                MatchType = "candidate",
                Summary = $"这条项目线里目前先记到：{item}",
                Priority = "next",
                NextAction = item,
                Keywords = ExtractKeywords(item),
                Items = [item]
            });
        }

        var mergedProjects = MergeDuplicateProjects(matchedProjects);
        RankProjects(mergedProjects);

        return BuildDigest(mergedProjects);
    }

    public void MergeDigestIntoProjects(IList<ProjectMemory> knownProjects, ProjectCognitionDigest digest)
    {
        var now = DateTimeOffset.Now;
        foreach (var project in digest.Projects.Where(project => !string.IsNullOrWhiteSpace(project.Name)))
        {
            var matched = FindBestProjectMemory(project, knownProjects);
            if (matched is null)
            {
                knownProjects.Add(CreateProjectMemory(project, now));
                continue;
            }

            ApplyDigestToProjectMemory(matched, project, now);
        }
    }

    public string BuildCompanionReply(ProjectCognitionDigest digest)
    {
        if (digest.Projects.Count == 0)
        {
            return "我先听到了一堆事，但这次还没稳稳拆出项目线。你继续往下丢，我来帮你重排。";
        }

        var projectNames = string.Join("、", digest.Projects.Take(4).Select(project => project.Name));
        var existingCount = digest.Projects.Count(project => project.MatchType == "existing");

        var summary = existingCount > 0
            ? $"我先把这批事理成了 {digest.Projects.Count} 条项目线：{projectNames}。里面有 {existingCount} 条更像你之前已经在推进的旧项目。"
            : $"我先把这批事理成了 {digest.Projects.Count} 条项目线：{projectNames}。现在看起来更像几条并行推进的主题，不是一份周报。";

        var actionParts = new List<string>();

        if (digest.NowItems.Count > 0)
        {
            actionParts.Add($"先动“{digest.NowItems[0]}”");
        }

        if (digest.NextItems.Count > 0)
        {
            actionParts.Add($"然后看“{digest.NextItems[0]}”");
        }

        if (digest.LaterItems.Count > 0)
        {
            actionParts.Add($"剩下先挂着“{digest.LaterItems[0]}”");
        }

        var actionLine = actionParts.Count == 0
            ? string.Empty
            : $"{string.Join("，", actionParts)}。";

        var followUp = string.IsNullOrWhiteSpace(digest.FollowUpPrompt) || digest.FollowUpPrompt.Length > 72
            ? BuildFollowUpPrompt(digest.Projects)
            : digest.FollowUpPrompt;

        return $"{summary}{actionLine}{followUp}";
    }

    public static bool TryDeserializeDigest(string rawText, out ProjectCognitionDigest? digest)
    {
        digest = null;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var trimmed = rawText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed
                .Trim('`')
                .Replace("json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed[firstBrace..(lastBrace + 1)];
        }

        try
        {
            digest = JsonSerializer.Deserialize<ProjectCognitionDigest>(
                trimmed,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return digest is not null;
        }
        catch
        {
            return false;
        }
    }

    private static ProjectCognitionDigest BuildDigest(List<ProjectDigestProject> projects)
    {
        var nowItems = projects
            .Where(project => project.Priority == "now")
            .Select(project => project.NextAction)
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var nextItems = projects
            .Where(project => project.Priority == "next")
            .Select(project => project.NextAction)
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var laterItems = projects
            .Where(project => project.Priority == "later")
            .Select(project => project.NextAction)
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var suggestedFocus = nowItems.FirstOrDefault()
            ?? nextItems.FirstOrDefault()
            ?? projects.FirstOrDefault()?.NextAction
            ?? projects.FirstOrDefault()?.Name
            ?? string.Empty;

        return new ProjectCognitionDigest
        {
            Summary = $"我先把这批事情拆成了 {projects.Count} 条项目线。",
            FollowUpPrompt = BuildFollowUpPrompt(projects),
            SuggestedFocus = suggestedFocus,
            NowItems = nowItems,
            NextItems = nextItems,
            LaterItems = laterItems,
            Projects = projects
        };
    }

    private static void RankProjects(List<ProjectDigestProject> projects)
    {
        var ordered = projects
            .OrderBy(project => project.MatchType == "existing" ? 0 : project.MatchType == "candidate" ? 1 : 2)
            .ThenByDescending(project => project.Items.Count)
            .ThenBy(project => project.Name)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Priority = index switch
            {
                0 => "now",
                1 or 2 => "next",
                _ => "later"
            };

            if (string.IsNullOrWhiteSpace(ordered[index].NextAction))
            {
                ordered[index].NextAction = ordered[index].Items.FirstOrDefault() ?? ordered[index].Name;
            }
        }

        projects.Clear();
        projects.AddRange(ordered);
    }

    private static List<ProjectDigestProject> MergeDuplicateProjects(IEnumerable<ProjectDigestProject> projects)
    {
        var merged = new List<ProjectDigestProject>();

        foreach (var project in projects)
        {
            var existing = merged.FirstOrDefault(candidate => Normalize(candidate.Name) == Normalize(project.Name));
            if (existing is null)
            {
                merged.Add(new ProjectDigestProject
                {
                    Name = project.Name,
                    MatchType = project.MatchType,
                    Summary = project.Summary,
                    Priority = project.Priority,
                    NextAction = project.NextAction,
                    Keywords = MergeStrings([], project.Keywords),
                    Items = MergeStrings([], project.Items, maxCount: 8)
                });
                continue;
            }

            existing.MatchType = existing.MatchType == "existing" ? existing.MatchType : project.MatchType;
            existing.Summary = string.IsNullOrWhiteSpace(existing.Summary) ? project.Summary : existing.Summary;
            existing.Priority = existing.Priority == "now" ? existing.Priority : project.Priority;
            existing.NextAction = string.IsNullOrWhiteSpace(existing.NextAction) ? project.NextAction : existing.NextAction;
            existing.Keywords = MergeStrings(existing.Keywords, project.Keywords);
            existing.Items = MergeStrings(existing.Items, project.Items, maxCount: 8);
        }

        return merged;
    }

    private static ProjectMemory CreateProjectMemory(ProjectDigestProject project, DateTimeOffset now)
    {
        var memory = new ProjectMemory
        {
            Name = project.Name,
            Summary = project.Summary,
            KindLabel = GetKindLabel(project.MatchType),
            PriorityLabel = GetPriorityLabel(project.Priority),
            NextAction = project.NextAction,
            Keywords = MergeStrings([], project.Keywords),
            RecentItems = MergeStrings([], project.Items, maxCount: 6),
            UpdatedAt = now
        };

        ApplyDigestState(memory, project, now);
        return memory;
    }

    private static void ApplyDigestToProjectMemory(ProjectMemory memory, ProjectDigestProject project, DateTimeOffset now)
    {
        memory.Name = string.IsNullOrWhiteSpace(memory.Name) ? project.Name : memory.Name;
        memory.Summary = string.IsNullOrWhiteSpace(project.Summary) ? memory.Summary : project.Summary;
        memory.KindLabel = GetKindLabel(project.MatchType);
        memory.PriorityLabel = GetPriorityLabel(project.Priority);
        memory.NextAction = string.IsNullOrWhiteSpace(project.NextAction) ? memory.NextAction : project.NextAction;
        memory.Keywords = MergeStrings(memory.Keywords, project.Keywords);
        memory.RecentItems = MergeStrings(memory.RecentItems, project.Items, maxCount: 6);
        memory.UpdatedAt = now;

        ApplyDigestState(memory, project, now);
    }

    private static void ApplyDigestState(ProjectMemory memory, ProjectDigestProject project, DateTimeOffset now)
    {
        memory.CurrentMilestone = ChooseCurrentMilestone(memory, project);
        memory.ExpectedDeliverable = string.IsNullOrWhiteSpace(memory.ExpectedDeliverable)
            ? InferExpectedDeliverable(project)
            : memory.ExpectedDeliverable;
        memory.Blockers = MergeBlockers(memory.Blockers, InferBlockers(project.Items), 6);
        memory.EvidenceItems = MergeEvidence(memory.EvidenceItems, BuildDigestEvidence(project, now), 16);
        memory.LastEvidenceAt = memory.EvidenceItems.Count > 0
            ? memory.EvidenceItems.Max(evidence => evidence.ObservedAt)
            : memory.LastEvidenceAt ?? now;
        memory.LastMeaningfulProgressAt = project.Items.Count > 0
            ? now
            : memory.LastMeaningfulProgressAt ?? memory.LastEvidenceAt ?? now;
        memory.MomentumScore = InferMomentumScore(project, memory.Blockers.Count);
        memory.ClarityScore = InferClarityScore(project, memory.ExpectedDeliverable, memory.CurrentMilestone);
        memory.RiskScore = InferRiskScore(project, memory.Blockers.Count);
        memory.DriftScore = InferDriftScore(project, memory.ExpectedDeliverable);
        memory.ConfidenceScore = InferConfidenceScore(project);
        memory.ProgressSnapshot = BuildProgressSnapshot(memory, project, now);
    }

    private static string ChooseCurrentMilestone(ProjectMemory memory, ProjectDigestProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.NextAction))
        {
            return project.NextAction;
        }

        if (!string.IsNullOrWhiteSpace(memory.CurrentMilestone))
        {
            return memory.CurrentMilestone;
        }

        return project.Items.FirstOrDefault() ?? memory.NextAction;
    }

    private static string InferExpectedDeliverable(ProjectDigestProject project)
    {
        var joined = string.Join(" ", project.Items).ToLowerInvariant();
        if (ContainsAny(joined, "proposal", "sop", "cv", "deck", "ppt", "paper", "doc", "slides", "report"))
        {
            return project.Items.FirstOrDefault(item => item.Length <= 40) ?? "working draft";
        }

        if (ContainsAny(joined, "demo", "prototype", "ui", "frontend", "app", "shell"))
        {
            return "working prototype";
        }

        if (ContainsAny(joined, "experiment", "eval", "benchmark", "result"))
        {
            return "validated result";
        }

        return string.Empty;
    }

    private static List<ProjectBlocker> InferBlockers(IEnumerable<string> items)
    {
        return items
            .Where(item => ContainsAny(item, "卡", "堵", "blocked", "blocker", "waiting", "等", "没法", "不能", "问题"))
            .Select(item => new ProjectBlocker
            {
                Summary = item,
                SeverityLabel = ContainsAny(item, "blocked", "blocker", "没法", "不能") ? "high" : "medium",
                UpdatedAt = DateTimeOffset.Now
            })
            .Take(3)
            .ToList();
    }

    private static List<ProjectEvidenceItem> BuildDigestEvidence(ProjectDigestProject project, DateTimeOffset now)
    {
        var evidenceItems = new List<ProjectEvidenceItem>();

        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            evidenceItems.Add(new ProjectEvidenceItem
            {
                SourceType = "project-digest",
                SourceLabel = "project digest",
                Summary = project.Summary,
                Detail = project.Name,
                Weight = 70,
                IndicatesProgress = true,
                ObservedAt = now
            });
        }

        foreach (var item in project.Items.Take(4))
        {
            evidenceItems.Add(new ProjectEvidenceItem
            {
                SourceType = "project-digest",
                SourceLabel = "project digest",
                Summary = item,
                Detail = project.Name,
                Weight = 55,
                IndicatesProgress = !ContainsAny(item, "卡", "blocked", "blocker", "waiting"),
                ObservedAt = now
            });
        }

        return evidenceItems;
    }

    private static List<ProjectEvidenceItem> MergeEvidence(
        IEnumerable<ProjectEvidenceItem> original,
        IEnumerable<ProjectEvidenceItem> incoming,
        int maxCount)
    {
        return original
            .Concat(incoming)
            .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
            .GroupBy(item => $"{Normalize(item.SourceType)}::{Normalize(item.Summary)}")
            .Select(group => group.OrderByDescending(item => item.ObservedAt).First())
            .OrderByDescending(item => item.ObservedAt)
            .Take(maxCount)
            .ToList();
    }

    private static List<ProjectBlocker> MergeBlockers(
        IEnumerable<ProjectBlocker> original,
        IEnumerable<ProjectBlocker> incoming,
        int maxCount)
    {
        return original
            .Concat(incoming)
            .Where(blocker => !string.IsNullOrWhiteSpace(blocker.Summary))
            .GroupBy(blocker => Normalize(blocker.Summary))
            .Select(group => group.OrderByDescending(blocker => blocker.UpdatedAt).First())
            .OrderByDescending(blocker => blocker.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    private static int InferMomentumScore(ProjectDigestProject project, int blockerCount)
    {
        var score = project.Priority switch
        {
            "now" => 72,
            "later" => 38,
            _ => 56
        };

        score += Math.Min(12, project.Items.Count * 4);
        score -= blockerCount switch
        {
            >= 2 => 22,
            1 => 12,
            _ => 0
        };

        return ClampScore(score);
    }

    private static int InferClarityScore(ProjectDigestProject project, string expectedDeliverable, string currentMilestone)
    {
        var score = 18;
        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(currentMilestone))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(expectedDeliverable))
        {
            score += 18;
        }

        score += Math.Min(12, project.Keywords.Count * 3);
        return ClampScore(score);
    }

    private static int InferRiskScore(ProjectDigestProject project, int blockerCount)
    {
        var score = 16;
        if (blockerCount > 0)
        {
            score += blockerCount >= 2 ? 42 : 25;
        }

        if (string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 12;
        }

        if (project.Priority == "now" && blockerCount > 0)
        {
            score += 8;
        }

        return ClampScore(score);
    }

    private static int InferDriftScore(ProjectDigestProject project, string expectedDeliverable)
    {
        var score = project.Priority == "later" ? 34 : 14;
        if (string.IsNullOrWhiteSpace(expectedDeliverable))
        {
            score += 12;
        }

        if (project.Items.Count >= 4 && string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 10;
        }

        return ClampScore(score);
    }

    private static int InferConfidenceScore(ProjectDigestProject project)
    {
        var score = 24;
        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            score += 15;
        }

        score += Math.Min(28, project.Items.Count * 7);
        score += Math.Min(20, project.Keywords.Count * 4);
        return ClampScore(score);
    }

    private static ProjectProgressSnapshot BuildProgressSnapshot(
        ProjectMemory memory,
        ProjectDigestProject project,
        DateTimeOffset now)
    {
        var statusLabel = memory.Blockers.Count > 0
            ? "blocked"
            : project.Priority switch
            {
                "now" => "active",
                "later" => "parked",
                _ => "queued"
            };

        var focusSummary = !string.IsNullOrWhiteSpace(memory.CurrentMilestone)
            ? memory.CurrentMilestone
            : !string.IsNullOrWhiteSpace(memory.NextAction)
                ? memory.NextAction
                : memory.Name;

        var healthSummary = memory.Blockers.Count > 0
            ? $"Blocked by {memory.Blockers[0].Summary}"
            : !string.IsNullOrWhiteSpace(memory.ExpectedDeliverable)
                ? $"Target deliverable: {memory.ExpectedDeliverable}"
                : $"Momentum {memory.MomentumScore}/100";

        return new ProjectProgressSnapshot
        {
            StatusLabel = statusLabel,
            FocusSummary = focusSummary,
            HealthSummary = healthSummary,
            UpdatedAt = now
        };
    }

    private static int ClampScore(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }

    private static bool ContainsAny(string input, params string[] needles)
    {
        return needles.Any(needle => input.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectMemory? FindBestKnownProject(string item, IReadOnlyList<ProjectMemory> knownProjects)
    {
        var normalizedItem = Normalize(item);
        var itemKeywords = ExtractKeywords(item);

        return knownProjects
            .Select(project => new
            {
                Project = project,
                Score = GetProjectScore(project, normalizedItem, itemKeywords)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .Select(entry => entry.Project)
            .FirstOrDefault();
    }

    private static ProjectMemory? FindBestProjectMemory(ProjectDigestProject project, IEnumerable<ProjectMemory> knownProjects)
    {
        var normalizedName = Normalize(project.Name);
        var projectKeywords = project.Keywords.Count > 0 ? project.Keywords : ExtractKeywords(string.Join(" ", project.Items));

        return knownProjects
            .Select(memory => new
            {
                Memory = memory,
                Score = GetProjectScore(memory, normalizedName, projectKeywords)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .Select(entry => entry.Memory)
            .FirstOrDefault();
    }

    private static int GetProjectScore(ProjectMemory project, string normalizedText, IReadOnlyList<string> keywords)
    {
        var normalizedName = Normalize(project.Name);
        if (normalizedName == normalizedText)
        {
            return 100;
        }

        var score = 0;
        if (!string.IsNullOrWhiteSpace(normalizedText)
            && normalizedName.Contains(normalizedText, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        foreach (var keyword in keywords)
        {
            if (project.Keywords.Any(existing => Normalize(existing) == Normalize(keyword)))
            {
                score += 12;
            }

            if (normalizedName.Contains(Normalize(keyword), StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
        }

        return score;
    }

    private static List<string> ExtractItems(string input)
    {
        var numberedMatches = NumberedItemRegex.Matches(input);
        if (numberedMatches.Count > 0)
        {
            return numberedMatches
                .Select(match => match.Groups[1].Value.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return input
            .Split(['\r', '\n', '，', ';', '；', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string InferProjectName(string item)
    {
        var candidate = item.Trim();

        var openIndex = candidate.IndexOf('（');
        if (openIndex < 0)
        {
            openIndex = candidate.IndexOf('(');
        }

        if (openIndex >= 0)
        {
            var closeIndex = candidate.IndexOfAny(['）', ')'], openIndex + 1);
            if (closeIndex > openIndex)
            {
                var inside = candidate[(openIndex + 1)..closeIndex].Trim();
                if (!string.IsNullOrWhiteSpace(inside))
                {
                    candidate = inside + candidate[..openIndex].Trim();
                }
            }
        }

        foreach (var prefix in VerbPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[prefix.Length..].Trim();
                break;
            }
        }

        candidate = candidate
            .Trim('，', '。', ':', '：', '(', ')', '（', '）')
            .Trim();

        if (candidate.Length > 14)
        {
            candidate = candidate[..14].Trim();
        }

        return string.IsNullOrWhiteSpace(candidate) ? "待确认项目线" : candidate;
    }

    private static List<string> ExtractKeywords(string input, IReadOnlyList<string>? seedKeywords = null)
    {
        var keywords = new List<string>();

        if (seedKeywords is not null)
        {
            keywords.AddRange(seedKeywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)));
        }

        var fragments = input
            .Split([' ', '\t', '\r', '\n', '，', '。', '、', ',', '.', '（', '）', '(', ')', ':', '：', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(fragment => fragment.Trim())
            .Where(fragment => fragment.Length >= 2 && !StopKeywords.Contains(fragment))
            .Where(fragment => !fragment.All(char.IsDigit));

        keywords.AddRange(fragments);
        return MergeStrings([], keywords, maxCount: 8);
    }

    private static string BuildFollowUpPrompt(IReadOnlyList<ProjectDigestProject> projects)
    {
        var mainProject = projects
            .OrderBy(project => project.Priority == "now" ? 0 : project.Priority == "next" ? 1 : 2)
            .ThenByDescending(project => project.Items.Count)
            .FirstOrDefault();

        return mainProject is null
            ? "你继续往下说，我会帮你把项目线捋顺。"
            : $"如果你愿意，我下一步先把“{mainProject.Name}”这条拆成今天能动的第一步。";
    }

    private static string GetKindLabel(string matchType)
    {
        return matchType switch
        {
            "existing" => "旧项目",
            "unknown" => "待确认",
            _ => "项目线"
        };
    }

    private static string GetPriorityLabel(string priority)
    {
        return priority switch
        {
            "now" => "先动",
            "later" => "先挂着",
            "unknown" => "待确认",
            _ => "下一顺位"
        };
    }

    private static string NormalizePriorityLabel(string priorityLabel)
    {
        return priorityLabel switch
        {
            "先动" => "now",
            "先挂着" => "later",
            "待确认" => "unknown",
            _ => "next"
        };
    }

    private static List<string> MergeStrings(
        IEnumerable<string> original,
        IEnumerable<string> incoming,
        int maxCount = 10)
    {
        return original
            .Concat(incoming)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    private static string Normalize(string text)
    {
        return new string(text
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray())
            .ToLowerInvariant();
    }
}
