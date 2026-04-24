using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DesktopCompanion.WpfHost.Cognition;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class OllamaChatService
{
    private readonly HttpClient _httpClient;
    private readonly ProjectCognitionQualityGuard _qualityGuard;

    public OllamaChatService(
        HttpClient? httpClient = null,
        string modelName = "gemma4:e4b",
        ProjectCognitionQualityGuard? qualityGuard = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = TimeSpan.FromSeconds(90)
        };

        ModelName = modelName;
        _qualityGuard = qualityGuard ?? new ProjectCognitionQualityGuard();
    }

    public string ModelName { get; }

    public async Task<OllamaAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>("api/tags", cancellationToken);
            var models = response?.Models ?? [];
            var matched = models.FirstOrDefault(model => string.Equals(model.Name, ModelName, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                return new OllamaAvailabilityResult(true, $"Ollama · {matched.Name}");
            }

            if (models.Count > 0)
            {
                return new OllamaAvailabilityResult(true, $"Ollama · {models[0].Name}");
            }

            return new OllamaAvailabilityResult(false, "Ollama 已连接，但还没有模型");
        }
        catch
        {
            return new OllamaAvailabilityResult(false, "Ollama 未连接");
        }
    }

    public async Task<string?> GenerateReplyAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string userInput,
        string? taskFeedback,
        string? activeWorkspaceContext,
        bool supervisionEnabled,
        bool focusSprintActive,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = ModelName,
            Stream = false,
            Think = false,
            Messages = BuildMessages(
                conversationHistory,
                orderedTasks,
                knownProjects,
                taskFeedback,
                activeWorkspaceContext,
                supervisionEnabled,
                focusSprintActive),
            Options = new OllamaOptions
            {
                Temperature = 0.8,
                NumPredict = 160
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        var content = payload?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (LooksLikeEncodingFallback(content, userInput))
        {
            return null;
        }

        return NormalizeReply(content);
    }

    public async Task<ProjectCognitionDigest?> AnalyzeProjectDumpAsync(
        string userInput,
        IReadOnlyList<ProjectMemory> knownProjects,
        CancellationToken cancellationToken = default)
    {
        var knownProjectLines = knownProjects.Count == 0
            ? "当前没有已知项目线。"
            : string.Join(
                "；",
                knownProjects.Take(8).Select(project =>
                    $"{project.Name}（关键词：{string.Join("、", project.Keywords.Take(4))}）"));

        var request = new OllamaChatRequest
        {
            Model = ModelName,
            Stream = false,
            Think = false,
            Messages =
            [
                new OllamaMessage
                {
                    Role = "system",
                    Content = TuanziCognitionProfile.BuildProjectCognitionSystemPrompt()
                },
                new OllamaMessage
                {
                    Role = "user",
                    Content =
                        $"已知项目线：{knownProjectLines}\n" +
                        $"用户刚发来的混合清单：\n{userInput}"
                }
            ],
            Options = new OllamaOptions
            {
                Temperature = 0.2,
                NumPredict = 480
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        var rawText = payload?.Message?.Content?.Trim();

        if (!ProjectCognitionService.TryDeserializeDigest(rawText ?? string.Empty, out var digest))
        {
            return null;
        }

        return _qualityGuard.Normalize(digest);
    }

    public async Task<DistilledUserProfile?> AnalyzePersonalProfileAsync(
        string analysisInput,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = ModelName,
            Stream = false,
            Think = false,
            Messages =
            [
                new OllamaMessage
                {
                    Role = "system",
                    Content = TuanziCognitionProfile.BuildPersonalDistillationSystemPrompt()
                },
                new OllamaMessage
                {
                    Role = "user",
                    Content = analysisInput
                }
            ],
            Options = new OllamaOptions
            {
                Temperature = 0.2,
                NumPredict = 520
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        var rawText = payload?.Message?.Content?.Trim();

        if (!PersonalDistillationService.TryDeserializeProfile(rawText ?? string.Empty, out var profile))
        {
            return null;
        }

        return profile;
    }

    private static List<OllamaMessage> BuildMessages(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string? taskFeedback,
        string? activeWorkspaceContext,
        bool supervisionEnabled,
        bool focusSprintActive)
    {
        var messages = new List<OllamaMessage>
        {
            new()
            {
                Role = "system",
                Content = TuanziCognitionProfile.BuildCompanionSystemPrompt()
            },
            new()
            {
                Role = "system",
                Content = BuildTaskContext(orderedTasks, knownProjects, taskFeedback, activeWorkspaceContext, supervisionEnabled, focusSprintActive)
            }
        };

        foreach (var message in conversationHistory.TakeLast(10))
        {
            messages.Add(new OllamaMessage
            {
                Role = message.Role == ConversationRole.User ? "user" : "assistant",
                Content = message.Text
            });
        }

        return messages;
    }

    private static string BuildTaskContext(
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string? taskFeedback,
        string? activeWorkspaceContext,
        bool supervisionEnabled,
        bool focusSprintActive)
    {
        var taskLines = orderedTasks.Count == 0
            ? "当前还没有被记住的任务。"
            : string.Join(
                "；",
                orderedTasks.Take(4).Select(task => $"{task.Title}（{GetStateLabel(task.State)}）"));

        var projectLines = knownProjects.Count == 0
            ? "当前还没有被记住的项目线。"
            : string.Join(
                "；",
                knownProjects.Take(4).Select(project =>
                {
                    var focus = string.IsNullOrWhiteSpace(project.NextAction) ? "未定" : project.NextAction;
                    var priority = string.IsNullOrWhiteSpace(project.PriorityLabel) ? "未排顺位" : project.PriorityLabel;
                    return $"{project.Name}（{project.KindLabel} / {priority} / 下一步：{focus}）";
                }));

        var feedbackLine = string.IsNullOrWhiteSpace(taskFeedback)
            ? "这轮对话里没有新的任务记录动作。"
            : $"这轮对话里系统已经完成的记录动作：{taskFeedback}";

        var supervisionLine = supervisionEnabled ? "监督是开启的。" : "监督目前暂停。";
        var sprintLine = focusSprintActive ? "当前正在专注冲刺。" : "当前不在专注冲刺。";

        var workspaceLine = string.IsNullOrWhiteSpace(activeWorkspaceContext)
            ? "当前没有活跃工作区上下文。"
            : activeWorkspaceContext;

        return $"已记录任务：{taskLines}。已知项目线：{projectLines}。{workspaceLine}。{feedbackLine}。{supervisionLine}{sprintLine}";
    }

    private static string GetStateLabel(CompanionTaskState state)
    {
        return state switch
        {
            CompanionTaskState.Doing => "推进中",
            CompanionTaskState.Blocked => "阻塞",
            CompanionTaskState.Done => "完成",
            _ => "待启动"
        };
    }

    private static string NormalizeReply(string reply)
    {
        return string.Join(
            Environment.NewLine,
            reply.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool LooksLikeEncodingFallback(string reply, string userInput)
    {
        if (!ContainsCjk(userInput))
        {
            return false;
        }

        if (ContainsCjk(reply))
        {
            return false;
        }

        return reply.Contains("provide more context", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("complete your question", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("what you are looking for", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCjk(string text)
    {
        return text.Any(character => character is >= '\u4e00' and <= '\u9fff');
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaTagModel> Models { get; set; } = [];
    }

    private sealed class OllamaTagModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("think")]
        public bool Think { get; set; }

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = [];

        [JsonPropertyName("options")]
        public OllamaOptions Options { get; set; } = new();
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}

public readonly record struct OllamaAvailabilityResult(bool IsAvailable, string StatusLabel);
