using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopCompanion.WpfHost.Cognition;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class OpenAiChatService
{
    private readonly HttpClient _httpClient;
    private readonly ProjectCognitionQualityGuard _qualityGuard;

    public OpenAiChatService(
        HttpClient? httpClient = null,
        string? apiKey = null,
        string? modelName = null,
        string? baseUrl = null,
        ProjectCognitionQualityGuard? qualityGuard = null)
    {
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        ModelName = modelName ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5";
        _qualityGuard = qualityGuard ?? new ProjectCognitionQualityGuard();

        var resolvedBaseUrl = baseUrl
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            ?? "https://api.openai.com/v1/";

        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(resolvedBaseUrl),
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public string ApiKey { get; }

    public string ModelName { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public Task<OpenAiAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(new OpenAiAvailabilityResult(false, "ChatGPT 未配置"));
        }

        return Task.FromResult(new OpenAiAvailabilityResult(true, $"ChatGPT · {ModelName}"));
    }

    public async Task<string?> GenerateReplyAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string userInput,
        string? taskFeedback,
        bool supervisionEnabled,
        bool focusSprintActive,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        var payload = new OpenAiResponsesRequest
        {
            Model = ModelName,
            Instructions = TuanziCognitionProfile.BuildCompanionSystemPrompt(),
            Input = BuildInput(conversationHistory, orderedTasks, knownProjects, userInput, taskFeedback, supervisionEnabled, focusSprintActive)
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return ExtractOutputText(document.RootElement);
    }

    public async Task<ProjectCognitionDigest?> AnalyzeProjectDumpAsync(
        string userInput,
        IReadOnlyList<ProjectMemory> knownProjects,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        var knownProjectLines = knownProjects.Count == 0
            ? "当前没有已知项目线。"
            : string.Join(
                "；",
                knownProjects.Take(8).Select(project =>
                    $"{project.Name}（关键词：{string.Join("、", project.Keywords.Take(4))}）"));

        var payload = new OpenAiResponsesRequest
        {
            Model = ModelName,
            Instructions = TuanziCognitionProfile.BuildProjectCognitionSystemPrompt(),
            Input =
                $"已知项目线：{knownProjectLines}\n" +
                $"用户刚发来的混合清单：\n{userInput}"
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var rawText = ExtractOutputText(document.RootElement);

        if (!ProjectCognitionService.TryDeserializeDigest(rawText ?? string.Empty, out var digest))
        {
            return null;
        }

        return _qualityGuard.Normalize(digest);
    }

    private static string BuildInput(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string userInput,
        string? taskFeedback,
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

        var historyLines = conversationHistory
            .TakeLast(10)
            .Select(message => message.Role == ConversationRole.User
                ? $"用户：{message.Text}"
                : $"团子：{message.Text}");

        var taskFeedbackLine = string.IsNullOrWhiteSpace(taskFeedback)
            ? "这轮对话里没有新的任务记录动作。"
            : $"这轮对话里系统已经完成的记录动作：{taskFeedback}";

        var supervisionLine = supervisionEnabled ? "监督是开启的。" : "监督目前暂停。";
        var sprintLine = focusSprintActive ? "当前正在专注冲刺。" : "当前不在专注冲刺。";

        return
            $"已记录任务：{taskLines}\n" +
            $"已知项目线：{projectLines}\n" +
            $"{taskFeedbackLine}\n" +
            $"{supervisionLine}{sprintLine}\n" +
            $"最近对话：\n{string.Join("\n", historyLines)}\n" +
            $"用户刚刚说：{userInput}";
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputTextElement)
            && outputTextElement.ValueKind == JsonValueKind.String)
        {
            var directText = outputTextElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }
        }

        if (!root.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() != "message")
            {
                continue;
            }

            if (!outputItem.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentElement.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("type", out var contentTypeElement)
                    || contentTypeElement.GetString() != "output_text")
                {
                    continue;
                }

                if (contentItem.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    var text = textElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                }
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
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

    private sealed class OpenAiResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("instructions")]
        public string Instructions { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;
    }
}

public readonly record struct OpenAiAvailabilityResult(bool IsAvailable, string StatusLabel);
