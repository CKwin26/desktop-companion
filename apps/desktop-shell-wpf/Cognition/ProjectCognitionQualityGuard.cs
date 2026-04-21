using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Cognition;

public sealed class ProjectCognitionQualityGuard
{
    public ProjectCognitionDigest? Normalize(ProjectCognitionDigest? digest)
    {
        if (digest is null)
        {
            return null;
        }

        var cleanedProjects = digest.Projects
            .Select(NormalizeProject)
            .Where(project => project is not null)
            .Cast<ProjectDigestProject>()
            .GroupBy(project => NormalizeText(project.Name))
            .Select(group => MergeGroup(group.ToList()))
            .OrderBy(project => GetPriorityRank(project.Priority))
            .ThenByDescending(project => project.Items.Count)
            .Take(6)
            .ToList();

        if (cleanedProjects.Count == 0)
        {
            return null;
        }

        var suggestedFocus = NormalizeShortText(digest.SuggestedFocus);
        if (string.IsNullOrWhiteSpace(suggestedFocus))
        {
            var focusProject = cleanedProjects
                .OrderBy(project => GetPriorityRank(project.Priority))
                .ThenByDescending(project => project.Items.Count)
                .First();
            suggestedFocus = string.IsNullOrWhiteSpace(focusProject.NextAction) ? focusProject.Name : focusProject.NextAction;
        }

        var nowItems = NormalizeActionItems(digest.NowItems, cleanedProjects.Where(project => project.Priority == "now"));
        var nextItems = NormalizeActionItems(digest.NextItems, cleanedProjects.Where(project => project.Priority == "next"));
        var laterItems = NormalizeActionItems(digest.LaterItems, cleanedProjects.Where(project => project.Priority == "later"));

        var summary = NormalizeShortText(digest.Summary);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"这批事情目前更像 {cleanedProjects.Count} 条项目线。";
        }

        return new ProjectCognitionDigest
        {
            Summary = summary,
            FollowUpPrompt = NormalizeFollowUpPrompt(digest.FollowUpPrompt, cleanedProjects),
            SuggestedFocus = suggestedFocus,
            NowItems = nowItems,
            NextItems = nextItems,
            LaterItems = laterItems,
            Projects = cleanedProjects
        };
    }

    private static ProjectDigestProject? NormalizeProject(ProjectDigestProject? project)
    {
        if (project is null)
        {
            return null;
        }

        var name = NormalizeShortText(project.Name);
        var items = (project.Items ?? [])
            .Select(NormalizeShortText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (string.IsNullOrWhiteSpace(name) && items.Count == 0)
        {
            return null;
        }

        name = string.IsNullOrWhiteSpace(name)
            ? InferNameFromItems(items)
            : name;

        var summary = NormalizeShortText(project.Summary);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"这条项目线里目前先记到：{items.FirstOrDefault() ?? name}";
        }

        var nextAction = NormalizeShortText(project.NextAction);
        if (string.IsNullOrWhiteSpace(nextAction))
        {
            nextAction = items.FirstOrDefault() ?? name;
        }

        var keywords = (project.Keywords ?? [])
            .Concat(TokenizeKeywords(name))
            .Concat(items.SelectMany(TokenizeKeywords))
            .Select(NormalizeShortText)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return new ProjectDigestProject
        {
            Name = name,
            MatchType = NormalizeMatchType(project.MatchType),
            Summary = summary,
            Priority = NormalizePriority(project.Priority),
            NextAction = nextAction,
            Keywords = keywords,
            Items = items.Count > 0 ? items : [name]
        };
    }

    private static ProjectDigestProject MergeGroup(IReadOnlyList<ProjectDigestProject> group)
    {
        var first = group[0];
        return new ProjectDigestProject
        {
            Name = first.Name,
            MatchType = group.Any(project => project.MatchType == "existing")
                ? "existing"
                : group.Any(project => project.MatchType == "candidate")
                    ? "candidate"
                    : "unknown",
            Summary = group.Select(project => project.Summary).First(summary => !string.IsNullOrWhiteSpace(summary)),
            Priority = group.OrderBy(project => GetPriorityRank(project.Priority)).Select(project => project.Priority).First(),
            NextAction = group.Select(project => project.NextAction).First(action => !string.IsNullOrWhiteSpace(action)),
            Keywords = group.SelectMany(project => project.Keywords).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
            Items = group.SelectMany(project => project.Items).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
        };
    }

    private static List<string> NormalizeActionItems(
        IEnumerable<string>? rawItems,
        IEnumerable<ProjectDigestProject> fallbackProjects)
    {
        var cleaned = (rawItems ?? [])
            .Select(NormalizeShortText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (cleaned.Count > 0)
        {
            return cleaned;
        }

        return fallbackProjects
            .Select(project => string.IsNullOrWhiteSpace(project.NextAction) ? project.Name : project.NextAction)
            .Select(NormalizeShortText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static string NormalizeFollowUpPrompt(string? followUpPrompt, IReadOnlyList<ProjectDigestProject> projects)
    {
        var cleaned = NormalizeShortText(followUpPrompt);
        if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length <= 72)
        {
            return cleaned;
        }

        var topProject = projects
            .OrderBy(project => GetPriorityRank(project.Priority))
            .ThenByDescending(project => project.Items.Count)
            .FirstOrDefault();

        return topProject is null
            ? "你继续往下说，我会帮你把事情排轻重。"
            : $"如果你愿意，我先陪你把“{topProject.Name}”拆成今天能动的第一步。";
    }

    private static string NormalizeMatchType(string? matchType)
    {
        return matchType?.Trim().ToLowerInvariant() switch
        {
            "existing" => "existing",
            "unknown" => "unknown",
            _ => "candidate"
        };
    }

    private static string NormalizePriority(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "now" => "now",
            "later" => "later",
            "unknown" => "unknown",
            _ => "next"
        };
    }

    private static int GetPriorityRank(string? priority)
    {
        return priority switch
        {
            "now" => 0,
            "next" => 1,
            "later" => 2,
            _ => 3
        };
    }

    private static IEnumerable<string> TokenizeKeywords(string input)
    {
        return input
            .Split([' ', '\t', '\r', '\n', '，', '。', '、', ',', '.', '（', '）', '(', ')', ':', '：', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Where(token => !token.All(char.IsDigit));
    }

    private static string InferNameFromItems(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return "待确认项目线";
        }

        var first = items[0].Trim();
        return first.Length <= 14 ? first : first[..14].Trim();
    }

    private static string NormalizeShortText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string NormalizeText(string value)
    {
        return new string(value
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray())
            .ToLowerInvariant();
    }
}
