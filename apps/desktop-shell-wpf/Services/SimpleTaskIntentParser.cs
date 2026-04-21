using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class SimpleTaskIntentParser
{
    private static readonly string[] DonePrefixes = ["完成", "做完", "搞定", "收尾", "提交"];
    private static readonly string[] DoingPrefixes = ["开始", "继续", "先做", "推进", "处理"];
    private static readonly string[] BlockedPrefixes = ["卡住", "阻塞", "搁置", "先放一下", "停一下"];
    private static readonly string[] EmotionKeywords =
    [
        "有点烦",
        "不想做",
        "好累",
        "好烦",
        "好焦虑",
        "好崩",
        "怎么办",
        "你是谁",
        "陪我",
        "在吗",
        "聊聊",
        "谢谢",
        "难受",
        "没劲",
        "心情",
        "委屈",
        "烦死了",
        "撑不住",
        "想躺",
        "不开心"
    ];

    private static readonly string[] ActionPrefixes =
    [
        "我要",
        "我想",
        "我得",
        "先",
        "把",
        "写",
        "做",
        "改",
        "整理",
        "提交",
        "补",
        "联系",
        "回复",
        "复习",
        "买",
        "洗",
        "处理",
        "修",
        "搞",
        "盯"
    ];

    private static readonly string[] TaskNouns =
    [
        "周报",
        "汇报",
        "文档",
        "代码",
        "实验",
        "作业",
        "任务",
        "邮件",
        "ppt",
        "方案",
        "bug",
        "接口",
        "数据",
        "家务",
        "洗衣",
        "买菜",
        "收拾",
        "wpf"
    ];

    public bool TryParseTaskIntent(string input, out ParsedTaskIntent intent)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            intent = default;
            return false;
        }

        if (TryBuildStateChange(normalized, DonePrefixes, CompanionTaskState.Done, out var doneIntent))
        {
            intent = doneIntent;
            return true;
        }

        if (TryBuildStateChange(normalized, BlockedPrefixes, CompanionTaskState.Blocked, out var blockedIntent))
        {
            intent = blockedIntent;
            return true;
        }

        if (TryBuildStateChange(normalized, DoingPrefixes, CompanionTaskState.Doing, out var doingIntent))
        {
            intent = doingIntent;
            return true;
        }

        if (LooksLikeConversation(normalized))
        {
            intent = default;
            return false;
        }

        if (!LooksLikeTaskCreation(normalized))
        {
            intent = default;
            return false;
        }

        var title = StripSoftPrefixes(normalized);
        intent = new ParsedTaskIntent(
            TaskIntentAction.CreateTask,
            title,
            title,
            InferCreateState(normalized),
            InferCategory(normalized),
            BuildNote(normalized));

        return true;
    }

    private static bool TryBuildStateChange(
        string input,
        IReadOnlyList<string> prefixes,
        CompanionTaskState state,
        out ParsedTaskIntent intent)
    {
        foreach (var prefix in prefixes)
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = TrimDecorators(input[prefix.Length..]);
            intent = new ParsedTaskIntent(
                TaskIntentAction.UpdateExistingTask,
                string.IsNullOrWhiteSpace(title) ? input : title,
                string.IsNullOrWhiteSpace(title) ? input : title,
                state,
                InferCategory(title),
                BuildNote(input));

            return true;
        }

        intent = default;
        return false;
    }

    private static bool LooksLikeTaskCreation(string input)
    {
        if (input.Contains("提醒我", StringComparison.OrdinalIgnoreCase)
            || input.Contains("帮我记", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (LooksLikeConversation(input))
        {
            return false;
        }

        var hasTaskNoun = ContainsAny(input, TaskNouns);
        var hasActionCue = StartsWithAny(input, ActionPrefixes)
            || ContainsAny(input, "写完", "做完", "改完", "盯住", "推进", "处理", "完成", "提交", "收尾", "整理");

        if (hasTaskNoun && hasActionCue)
        {
            return true;
        }

        if (hasTaskNoun)
        {
            return true;
        }

        return StartsWithAny(input, ActionPrefixes);
    }

    private static bool LooksLikeConversation(string input)
    {
        return ContainsAny(input, EmotionKeywords);
    }

    private static CompanionTaskState InferCreateState(string input)
    {
        return input.StartsWith("正在", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("继续", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("先做", StringComparison.OrdinalIgnoreCase)
            ? CompanionTaskState.Doing
            : CompanionTaskState.Todo;
    }

    private static string InferCategory(string input)
    {
        var normalized = input.ToLowerInvariant();

        if (ContainsAny(normalized, "wpf", "代码", "开发", "接口", "bug", "调试", "脚本"))
        {
            return "开发";
        }

        if (ContainsAny(normalized, "论文", "实验", "课程", "复习", "作业", "学习"))
        {
            return "学习";
        }

        if (ContainsAny(normalized, "汇报", "周报", "文档", "邮件", "ppt", "方案", "工作"))
        {
            return "工作";
        }

        if (ContainsAny(normalized, "洗衣", "买菜", "收拾", "健身", "散步", "生活", "家务"))
        {
            return "生活";
        }

        return "日常";
    }

    private static string BuildNote(string input)
    {
        if (input.Contains("今天", StringComparison.OrdinalIgnoreCase)
            || input.Contains("今晚", StringComparison.OrdinalIgnoreCase))
        {
            return "时间线先按今天处理。";
        }

        if (input.Contains("明天", StringComparison.OrdinalIgnoreCase))
        {
            return "时间线先按明天处理。";
        }

        return "她已经替你把这件事记住了。";
    }

    private static string StripSoftPrefixes(string input)
    {
        var result = input;

        foreach (var prefix in new[] { "我要", "我想", "我得", "帮我", "记得", "今天", "今晚", "明天", "先", "把" })
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[prefix.Length..];
                break;
            }
        }

        return TrimDecorators(result);
    }

    private static string Normalize(string input)
    {
        return string.Join(" ", input.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string TrimDecorators(string input)
    {
        return input
            .Trim()
            .Trim('，', '。', '！', '？', ',', '.', '!', '?', ':', '：')
            .Trim();
    }

    private static bool ContainsAny(string input, params string[] keywords)
    {
        return keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string input, IReadOnlyList<string> keywords)
    {
        return keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithAny(string input, params string[] prefixes)
    {
        return prefixes.Any(prefix => input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithAny(string input, IReadOnlyList<string> prefixes)
    {
        return prefixes.Any(prefix => input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

public enum TaskIntentAction
{
    CreateTask,
    UpdateExistingTask
}

public readonly record struct ParsedTaskIntent(
    TaskIntentAction Action,
    string Title,
    string MatchQuery,
    CompanionTaskState State,
    string Category,
    string Note);
