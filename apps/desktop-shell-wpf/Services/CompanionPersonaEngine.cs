using DesktopCompanion.WpfHost.Cognition;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class CompanionPersonaEngine
{
    public string BuildWelcomeMessage()
    {
        return TuanziCognitionProfile.WelcomeMessage;
    }

    public string ReplyToTaskIntent(
        string feedback,
        IReadOnlyList<CompanionTask> orderedTasks,
        bool supervisionEnabled,
        bool focusSprintActive)
    {
        var topTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Doing)
            ?? orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Todo)
            ?? orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Blocked);

        var moodTail = focusSprintActive
            ? "现在正在专注冲刺里，我会把注意力继续按在这条主线上。"
            : supervisionEnabled
                ? "我会继续盯着，不会让它悄悄掉下去。"
                : "虽然你把监督先暂停了，但这条事我还是替你记着。";

        if (topTask is null)
        {
            return $"{feedback} {moodTail}";
        }

        return $"{feedback} 现在最值得盯的是“{topTask.Title}”。{moodTail}";
    }

    public string ReplyToConversation(
        string userInput,
        IReadOnlyList<CompanionTask> orderedTasks,
        bool supervisionEnabled,
        bool focusSprintActive)
    {
        var normalized = userInput.ToLowerInvariant();
        var topTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Doing)
            ?? orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Todo)
            ?? orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Blocked);

        if (ContainsAny(normalized, "你是谁", "你叫什么", "你是干嘛的"))
        {
            return "我是团子，一个会陪你聊天、也会盯进度的桌面搭子。你不用把我当成冷冰冰的待办框，我更像坐在你旁边替你盯节奏的人。";
        }

        if (ContainsAny(normalized, "在吗", "陪我", "说说话", "聊聊"))
        {
            return "在，我一直挂在这里。你想先吐槽两句，还是让我把今天最烦的那件事拎出来陪你做？";
        }

        if (ContainsAny(normalized, "累", "困", "烦", "崩", "焦虑", "不想做", "没劲", "难受"))
        {
            if (topTask is null)
            {
                return "那我先不催你。你不用一下子把自己整理好，只要告诉我现在最烦的是哪件事，我先陪你把它缩成一个能动的小动作。";
            }

            return $"听到了。你现在不用把所有事一起扛住，我们先只盯“{topTask.Title}”这一件，做到最小一步就算赢。";
        }

        if (ContainsAny(normalized, "怎么办", "怎么搞", "怎么做", "没思路"))
        {
            if (topTask is null)
            {
                return "先别急着找完整答案。你先告诉我你卡在哪一块，我会陪你把下一步缩到十分钟能动的程度。";
            }

            return $"先别想太大，我们就拿“{topTask.Title}”开刀。先做一个十分钟内能完成的动作，我再陪你往下拎。";
        }

        if (ContainsAny(normalized, "谢谢", "多谢"))
        {
            return "不客气。你不用一个人默默扛着，有事就继续丢给我，我会替你接着。";
        }

        if (focusSprintActive)
        {
            return "我听着呢。不过你现在在冲刺里，我会更偏向把你拉回主线。你可以继续跟我说，但我们先别让注意力跑掉。";
        }

        if (!supervisionEnabled)
        {
            return "虽然你把自动监督停了，但我还在。你可以把我当成不会多嘴的同桌，想说什么就说。";
        }

        return topTask is null
            ? "我听见了。你可以继续跟我说，也可以直接把今天最想逃的一件事交给我。"
            : $"我听见了。现在我脑子里最挂着的是“{topTask.Title}”，但你想先聊感受也可以，我不会只盯任务。";
    }

    public string BuildReviewMessage(IReadOnlyList<CompanionTask> orderedTasks)
    {
        var blockedTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Blocked);
        if (blockedTask is not null)
        {
            return $"我回来梳理了。现在最该正面处理的是“{blockedTask.Title}”，别让它继续偷偷挂着。";
        }

        var doingTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Doing);
        if (doingTask is not null)
        {
            return $"我回来看看，你现在主线还是“{doingTask.Title}”。如果已经推进了，就直接告诉我，我帮你往下收口。";
        }

        var todoTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Todo);
        if (todoTask is not null)
        {
            return $"我回来盯进度了。你还没点亮主线，那就先从“{todoTask.Title}”开始，别同时扛一堆。";
        }

        return "这一轮看起来已经收干净了。你可以先喘口气，等有新事再扔给我。";
    }

    public string BuildFocusSprintMessage(string topTask)
    {
        return $"专注冲刺已经开始了。接下来 25 分钟，我就陪你盯“{topTask}”，别的先一律靠后。";
    }

    public string BuildFocusSprintFinishedMessage(IReadOnlyList<CompanionTask> orderedTasks)
    {
        var topTask = orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Doing)
            ?? orderedTasks.FirstOrDefault(task => task.State == CompanionTaskState.Todo);

        return topTask is null
            ? "25 分钟到了。你这轮已经清得挺干净，要不要顺手跟我复盘一下？"
            : $"25 分钟到了。“{topTask.Title}”现在推进到哪一步了？你直接跟我说，我帮你记。";
    }

    private static bool ContainsAny(string input, params string[] keywords)
    {
        return keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
