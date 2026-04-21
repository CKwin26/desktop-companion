using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopCompanion.WpfHost.Models;
using DesktopCompanion.WpfHost.Services;

namespace DesktopCompanion.WpfHost.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> SampleTaskTitles =
    [
        "把任务监督系统文档补一版",
        "确认小人说话时的语气",
        "让聊天和任务记忆连起来",
        "先把 WPF 桌宠壳子站起来",
        "接一句话创建任务",
        "确认任务面板展开方式",
        "你记下来了什么",
        "你汇总一下现在的信息",
        "我今天有点烦，不太想做事"
    ];

    private readonly List<CompanionTask> _tasks = [];
    private readonly List<ProjectMemory> _projects = [];
    private readonly List<WorkspaceSourceMemory> _workspaceSources = [];
    private readonly List<ConversationMessage> _conversationHistory = [];
    private readonly LocalTaskStore _taskStore;
    private readonly LocalProjectStore _projectStore;
    private readonly LocalWorkspaceSourceStore _workspaceSourceStore;
    private readonly LocalPermissionStore _permissionStore;
    private readonly LocalConversationStore _conversationStore;
    private readonly SimpleTaskIntentParser _intentParser;
    private readonly CompanionPersonaEngine _personaEngine;
    private readonly ProjectCognitionService _projectCognitionService;
    private readonly WorkspaceIngestionService _workspaceIngestionService;
    private readonly VsCodeBridgeService _vsCodeBridgeService;
    private readonly OpenAiChatService _openAiChatService;
    private readonly OllamaChatService _ollamaChatService;
    private readonly DispatcherTimer _supervisionTimer;
    private readonly TimeSpan _reviewCadence = TimeSpan.FromMinutes(25);
    private readonly TimeSpan _focusSprintDuration = TimeSpan.FromMinutes(25);
    private readonly CompanionPermissionProfile _permissionProfile;

    private DateTimeOffset _nextReviewAt;
    private DateTimeOffset? _focusSprintEndsAt;

    private string _petName = "团子";
    private string _moodLabel = "在你这边";
    private string _topTaskTitle = "先和我说一句，现在最烦的是哪件事。";
    private string _statusLine = "你可以把任务、卡点和情绪都直接丢给我。";
    private string _whisperLine = "我会先回你，再顺手把真正要紧的事记下来。";
    private string _aiStatusLabel = "初始化中";
    private Brush _accentBrush = CreateBrush("#FF79B5AA");
    private Brush _accentTintBrush = CreateBrush("#3379B5AA");
    private Brush _whisperBrush = CreateBrush("#CC22363B");
    private PetMood _mood = PetMood.Idle;
    private bool _isPanelOpen;
    private bool _supervisionEnabled = true;
    private bool _isAwaitingReply;
    private string _panelSummary = "现在这里只有一块主聊天面板。你先说话，团子再替你记事。";
    private string _panelHint = "可以直接说心情，也可以直接交待任务。";
    private string _chatInput = string.Empty;
    private string _rhythmStatusLine = "监督中 · 下次梳理 25:00";
    private string _focusSprintStatusLine = "专注冲刺还没开始。";

    public MainWindowViewModel()
        : this(
            new LocalTaskStore(),
            new LocalProjectStore(),
            new LocalWorkspaceSourceStore(),
            new LocalPermissionStore(),
            new LocalConversationStore(),
            new SimpleTaskIntentParser(),
            new CompanionPersonaEngine(),
            new ProjectCognitionService(),
            new WorkspaceIngestionService(),
            new VsCodeBridgeService(),
            new OpenAiChatService(),
            new OllamaChatService())
    {
    }

    internal MainWindowViewModel(
        LocalTaskStore taskStore,
        LocalProjectStore projectStore,
        LocalWorkspaceSourceStore workspaceSourceStore,
        LocalPermissionStore permissionStore,
        LocalConversationStore conversationStore,
        SimpleTaskIntentParser intentParser,
        CompanionPersonaEngine personaEngine,
        ProjectCognitionService projectCognitionService,
        WorkspaceIngestionService workspaceIngestionService,
        VsCodeBridgeService vsCodeBridgeService,
        OpenAiChatService openAiChatService,
        OllamaChatService ollamaChatService)
    {
        _taskStore = taskStore;
        _projectStore = projectStore;
        _workspaceSourceStore = workspaceSourceStore;
        _permissionStore = permissionStore;
        _conversationStore = conversationStore;
        _intentParser = intentParser;
        _personaEngine = personaEngine;
        _projectCognitionService = projectCognitionService;
        _workspaceIngestionService = workspaceIngestionService;
        _vsCodeBridgeService = vsCodeBridgeService;
        _openAiChatService = openAiChatService;
        _ollamaChatService = ollamaChatService;
        _permissionProfile = _permissionStore.LoadProfile();

        ConversationMessages = [];
        RecentMemoryCards = [];
        SuggestionBubbles = [];

        LoadTasks();
        LoadProjects();
        LoadWorkspaceSources();
        LoadConversation();

        _nextReviewAt = DateTimeOffset.Now.Add(_reviewCadence);
        _supervisionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _supervisionTimer.Tick += (_, _) => HandleSupervisionTick();
        _supervisionTimer.Start();

        RefreshDashboard(GetLatestCompanionText());
        _ = InitializeAiAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PetName
    {
        get => _petName;
        private set => SetField(ref _petName, value);
    }

    public string MoodLabel
    {
        get => _moodLabel;
        private set => SetField(ref _moodLabel, value);
    }

    public string TopTaskTitle
    {
        get => _topTaskTitle;
        private set => SetField(ref _topTaskTitle, value);
    }

    public string StatusLine
    {
        get => _statusLine;
        private set => SetField(ref _statusLine, value);
    }

    public string WhisperLine
    {
        get => _whisperLine;
        private set => SetField(ref _whisperLine, value);
    }

    public string AiStatusLabel
    {
        get => _aiStatusLabel;
        private set => SetField(ref _aiStatusLabel, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    public Brush AccentTintBrush
    {
        get => _accentTintBrush;
        private set => SetField(ref _accentTintBrush, value);
    }

    public Brush WhisperBrush
    {
        get => _whisperBrush;
        private set => SetField(ref _whisperBrush, value);
    }

    public PetMood Mood
    {
        get => _mood;
        private set => SetField(ref _mood, value);
    }

    public bool IsPanelOpen
    {
        get => _isPanelOpen;
        private set
        {
            if (!SetField(ref _isPanelOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PanelToggleText));
        }
    }

    public bool SupervisionEnabled
    {
        get => _supervisionEnabled;
        private set
        {
            if (!SetField(ref _supervisionEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SupervisionToggleText));
        }
    }

    public bool IsAwaitingReply
    {
        get => _isAwaitingReply;
        private set
        {
            if (!SetField(ref _isAwaitingReply, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SendButtonText));
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    public bool HasRecentMemories => RecentMemoryCards.Count > 0;

    public string PanelToggleText => IsPanelOpen ? "先收起" : "继续聊";

    public string SupervisionToggleText => SupervisionEnabled ? "暂停监督" : "继续监督";

    public string SendButtonText => IsAwaitingReply ? "思考中..." : "发送";

    public bool CanSendMessage => !IsAwaitingReply;

    public string FocusSprintButtonText => _focusSprintEndsAt is null ? "专注 25 分钟" : "重新冲刺";

    public string PanelSummary
    {
        get => _panelSummary;
        private set => SetField(ref _panelSummary, value);
    }

    public string PanelHint
    {
        get => _panelHint;
        private set => SetField(ref _panelHint, value);
    }

    public string ChatInput
    {
        get => _chatInput;
        set => SetField(ref _chatInput, value);
    }

    public string RhythmStatusLine
    {
        get => _rhythmStatusLine;
        private set => SetField(ref _rhythmStatusLine, value);
    }

    public string FocusSprintStatusLine
    {
        get => _focusSprintStatusLine;
        private set => SetField(ref _focusSprintStatusLine, value);
    }

    public string MemoryEmptyText => "她现在还没替你记住具体的任务或项目线。先说一句，她会接住。";

    public ObservableCollection<ConversationMessageViewModel> ConversationMessages { get; }

    public ObservableCollection<RecentMemoryViewModel> RecentMemoryCards { get; }

    public ObservableCollection<SuggestionBubbleViewModel> SuggestionBubbles { get; }

    public bool HasSuggestionBubbles => SuggestionBubbles.Count > 0;

    public void TogglePanel()
    {
        if (!_permissionProfile.IsConfigured && !IsPanelOpen)
        {
            IsPanelOpen = true;
            const string permissionPrompt = "先告诉我你想给团子开什么权限吧。我会先列可选项，你点一个，我们再往下走。";
            EnsureRecentCompanionPrompt(permissionPrompt);
            WhisperLine = permissionPrompt;
            return;
        }

        IsPanelOpen = !IsPanelOpen;
        if (IsPanelOpen)
        {
            var prompt = "我在。你想先吐槽一下，还是让我把最烦的那件事拎出来？";
            EnsureRecentCompanionPrompt(prompt);
            WhisperLine = prompt;
        }
        else
        {
            WhisperLine = GetLatestCompanionText();
        }
    }

    public async Task UseSuggestionAsync(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        ChatInput = suggestion;
        await SendChatMessageAsync();
    }

    public async Task SendChatMessageAsync()
    {
        if (IsAwaitingReply)
        {
            return;
        }

        var input = ChatInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            WhisperLine = "你想说什么就直接打给我。";
            return;
        }

        AddConversation(ConversationRole.User, input);
        ChatInput = string.Empty;

        var provider = await ResolveActiveProviderAsync();
        string? taskFeedback = null;
        string fallbackReply;

        if (TryApplyPermissionPreset(input, out var permissionReply))
        {
            AddConversation(ConversationRole.Companion, permissionReply);
            RefreshDashboard(permissionReply);
            return;
        }

        if (LooksLikeVsCodeOpenRequest(input))
        {
            if (!TryResolveWorkspaceForExternalTools(input, out var toolWorkspacePath))
            {
                const string missingWorkspaceReply = "你先把项目路径发给我，或者先让我记住一个已授权项目目录，我再替你在 VS Code 里打开。";
                AddConversation(ConversationRole.Companion, missingWorkspaceReply);
                RefreshDashboard(missingWorkspaceReply);
                return;
            }

            var openSucceeded = _vsCodeBridgeService.TryOpenWorkspace(toolWorkspacePath, out var openReply);
            AddConversation(ConversationRole.Companion, openReply);
            RefreshDashboard(openReply);

            if (openSucceeded && !_permissionProfile.CanReadSelectedWorkspace && _workspaceSources.Count == 0)
            {
                PanelHint = "如果你愿意，后面也可以给团子开放项目目录读取权限。";
            }

            return;
        }

        if (LooksLikeCodexDispatchRequest(input))
        {
            if (!TryResolveWorkspaceForExternalTools(input, out var toolWorkspacePath))
            {
                const string missingWorkspaceReply = "你先把项目路径发给我，或者先让我记住一个已授权项目目录，我再把任务交给 Codex。";
                AddConversation(ConversationRole.Companion, missingWorkspaceReply);
                RefreshDashboard(missingWorkspaceReply);
                return;
            }

            var codexPrompt = ExtractCodexTaskPrompt(input);
            if (string.IsNullOrWhiteSpace(codexPrompt))
            {
                codexPrompt = "先阅读当前项目，然后告诉我这个项目现在最值得推进的下一步，并直接开始处理。";
            }

            IsAwaitingReply = true;
            AiStatusLabel = "Codex 开工中";
            WhisperLine = "团子正在把这件事交给 Codex 处理，我会把结果带回来。";
            RefreshDashboard("团子正在把这件事交给 Codex 处理，我会把结果带回来。");

            try
            {
                _vsCodeBridgeService.TryOpenWorkspace(toolWorkspacePath, out _);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await _vsCodeBridgeService.RunCodexTaskAsync(toolWorkspacePath, codexPrompt, timeoutCts.Token);
                var codexReply = result.IsSuccess
                    ? $"我已经把这件事交给 Codex 了。{result.Message}"
                    : $"这次我试着把任务交给 Codex，但没顺利跑完。{result.Message}";

                AiStatusLabel = result.IsSuccess ? "Codex 已完成" : "Codex 暂时没接住";
                codexReply = result.IsSuccess
                    ? BuildCodexSuccessReplySafe(result, toolWorkspacePath)
                    : result.IsTimedOut
                        ? BuildCodexTimeoutReplySafe(result)
                        : BuildCodexFailureReplySafe(result);
                AiStatusLabel = result.IsSuccess
                    ? "Codex 宸插畬鎴?"
                    : result.IsTimedOut
                        ? "Codex 宸茶秴鏃?"
                        : "Codex 鏆傛椂娌℃帴浣?";
                AddConversation(ConversationRole.Companion, codexReply);
                RefreshDashboard(codexReply);
            }
            finally
            {
                IsAwaitingReply = false;
            }

            return;
        }

        if (_workspaceIngestionService.TryExtractWorkspacePath(input, out var workspacePath))
        {
            if (!_permissionProfile.CanReadSelectedWorkspace)
            {
                const string noPermissionReply = "这一步我先不去读你电脑里的目录。你先从左边选一个权限方案，我再按你给的边界做事。";
                AddConversation(ConversationRole.Companion, noPermissionReply);
                RefreshDashboard(noPermissionReply);
                return;
            }

            IsAwaitingReply = true;
            AiStatusLabel = $"{provider.Label} 读文档中";
            WhisperLine = "团子去翻你给我的项目目录了，我先把能读的资料捋出来。";
            RefreshDashboard("团子去翻你给我的项目目录了，我先把能读的资料捋出来。");

            try
            {
                var scan = _workspaceIngestionService.ScanWorkspace(workspacePath);
                if (!scan.IsSuccess)
                {
                    AiStatusLabel = provider.Label;
                    AddConversation(ConversationRole.Companion, scan.Message);
                    RefreshDashboard(scan.Message);
                    return;
                }

                var digest = await provider.AnalyzeProjectDumpAsync(
                                 scan.AnalysisInput,
                                 GetOrderedProjects())
                             ?? _projectCognitionService.CreateFallbackDigest(
                                 string.Join(
                                     "\n",
                                     scan.Documents.Select(document => $"{document.FileName}: {document.Excerpt}")),
                                 GetOrderedProjects());

                if (_permissionProfile.CanBuildProjectMemoryFromDocs)
                {
                    _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                    PersistProjects();
                }

                if (_permissionProfile.CanRememberWorkspaceSources)
                {
                    UpsertWorkspaceSource(scan);
                    PersistWorkspaceSources();
                }

                var workspaceReply =
                    $"我先读了 {scan.RootLabel} 这一路里的 {scan.Documents.Count} 份资料，先替你归出了项目线。"
                    + _projectCognitionService.BuildCompanionReply(digest);

                workspaceReply = BuildWorkspaceImportReply(scan, digest);

                AiStatusLabel = provider.Label;
                AddConversation(ConversationRole.Companion, workspaceReply);
                RefreshDashboard(workspaceReply);
            }
            catch
            {
                const string failureReply = "这个目录我去翻了，但这轮还没稳稳读出来。你可以先给我 README、总结文档，或者把更具体的子目录发我。";
                AiStatusLabel = $"{provider.Label} 暂时没接住";
                AddConversation(ConversationRole.Companion, failureReply);
                RefreshDashboard(failureReply);
            }
            finally
            {
                IsAwaitingReply = false;
            }

            return;
        }

        if (_projectCognitionService.LooksLikeProjectDump(input))
        {
            IsAwaitingReply = true;
            AiStatusLabel = $"{provider.Label} 归类中";
            WhisperLine = "团子在替你辨认这批事分别挂在哪条项目线上...";
            RefreshDashboard("团子在替你辨认这批事分别挂在哪条项目线上...");

            try
            {
                var digest = await provider.AnalyzeProjectDumpAsync(
                                 input,
                                 GetOrderedProjects())
                             ?? _projectCognitionService.CreateFallbackDigest(input, GetOrderedProjects());

                _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                PersistProjects();

                var projectReply = _projectCognitionService.BuildCompanionReply(digest);
                AiStatusLabel = provider.Label;
                AddConversation(ConversationRole.Companion, projectReply);
                RefreshDashboard(projectReply);
            }
            catch
            {
                AiStatusLabel = $"{provider.Label} 暂时没接上";
                var digest = _projectCognitionService.CreateFallbackDigest(input, GetOrderedProjects());
                _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                PersistProjects();

                var projectReply = _projectCognitionService.BuildCompanionReply(digest);
                AddConversation(ConversationRole.Companion, projectReply);
                RefreshDashboard(projectReply);
            }
            finally
            {
                IsAwaitingReply = false;
            }

            return;
        }

        if (_intentParser.TryParseTaskIntent(input, out var intent))
        {
            taskFeedback = intent.Action switch
            {
                TaskIntentAction.UpdateExistingTask => ApplyTaskUpdate(intent),
                _ => CreateTask(intent)
            };

            PersistTasks();
            fallbackReply = _personaEngine.ReplyToTaskIntent(
                taskFeedback,
                GetOrderedTasks(),
                SupervisionEnabled,
                _focusSprintEndsAt is not null);
        }
        else
        {
            fallbackReply = _personaEngine.ReplyToConversation(
                input,
                GetOrderedTasks(),
                SupervisionEnabled,
                _focusSprintEndsAt is not null);
        }

        IsAwaitingReply = true;
        AiStatusLabel = $"{provider.Label} 思考中";
        WhisperLine = "团子在想怎么回你...";
        RefreshDashboard("团子在想怎么回你...");

        string reply;
        try
        {
            reply = await provider.GenerateReplyAsync(
                        _conversationHistory,
                        GetOrderedTasks(),
                        GetOrderedProjects(),
                        input,
                        taskFeedback,
                        SupervisionEnabled,
                        _focusSprintEndsAt is not null)
                    ?? fallbackReply;

            AiStatusLabel = provider.Label;
        }
        catch
        {
            AiStatusLabel = $"{provider.Label} 暂时没接上";
            reply = fallbackReply;
        }
        finally
        {
            IsAwaitingReply = false;
        }

        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void ReviewTasks()
    {
        var reviewMessage = _personaEngine.BuildReviewMessage(GetOrderedTasks());
        AddConversation(ConversationRole.Companion, reviewMessage);
        RefreshDashboard(reviewMessage);
    }

    public void ToggleSupervision()
    {
        SupervisionEnabled = !SupervisionEnabled;
        var reply = SupervisionEnabled
            ? "好，那我把监督重新打开。我会按节奏回来找你，也会继续盯着主线。"
            : "行，我先把催促声收一收。但你一叫我，我还是立刻回来。";

        if (SupervisionEnabled)
        {
            _nextReviewAt = DateTimeOffset.Now.Add(_reviewCadence);
        }

        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void StartFocusSprint()
    {
        PromotePrimaryTaskToDoing();
        PersistTasks();

        _focusSprintEndsAt = DateTimeOffset.Now.Add(_focusSprintDuration);
        OnPropertyChanged(nameof(FocusSprintButtonText));

        var reply = _personaEngine.BuildFocusSprintMessage(TopTaskTitle);
        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void ReopenPermissionSetup()
    {
        _permissionProfile.IsConfigured = false;
        _permissionProfile.CanReadSelectedWorkspace = false;
        _permissionProfile.CanRememberWorkspaceSources = false;
        _permissionProfile.CanBuildProjectMemoryFromDocs = false;
        _permissionProfile.PresetLabel = string.Empty;
        PersistPermissionProfile();

        IsPanelOpen = true;
        const string reply = "好，我把权限设置重新打开了。你现在可以重新选团子能做到哪一步。";
        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void ClearWorkspaceAuthorizations()
    {
        _workspaceSources.Clear();
        PersistWorkspaceSources();

        const string reply = "我已经把记住的授权目录清掉了。后面你想让我再读哪个项目，再重新发路径给我就行。";
        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void RestartOnboarding()
    {
        _workspaceSources.Clear();
        PersistWorkspaceSources();
        ReopenPermissionSetup();
    }

    public string GetPermissionSummary()
    {
        if (!_permissionProfile.IsConfigured)
        {
            return "当前还没配置权限。";
        }

        if (!_permissionProfile.CanReadSelectedWorkspace)
        {
            return "当前权限：只聊天，不读本地文件。";
        }

        if (_permissionProfile.CanBuildProjectMemoryFromDocs)
        {
            return $"当前权限：可读指定目录、记住授权路径，并梳理项目线。已记住 {_workspaceSources.Count} 个授权目录。";
        }

        if (_permissionProfile.CanRememberWorkspaceSources)
        {
            return $"当前权限：可读指定目录，并记住授权路径。已记住 {_workspaceSources.Count} 个授权目录。";
        }

        return "当前权限：只读你临时指定的项目目录，不长期记住路径。";
    }

    public string OpenLatestWorkspaceInVsCode()
    {
        var workspacePath = _workspaceSources
            .OrderByDescending(source => source.LastScannedAt)
            .Select(source => source.Path)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return "我这边还没有记住任何项目目录。你先把一个项目路径发给我，或者先授权我记住目录。";
        }

        return _vsCodeBridgeService.TryOpenWorkspace(workspacePath, out var reply)
            ? reply
            : reply;
    }

    private async Task InitializeAiAsync()
    {
        var provider = await ResolveActiveProviderAsync();
        AiStatusLabel = provider.Label;

        if (!_permissionProfile.IsConfigured)
        {
            PanelHint = "第一次先选权限。团子只会在你选过之后，才去读本地项目目录或记住路径。";
            EnsureRecentCompanionPrompt("先告诉我你想给团子开什么权限吧。我会把可选项列出来，你点一个就行。");
            return;
        }

        PanelHint = provider.Kind switch
        {
            AiProviderKind.OpenAi => "现在会优先走 ChatGPT / OpenAI。你直接丢混合清单给她，她也会尝试按项目线归类。",
            AiProviderKind.Ollama => "当前在用本地 Ollama。它也能先认项目线，再决定哪些值得记住。",
            _ => "当前没有可用模型时，团子会退回内置回复和本地项目归类。"
        };
        if (_workspaceSources.Count == 0)
        {
            PanelHint = "第一次可以直接把项目文件夹路径发给团子，比如 C:\\Users\\...\\项目名。她会先读文档，再帮你梳理项目线。";
            EnsureRecentCompanionPrompt("第一次启动时，你可以直接把正在做的项目文件夹路径发给我。我会去读里面的 README、总结和说明文档，再先帮你理出项目线。");
        }
    }

    private async Task<ActiveAiProvider> ResolveActiveProviderAsync()
    {
        var preferredProvider = Environment.GetEnvironmentVariable("DESKTOP_COMPANION_AI_PROVIDER")?.Trim().ToLowerInvariant();
        var openAiAvailability = await _openAiChatService.CheckAvailabilityAsync();
        var ollamaAvailability = await _ollamaChatService.CheckAvailabilityAsync();

        if (preferredProvider == "ollama" && ollamaAvailability.IsAvailable)
        {
            return new ActiveAiProvider(
                AiProviderKind.Ollama,
                ollamaAvailability.StatusLabel,
                _ollamaChatService.GenerateReplyAsync,
                _ollamaChatService.AnalyzeProjectDumpAsync);
        }

        if (preferredProvider == "openai" && openAiAvailability.IsAvailable)
        {
            return new ActiveAiProvider(
                AiProviderKind.OpenAi,
                openAiAvailability.StatusLabel,
                _openAiChatService.GenerateReplyAsync,
                _openAiChatService.AnalyzeProjectDumpAsync);
        }

        if (openAiAvailability.IsAvailable)
        {
            return new ActiveAiProvider(
                AiProviderKind.OpenAi,
                openAiAvailability.StatusLabel,
                _openAiChatService.GenerateReplyAsync,
                _openAiChatService.AnalyzeProjectDumpAsync);
        }

        if (ollamaAvailability.IsAvailable)
        {
            return new ActiveAiProvider(
                AiProviderKind.Ollama,
                ollamaAvailability.StatusLabel,
                _ollamaChatService.GenerateReplyAsync,
                _ollamaChatService.AnalyzeProjectDumpAsync);
        }

        return new ActiveAiProvider(
            AiProviderKind.None,
            "AI 未连接",
            (_, _, _, _, _, _, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<ProjectCognitionDigest?>(null));
    }

    private void HandleSupervisionTick()
    {
        var now = DateTimeOffset.Now;

        if (_focusSprintEndsAt is not null && now >= _focusSprintEndsAt.Value)
        {
            _focusSprintEndsAt = null;
            OnPropertyChanged(nameof(FocusSprintButtonText));

            var sprintReply = _personaEngine.BuildFocusSprintFinishedMessage(GetOrderedTasks());
            AddConversation(ConversationRole.Companion, sprintReply);
            RefreshDashboard(sprintReply);
        }

        if (SupervisionEnabled && now >= _nextReviewAt)
        {
            _nextReviewAt = now.Add(_reviewCadence);
            var reviewReply = _personaEngine.BuildReviewMessage(GetOrderedTasks());
            AddConversation(ConversationRole.Companion, reviewReply);
            RefreshDashboard(reviewReply);
        }

        RefreshRhythmState();
    }

    private string CreateTask(ParsedTaskIntent intent)
    {
        var now = DateTimeOffset.Now;
        _tasks.Insert(0, new CompanionTask
        {
            Title = intent.Title,
            State = intent.State,
            Category = intent.Category,
            Note = intent.Note,
            CreatedAt = now,
            UpdatedAt = now
        });

        return intent.State switch
        {
            CompanionTaskState.Doing => $"好，我把“{intent.Title}”拉到主线上了。",
            CompanionTaskState.Blocked => $"我记下“{intent.Title}”现在卡住了，后面会优先回来处理它。",
            _ => $"“{intent.Title}”我替你记住了。"
        };
    }

    private string ApplyTaskUpdate(ParsedTaskIntent intent)
    {
        var task = FindBestMatch(intent.MatchQuery);
        if (task is null)
        {
            return CreateTask(intent with { Action = TaskIntentAction.CreateTask });
        }

        task.State = intent.State;
        task.Category = string.IsNullOrWhiteSpace(intent.Category) ? task.Category : intent.Category;
        task.Note = intent.Note;
        task.UpdatedAt = DateTimeOffset.Now;

        return intent.State switch
        {
            CompanionTaskState.Done => $"收到，“{task.Title}”我给你记成完成了。",
            CompanionTaskState.Blocked => $"我把“{task.Title}”标成阻塞了，后面会提醒你回来处理。",
            CompanionTaskState.Doing => $"好，“{task.Title}”现在就是主线。",
            _ => $"“{task.Title}”的状态已经更新。"
        };
    }

    private CompanionTask? FindBestMatch(string query)
    {
        var needle = NormalizeForMatch(query);
        if (string.IsNullOrWhiteSpace(needle))
        {
            return GetOrderedTasks().FirstOrDefault();
        }

        return _tasks
            .OrderBy(task => GetMatchScore(task, needle))
            .ThenBy(task => GetStateRank(task.State))
            .ThenByDescending(task => task.UpdatedAt)
            .FirstOrDefault(task => GetMatchScore(task, needle) < int.MaxValue);
    }

    private void PromotePrimaryTaskToDoing()
    {
        if (_tasks.Any(task => task.State == CompanionTaskState.Doing))
        {
            return;
        }

        var candidate = _tasks
            .Where(task => task.State == CompanionTaskState.Todo)
            .OrderByDescending(task => task.UpdatedAt)
            .FirstOrDefault();

        if (candidate is null)
        {
            return;
        }

        candidate.State = CompanionTaskState.Doing;
        candidate.Note = "这条任务正在专注冲刺里。";
        candidate.UpdatedAt = DateTimeOffset.Now;
    }

    private void RefreshDashboard(string? whisperOverride = null)
    {
        var orderedTasks = GetOrderedTasks();

        RebuildConversationMessages();
        RebuildRecentMemories(orderedTasks);
        RebuildSuggestionBubbles(orderedTasks);
        OnPropertyChanged(nameof(HasRecentMemories));
        OnPropertyChanged(nameof(HasSuggestionBubbles));

        var activeTask = orderedTasks.FirstOrDefault(task => task.State != CompanionTaskState.Done);
        var leadProject = GetOrderedProjects().FirstOrDefault();
        TopTaskTitle = activeTask?.Title
            ?? GetLeadProjectFocus(leadProject)
            ?? "先和我说一句，现在最想逃的是哪件事。";

        ApplyMood(orderedTasks);

        PanelSummary = HasRecentMemories
            ? "你先跟团子说，她会回复你，也会把真正要紧的任务和项目线留在下面。"
            : "这里先是聊天，再是记忆，不用先整理成待办。";
        WhisperLine = whisperOverride ?? GetLatestCompanionText();
        RefreshRhythmState();
    }

    private void ApplyMood(IReadOnlyList<CompanionTask> orderedTasks)
    {
        var doingCount = orderedTasks.Count(task => task.State == CompanionTaskState.Doing);
        var blockedCount = orderedTasks.Count(task => task.State == CompanionTaskState.Blocked);
        var todoCount = orderedTasks.Count(task => task.State == CompanionTaskState.Todo);

        if (blockedCount > 0)
        {
            Mood = PetMood.Concerned;
            MoodLabel = "有点担心";
            StatusLine = "我更想先陪你把卡住的那件事弄松一点。";
            AccentBrush = CreateBrush("#FFDA7B5A");
            AccentTintBrush = CreateBrush("#33DA7B5A");
            WhisperBrush = CreateBrush("#CC5E3527");
            return;
        }

        if (doingCount > 0)
        {
            Mood = PetMood.Focused;
            MoodLabel = "盯着主线";
            StatusLine = $"现在我脑子里挂着的是“{TopTaskTitle}”。";
            AccentBrush = CreateBrush("#FF79B5AA");
            AccentTintBrush = CreateBrush("#3379B5AA");
            WhisperBrush = CreateBrush("#CC234E48");
            return;
        }

        if (todoCount > 0)
        {
            Mood = PetMood.Idle;
            MoodLabel = "陪你理清";
            StatusLine = "事情还没开工也没关系，我们先聊清楚。";
            AccentBrush = CreateBrush("#FF83B39C");
            AccentTintBrush = CreateBrush("#3383B39C");
            WhisperBrush = CreateBrush("#CC35554B");
            return;
        }

        if (_projects.Count > 0)
        {
            Mood = PetMood.Idle;
            MoodLabel = "记着项目线";
            StatusLine = $"我还记得你手里挂着“{_projects.OrderByDescending(project => project.UpdatedAt).First().Name}”这条。";
            AccentBrush = CreateBrush("#FF6A8DB7");
            AccentTintBrush = CreateBrush("#336A8DB7");
            WhisperBrush = CreateBrush("#CC30465D");
            return;
        }

        Mood = PetMood.Idle;
        MoodLabel = "在你这边";
        StatusLine = "你可以先随便说一句，我会把重点接住。";
        AccentBrush = CreateBrush("#FF79B5AA");
        AccentTintBrush = CreateBrush("#3379B5AA");
        WhisperBrush = CreateBrush("#CC22363B");
    }

    private void RefreshRhythmState()
    {
        var now = DateTimeOffset.Now;
        RhythmStatusLine = SupervisionEnabled
            ? $"监督中 · 下次梳理 {FormatRemaining(_nextReviewAt - now)}"
            : "监督暂停了，但团子还在听你说话。";

        FocusSprintStatusLine = _focusSprintEndsAt is null
            ? "专注冲刺还没开始。"
            : $"专注冲刺剩余 {FormatRemaining(_focusSprintEndsAt.Value - now)}";
    }

    private IReadOnlyList<CompanionTask> GetOrderedTasks()
    {
        return _tasks
            .OrderBy(task => GetStateRank(task.State))
            .ThenByDescending(task => task.UpdatedAt)
            .ToList();
    }

    private void RebuildConversationMessages()
    {
        ConversationMessages.Clear();

        foreach (var message in _conversationHistory.TakeLast(18))
        {
            var isUser = message.Role == ConversationRole.User;
            ConversationMessages.Add(new ConversationMessageViewModel(
                isUser ? "你" : PetName,
                message.Text,
                isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                isUser ? CreateBrush("#FF22363B") : CreateBrush("#FFF8F3EA"),
                isUser ? Brushes.White : CreateBrush("#FF22363B"),
                isUser ? CreateBrush("#FFD7E0E3") : CreateBrush("#FF79B5AA")));
        }
    }

    private void RebuildRecentMemories(IEnumerable<CompanionTask> orderedTasks)
    {
        RecentMemoryCards.Clear();

        foreach (var task in orderedTasks.Take(2))
        {
            var (accentBrush, accentTintBrush) = GetBrushPair(task.State);
            RecentMemoryCards.Add(new RecentMemoryViewModel(
                task.Title,
                task.Note,
                GetStateLabel(task.State),
                accentBrush,
                accentTintBrush));
        }

        foreach (var project in GetOrderedProjects().Take(3 - RecentMemoryCards.Count))
        {
            var note = !string.IsNullOrWhiteSpace(project.NextAction)
                ? $"下一步：{project.NextAction}"
                : !string.IsNullOrWhiteSpace(project.Summary)
                    ? project.Summary
                : project.RecentItems.FirstOrDefault() ?? "这条项目线已经被团子记住了。";

            RecentMemoryCards.Add(new RecentMemoryViewModel(
                project.Name,
                note,
                string.IsNullOrWhiteSpace(project.PriorityLabel)
                    ? project.KindLabel
                    : $"{project.KindLabel} · {project.PriorityLabel}",
                CreateBrush("#FF6A8DB7"),
                CreateBrush("#336A8DB7")));
        }
    }

    private void RebuildSuggestionBubbles(IReadOnlyList<CompanionTask> orderedTasks)
    {
        SuggestionBubbles.Clear();

        var suggestionTexts = _projects.Count > 0
            && !orderedTasks.Any(task => task.State == CompanionTaskState.Blocked)
            && !orderedTasks.Any(task => task.State == CompanionTaskState.Doing)
                ? BuildProjectSuggestionTexts()
                : BuildSuggestionTexts(orderedTasks);

        foreach (var text in suggestionTexts)
        {
            SuggestionBubbles.Add(new SuggestionBubbleViewModel(
                text,
                CreateBrush("#FFF8F3EA"),
                CreateBrush("#FF79B5AA"),
                CreateBrush("#FF22363B")));
        }
    }

    private IReadOnlyList<string> BuildSuggestionTexts(IReadOnlyList<CompanionTask> orderedTasks)
    {
        if (!_permissionProfile.IsConfigured)
        {
            return
            [
                "只聊天，不读本地文件",
                "可读我指定的项目目录",
                "可读目录并记住授权路径",
                "可读目录、记住路径并梳理项目线"
            ];
        }

        if (_workspaceSources.Count == 0)
        {
            return
            [
                "告诉团子项目目录",
                "先读一下我的文档",
                "我的项目在 C:\\..."
            ];
        }

        if (orderedTasks.Any(task => task.State == CompanionTaskState.Blocked))
        {
            return
            [
                "先陪我拆下一步",
                "我卡在这里",
                "这个先延后一下"
            ];
        }

        if (orderedTasks.Any(task => task.State == CompanionTaskState.Doing))
        {
            return
            [
                "继续帮我盯这件事",
                "把它收成下一步",
                "我想开个 25 分钟冲刺"
            ];
        }

        if (_projects.Count > 0)
        {
            return
            [
                "你记得我最近在做什么",
                "帮我理一下主线",
                "我现在有点乱"
            ];
        }

        return
        [
            "我现在有点乱",
            "帮我盯住一个任务",
            "把这一串事理一理"
        ];
    }

    private IReadOnlyList<string> BuildProjectSuggestionTexts()
    {
        var leadProject = GetOrderedProjects().FirstOrDefault();
        var focusText = TrimForBubble(leadProject?.NextAction ?? leadProject?.Name, 14);

        if (_workspaceSources.Count > 0)
        {
            return
            [
                "在 VS Code 打开最近项目",
                "交给 Codex 去做",
                string.IsNullOrWhiteSpace(focusText) ? "帮我只看今天先动什么" : $"先陪我推进{focusText}"
            ];
        }

        return
        [
            string.IsNullOrWhiteSpace(focusText) ? "帮我只看今天先动什么" : $"先陪我推进{focusText}",
            "帮我只看今天先动什么",
            "把剩下的先往后放一放"
        ];
    }

    private void LoadTasks()
    {
        var storedTasks = _taskStore.LoadTasks();
        var loadedTasks = storedTasks
            .Where(task => !SampleTaskTitles.Contains(task.Title))
            .ToList();

        _tasks.AddRange(loadedTasks);

        if (_tasks.Count != storedTasks.Count)
        {
            PersistTasks();
        }
    }

    private void LoadProjects()
    {
        var storedProjects = _projectStore.LoadProjects();
        _projects.AddRange(storedProjects);
    }

    private void LoadWorkspaceSources()
    {
        var storedSources = _workspaceSourceStore.LoadSources();
        _workspaceSources.AddRange(storedSources);
    }

    private void LoadConversation()
    {
        var storedMessages = _conversationStore.LoadMessages();
        var loadedMessages = storedMessages
            .Where(message => !SampleTaskTitles.Any(title => message.Text.Contains(title, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (loadedMessages.Count == 0)
        {
            AddConversation(ConversationRole.Companion, _personaEngine.BuildWelcomeMessage(), persist: false);
            PersistConversation();
            return;
        }

        _conversationHistory.AddRange(loadedMessages);

        if (_conversationHistory.Count != storedMessages.Count)
        {
            PersistConversation();
        }
    }

    private void AddConversation(ConversationRole role, string text, bool persist = true)
    {
        _conversationHistory.Add(new ConversationMessage
        {
            Role = role,
            Text = text,
            CreatedAt = DateTimeOffset.Now
        });

        while (_conversationHistory.Count > 40)
        {
            _conversationHistory.RemoveAt(0);
        }

        if (persist)
        {
            PersistConversation();
        }
    }

    private void EnsureRecentCompanionPrompt(string text)
    {
        var latestCompanion = _conversationHistory.LastOrDefault(message => message.Role == ConversationRole.Companion);
        if (latestCompanion?.Text == text)
        {
            return;
        }

        AddConversation(ConversationRole.Companion, text);
        RefreshDashboard(text);
    }

    private string GetLatestCompanionText()
    {
        return _conversationHistory
            .LastOrDefault(message => message.Role == ConversationRole.Companion)?.Text
            ?? _personaEngine.BuildWelcomeMessage();
    }

    private void PersistTasks()
    {
        _taskStore.SaveTasks(_tasks.OrderByDescending(task => task.UpdatedAt));
    }

    private void PersistProjects()
    {
        _projectStore.SaveProjects(_projects.OrderByDescending(project => project.UpdatedAt));
    }

    private void PersistWorkspaceSources()
    {
        _workspaceSourceStore.SaveSources(_workspaceSources.OrderByDescending(source => source.LastScannedAt));
    }

    private void PersistPermissionProfile()
    {
        _permissionProfile.UpdatedAt = DateTimeOffset.Now;
        _permissionStore.SaveProfile(_permissionProfile);
    }

    private void PersistConversation()
    {
        _conversationStore.SaveMessages(_conversationHistory);
    }

    private IReadOnlyList<ProjectMemory> GetOrderedProjects()
    {
        return _projects
            .OrderBy(project => GetProjectPriorityRank(project.PriorityLabel))
            .ThenByDescending(project => project.UpdatedAt)
            .ThenBy(project => project.Name)
            .ToList();
    }

    private bool TryApplyPermissionPreset(string input, out string reply)
    {
        reply = string.Empty;

        if (input.Contains("只聊天", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = false;
            _permissionProfile.CanRememberWorkspaceSources = false;
            _permissionProfile.CanBuildProjectMemoryFromDocs = false;
            _permissionProfile.PresetLabel = "只聊天";
            PersistPermissionProfile();
            reply = "好，我先只保留聊天权限，不读你电脑里的文档。以后你想开放目录读取，再直接跟我说。";
            return true;
        }

        if (input.Contains("可读我指定的项目目录", StringComparison.OrdinalIgnoreCase)
            || input.Contains("读我指定的项目目录", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = false;
            _permissionProfile.CanBuildProjectMemoryFromDocs = false;
            _permissionProfile.PresetLabel = "只读指定目录";
            PersistPermissionProfile();
            reply = "好，我可以读你临时指定的项目目录，但不会记住路径，也不会默认把项目线长期存下来。现在你可以把文件夹路径发给我。";
            return true;
        }

        if (input.Contains("可读目录并记住授权路径", StringComparison.OrdinalIgnoreCase)
            || input.Contains("读目录并记住", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = true;
            _permissionProfile.CanBuildProjectMemoryFromDocs = false;
            _permissionProfile.PresetLabel = "读目录并记住路径";
            PersistPermissionProfile();
            reply = "好，我可以读你指定的目录，也可以记住你授权过的路径，但我暂时不把文档内容自动沉淀成长期项目记忆。现在把项目目录发给我就行。";
            return true;
        }

        if (input.Contains("可读目录、记住路径并梳理项目线", StringComparison.OrdinalIgnoreCase)
            || input.Contains("梳理项目线", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = true;
            _permissionProfile.CanBuildProjectMemoryFromDocs = true;
            _permissionProfile.PresetLabel = "读目录并梳理项目线";
            PersistPermissionProfile();
            reply = "好，这套权限下我可以读你指定的目录，记住你授权过的路径，并把文档内容整理成项目线和下一步。现在把项目文件夹路径发给我吧。";
            return true;
        }

        return false;
    }

    private string BuildWorkspaceImportReply(
        WorkspaceIngestionService.WorkspaceScanResult scan,
        ProjectCognitionDigest digest)
    {
        var prefix = $"我先读了 {scan.RootLabel} 这一路里的 {scan.Documents.Count} 份资料。";

        if (_permissionProfile.CanBuildProjectMemoryFromDocs)
        {
            return prefix + _projectCognitionService.BuildCompanionReply(digest);
        }

        var projectNames = digest.Projects.Count == 0
            ? "这轮还没稳稳拆出项目线"
            : string.Join("、", digest.Projects.Take(4).Select(project => project.Name));

        var focus = string.IsNullOrWhiteSpace(digest.SuggestedFocus)
            ? digest.NowItems.FirstOrDefault() ?? digest.NextItems.FirstOrDefault() ?? "先挑一条主线"
            : digest.SuggestedFocus;

        return $"{prefix} 先看起来更像这些项目线：{projectNames}。这轮我先不给你长期记住，只做临时梳理；现在更值得先动的是“{focus}”。";
    }

    private static string BuildCodexSuccessReplySafe(
        VsCodeBridgeService.CodexDispatchResult result,
        string workspacePath)
    {
        var workspaceLabel = Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var payload = result.Payload;

        if (payload is null)
        {
            return $"我已经把这件事交给 Codex 处理完了，项目是 {workspaceLabel}。\n{result.Message}";
        }

        var summary = string.IsNullOrWhiteSpace(payload.Summary) ? result.Message : payload.Summary;
        var changedFiles = payload.ChangedFiles ?? Array.Empty<string>();
        var testsRun = payload.TestsRun ?? Array.Empty<string>();
        var notes = payload.Notes ?? Array.Empty<string>();

        var lines = new List<string>
        {
            $"Codex 已经在 {workspaceLabel} 里跑完这一轮了。{summary}"
        };

        if (changedFiles.Count > 0)
        {
            lines.Add($"改动：{string.Join("、", changedFiles.Take(4))}");
        }

        if (testsRun.Count > 0)
        {
            lines.Add($"验证：{string.Join("；", testsRun.Take(3))}");
        }

        if (notes.Count > 0)
        {
            lines.Add($"备注：{string.Join("；", notes.Take(2))}");
        }

        if (!string.IsNullOrWhiteSpace(payload.NextStep))
        {
            lines.Add($"下一步：{payload.NextStep}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildCodexFailureReplySafe(VsCodeBridgeService.CodexDispatchResult result)
    {
        return $"这次我试着把任务交给 Codex，但没有顺利跑完。\n{result.Message}";
    }

    private static string BuildCodexTimeoutReplySafe(VsCodeBridgeService.CodexDispatchResult result)
    {
        return $"Codex timed out, so I stopped that run first.\n{result.Message}";
    }

    private static string BuildCodexSuccessReply(
        VsCodeBridgeService.CodexDispatchResult result,
        string workspacePath)
    {
        var workspaceLabel = Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var payload = result.Payload;

        if (payload is null)
        {
            return $"我已经把这件事交给 Codex 处理完了，项目是 {workspaceLabel}。\n{result.Message}";
        }

        var lines = new List<string>
        {
            $"Codex 已经在 {workspaceLabel} 里跑完这一轮了。{payload.Summary}"
        };

        if (payload.ChangedFiles.Count > 0)
        {
            lines.Add($"改动：{string.Join("、", payload.ChangedFiles.Take(4))}");
        }

        if (payload.TestsRun.Count > 0)
        {
            lines.Add($"验证：{string.Join("；", payload.TestsRun.Take(3))}");
        }

        if (payload.Notes.Count > 0)
        {
            lines.Add($"备注：{string.Join("；", payload.Notes.Take(2))}");
        }

        if (!string.IsNullOrWhiteSpace(payload.NextStep))
        {
            lines.Add($"下一步：{payload.NextStep}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildCodexFailureReply(VsCodeBridgeService.CodexDispatchResult result)
    {
        return $"这次我试着把任务交给 Codex，但没有顺利跑完。\n{result.Message}";
    }

    private bool LooksLikeVsCodeOpenRequest(string input)
    {
        var normalized = input.ToLowerInvariant();
        var mentionsVsCode = normalized.Contains("vscode")
                             || normalized.Contains("vs code")
                             || normalized.Contains("visual studio code");

        if (!mentionsVsCode)
        {
            return false;
        }

        return input.Contains("打开", StringComparison.OrdinalIgnoreCase)
               || input.Contains("open", StringComparison.OrdinalIgnoreCase);
    }

    private bool LooksLikeCodexDispatchRequest(string input)
    {
        var normalized = input.ToLowerInvariant();
        if (!normalized.Contains("codex"))
        {
            return false;
        }

        return input.Contains("交给", StringComparison.OrdinalIgnoreCase)
               || input.Contains("去做", StringComparison.OrdinalIgnoreCase)
               || input.Contains("处理", StringComparison.OrdinalIgnoreCase)
               || input.Contains("执行", StringComparison.OrdinalIgnoreCase)
               || input.Contains("帮我", StringComparison.OrdinalIgnoreCase)
               || input.Contains("改", StringComparison.OrdinalIgnoreCase)
               || input.Contains("修", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveWorkspaceForExternalTools(string input, out string workspacePath)
    {
        if (_workspaceIngestionService.TryExtractWorkspacePath(input, out workspacePath))
        {
            return true;
        }

        workspacePath = _workspaceSources
            .OrderByDescending(source => source.LastScannedAt)
            .Select(source => source.Path)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            ?? string.Empty;

        return !string.IsNullOrWhiteSpace(workspacePath);
    }

    private static string ExtractCodexTaskPrompt(string input)
    {
        var prompt = input
            .Replace("在 VS Code 里", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("在VS Code里", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("在 vscode 里", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("在vscode里", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("用 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("用 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("交给 Codex 去做", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("交给 codex 去做", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("交给 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("交给 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("让 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("让 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("帮我", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("处理", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("执行", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("：", " ", StringComparison.OrdinalIgnoreCase)
            .Replace(":", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return prompt;
    }

    private void UpsertWorkspaceSource(WorkspaceIngestionService.WorkspaceScanResult scan)
    {
        var existing = _workspaceSources.FirstOrDefault(source =>
            string.Equals(source.Path, scan.RootPath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _workspaceSources.Add(new WorkspaceSourceMemory
            {
                Path = scan.RootPath,
                Label = scan.RootLabel,
                ImportedDocumentCount = scan.Documents.Count,
                LastSummary = string.Join("；", scan.Documents.Take(3).Select(document => document.FileName)),
                ImportedAt = DateTimeOffset.Now,
                LastScannedAt = DateTimeOffset.Now
            });

            return;
        }

        existing.Label = scan.RootLabel;
        existing.ImportedDocumentCount = scan.Documents.Count;
        existing.LastSummary = string.Join("；", scan.Documents.Take(3).Select(document => document.FileName));
        existing.LastScannedAt = DateTimeOffset.Now;
    }

    private static (Brush AccentBrush, Brush AccentTintBrush) GetBrushPair(CompanionTaskState state)
    {
        return state switch
        {
            CompanionTaskState.Doing => (CreateBrush("#FF79B5AA"), CreateBrush("#3379B5AA")),
            CompanionTaskState.Blocked => (CreateBrush("#FFDA7B5A"), CreateBrush("#33DA7B5A")),
            CompanionTaskState.Done => (CreateBrush("#FFE2A34C"), CreateBrush("#33E2A34C")),
            _ => (CreateBrush("#FF83B39C"), CreateBrush("#3383B39C"))
        };
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

    private static int GetStateRank(CompanionTaskState state)
    {
        return state switch
        {
            CompanionTaskState.Doing => 0,
            CompanionTaskState.Blocked => 1,
            CompanionTaskState.Todo => 2,
            CompanionTaskState.Done => 3,
            _ => 4
        };
    }

    private static int GetMatchScore(CompanionTask task, string needle)
    {
        var haystack = NormalizeForMatch(task.Title);
        if (haystack == needle)
        {
            return 0;
        }

        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return haystack.Length - needle.Length + 1;
        }

        if (needle.Contains(haystack, StringComparison.OrdinalIgnoreCase))
        {
            return 10 + needle.Length - haystack.Length;
        }

        return int.MaxValue;
    }

    private static string NormalizeForMatch(string text)
    {
        return new string(text
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray())
            .ToLowerInvariant();
    }

    private static string? GetLeadProjectFocus(ProjectMemory? project)
    {
        if (project is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(project.NextAction)
            ? project.NextAction
            : project.Name;
    }

    private static int GetProjectPriorityRank(string? priorityLabel)
    {
        return priorityLabel switch
        {
            "先动" => 0,
            "下一顺位" => 1,
            "先挂着" => 2,
            _ => 3
        };
    }

    private static string TrimForBubble(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Trim();
        return cleaned.Length <= maxLength ? cleaned : $"{cleaned[..maxLength].Trim()}…";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "00:00";
        }

        return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed record ConversationMessageViewModel(
        string SpeakerLabel,
        string Text,
        HorizontalAlignment BubbleAlignment,
        Brush BubbleBrush,
        Brush TextBrush,
        Brush SpeakerBrush);

    public sealed record RecentMemoryViewModel(
        string Title,
        string Note,
        string StateLabel,
        Brush AccentBrush,
        Brush AccentTintBrush);

    public sealed record SuggestionBubbleViewModel(
        string Text,
        Brush FillBrush,
        Brush BorderBrush,
        Brush TextBrush);

    private delegate Task<string?> AiReplyHandler(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<CompanionTask> orderedTasks,
        IReadOnlyList<ProjectMemory> knownProjects,
        string userInput,
        string? taskFeedback,
        bool supervisionEnabled,
        bool focusSprintActive,
        CancellationToken cancellationToken = default);

    private delegate Task<ProjectCognitionDigest?> ProjectCognitionHandler(
        string userInput,
        IReadOnlyList<ProjectMemory> knownProjects,
        CancellationToken cancellationToken = default);

    private enum AiProviderKind
    {
        None,
        OpenAi,
        Ollama
    }

    private sealed record ActiveAiProvider(
        AiProviderKind Kind,
        string Label,
        AiReplyHandler GenerateReplyAsync,
        ProjectCognitionHandler AnalyzeProjectDumpAsync);
}
