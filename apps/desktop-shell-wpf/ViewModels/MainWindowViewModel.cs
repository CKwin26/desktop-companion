using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopCompanion.WpfHost.Cognition;
using DesktopCompanion.WpfHost.Models;
using DesktopCompanion.WpfHost.Services;

namespace DesktopCompanion.WpfHost.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan ProviderProbeCacheDuration = TimeSpan.FromSeconds(30);
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
    private readonly LocalCompanionKernelStore _companionKernelStore;
    private readonly LocalUserProfileStore _userProfileStore;
    private readonly LocalConversationStore _conversationStore;
    private readonly SimpleTaskIntentParser _intentParser;
    private readonly CompanionPersonaEngine _personaEngine;
    private readonly ProjectCognitionService _projectCognitionService;
    private readonly WorkspaceIngestionService _workspaceIngestionService;
    private readonly RepoStructureReaderService _repoStructureReaderService;
    private readonly ProjectArchetypeResolverService _projectArchetypeResolverService;
    private readonly InternalProjectAssessmentService _internalProjectAssessmentService;
    private readonly ExternalSignalCollectorService _externalSignalCollectorService;
    private readonly ProjectStateScorer _projectStateScorer;
    private readonly PersonalDistillationService _personalDistillationService;
    private readonly LocalCodexThreadIndexService _localCodexThreadIndexService;
    private readonly VsCodeBridgeService _vsCodeBridgeService;
    private readonly OpenAiChatService _openAiChatService;
    private readonly OllamaChatService _ollamaChatService;
    private readonly DispatcherTimer _supervisionTimer;
    private readonly TimeSpan _reviewCadence = TimeSpan.FromMinutes(25);
    private readonly TimeSpan _focusSprintDuration = TimeSpan.FromMinutes(25);
    private readonly CompanionPermissionProfile _permissionProfile;
    private DistilledUserProfile? _distilledUserProfile;
    private ActiveAiProvider? _cachedProvider;
    private DateTimeOffset _providerCacheValidUntil = DateTimeOffset.MinValue;
    private string? _cachedProviderPreference;

    private DateTimeOffset _nextReviewAt;
    private DateTimeOffset? _focusSprintEndsAt;
    private DateTimeOffset _lastCodexThreadSyncAt = DateTimeOffset.MinValue;
    private WorkspaceIngestionService.DirectorySurfaceResult? _lastDirectorySurface;
    private string? _pendingDirectoryPath;
    private string? _pendingDirectoryLabel;
    private string? _pendingWorkspacePath;
    private string? _pendingWorkspaceLabel;
    private string? _activeWorkspacePath;
    private string? _activeWorkspaceLabel;
    private string? _activeWorkspaceKindLabel;
    private DateTimeOffset? _activeWorkspaceTouchedAt;

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
    private bool _supervisionEnabled = false;
    private bool _isAwaitingReply;
    private string _panelSummary = "现在这里只有一块主聊天面板。你先说话，团子再替你记事。";
    private string _panelHint = "可以直接说心情，也可以直接交待任务。";
    private string _chatInput = string.Empty;
    private readonly TimeSpan _codexThreadSyncCadence = TimeSpan.FromSeconds(20);
    private string _rhythmStatusLine = "自动提醒已关闭。";
    private string _focusSprintStatusLine = "专注冲刺还没开始。";

    public MainWindowViewModel()
        : this(
            new LocalTaskStore(),
            new LocalProjectStore(),
            new LocalWorkspaceSourceStore(),
            new LocalPermissionStore(),
            new LocalCompanionKernelStore(),
            new LocalUserProfileStore(),
            new LocalConversationStore(),
            new SimpleTaskIntentParser(),
            new CompanionPersonaEngine(),
            new ProjectCognitionService(),
            new WorkspaceIngestionService(),
            new RepoStructureReaderService(),
            new ProjectArchetypeResolverService(),
            new InternalProjectAssessmentService(),
            new ExternalSignalCollectorService(),
            new ProjectStateScorer(),
            new PersonalDistillationService(),
            new LocalCodexThreadIndexService(),
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
        LocalCompanionKernelStore companionKernelStore,
        LocalUserProfileStore userProfileStore,
        LocalConversationStore conversationStore,
        SimpleTaskIntentParser intentParser,
        CompanionPersonaEngine personaEngine,
        ProjectCognitionService projectCognitionService,
        WorkspaceIngestionService workspaceIngestionService,
        RepoStructureReaderService repoStructureReaderService,
        ProjectArchetypeResolverService projectArchetypeResolverService,
        InternalProjectAssessmentService internalProjectAssessmentService,
        ExternalSignalCollectorService externalSignalCollectorService,
        ProjectStateScorer projectStateScorer,
        PersonalDistillationService personalDistillationService,
        LocalCodexThreadIndexService localCodexThreadIndexService,
        VsCodeBridgeService vsCodeBridgeService,
        OpenAiChatService openAiChatService,
        OllamaChatService ollamaChatService)
    {
        _taskStore = taskStore;
        _projectStore = projectStore;
        _workspaceSourceStore = workspaceSourceStore;
        _permissionStore = permissionStore;
        _companionKernelStore = companionKernelStore;
        _userProfileStore = userProfileStore;
        _conversationStore = conversationStore;
        _intentParser = intentParser;
        _personaEngine = personaEngine;
        _projectCognitionService = projectCognitionService;
        _workspaceIngestionService = workspaceIngestionService;
        _repoStructureReaderService = repoStructureReaderService;
        _projectArchetypeResolverService = projectArchetypeResolverService;
        _internalProjectAssessmentService = internalProjectAssessmentService;
        _externalSignalCollectorService = externalSignalCollectorService;
        _projectStateScorer = projectStateScorer;
        _personalDistillationService = personalDistillationService;
        _localCodexThreadIndexService = localCodexThreadIndexService;
        _vsCodeBridgeService = vsCodeBridgeService;
        _openAiChatService = openAiChatService;
        _ollamaChatService = ollamaChatService;
        _permissionProfile = _permissionStore.LoadProfile();
        CompanionKernelRuntime.SetCurrent(_companionKernelStore.LoadSelection().KernelId);
        _distilledUserProfile = _userProfileStore.LoadProfile();

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

    public string SupervisionToggleText => "监督已关闭";

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

        ActiveAiProvider? provider = null;
        async Task<ActiveAiProvider> GetProviderAsync()
        {
            provider ??= await ResolveActiveProviderAsync();
            return provider;
        }

        string? taskFeedback = null;
        string fallbackReply;

        if (TryApplyPermissionPreset(input, out var permissionReply))
        {
            AddConversation(ConversationRole.Companion, permissionReply);
            RefreshDashboard(permissionReply);
            return;
        }

        if (_personalDistillationService.LooksLikePersonalDistillationRequest(input))
        {
            if (!_permissionProfile.CanBuildPersonalProfileFromPrivateSources)
            {
                const string noPermissionReply = "这一步我先不碰你的私人资料。你要先明确给我“可读私人资料并沉淀安全画像”这档权限，我才会只提炼安全画像，不保存原始聊天内容。";
                AddConversation(ConversationRole.Companion, noPermissionReply);
                RefreshDashboard(noPermissionReply);
                return;
            }

            var sourcePaths = _personalDistillationService.ExtractSourcePaths(input);
            if (sourcePaths.Count == 0)
            {
                const string missingSourceReply = "你要蒸馏自己时，把资料路径也一起发给我，比如 G:\\wechat 或桌面上的某个目录。";
                AddConversation(ConversationRole.Companion, missingSourceReply);
                RefreshDashboard(missingSourceReply);
                return;
            }

            IsAwaitingReply = true;
            AiStatusLabel = "AI 蒸馏中";
            WhisperLine = "团子在从你授权的私人资料里提炼隐私安全画像，只会保留长期风格和主线结构。";
            RefreshDashboard("团子在从你授权的私人资料里提炼隐私安全画像，只会保留长期风格和主线结构。");

            try
            {
                provider = await GetProviderAsync();
                var scan = await Task.Run(() => _personalDistillationService.ScanSources(sourcePaths));
                if (!scan.IsSuccess)
                {
                    AiStatusLabel = provider.Label;
                    AddConversation(ConversationRole.Companion, scan.Message);
                    RefreshDashboard(scan.Message);
                    return;
                }

                var profile = await provider.AnalyzePersonalProfileAsync(scan.AnalysisInput)
                              ?? _personalDistillationService.CreateFallbackProfile(scan);

                _distilledUserProfile = profile;
                _userProfileStore.SaveProfile(profile);

                var profileReply = _personalDistillationService.BuildCompanionReply(profile, scan);
                AiStatusLabel = provider.Label;
                AddConversation(ConversationRole.Companion, profileReply);
                RefreshDashboard(profileReply);
            }
            catch
            {
                const string failureReply = "这轮私人资料蒸馏没稳稳跑完。路径我看到了，但我宁可先停住，也不乱存你的原始信息。你可以先给我文本导出、总结文档，或者缩小到一两个目录重试。";
                InvalidateProviderCache();
                AiStatusLabel = $"{provider?.Label ?? "AI"} 暂时没接住";
                AddConversation(ConversationRole.Companion, failureReply);
                RefreshDashboard(failureReply);
            }
            finally
            {
                IsAwaitingReply = false;
            }

            return;
        }

        if (LooksLikeVsCodeOpenRequest(input))
        {
            if (!TryResolveWorkspaceForCodex(input, out var toolWorkspacePath))
            {
                const string missingWorkspaceReply = "我这轮还没从当前对话、活跃工作区或最近 Codex 项目里推到明确目录。你如果是要切到别的地方，再把路径发我，我就直接替你在 VS Code 里打开。";
                AddConversation(ConversationRole.Companion, missingWorkspaceReply);
                RefreshDashboard(missingWorkspaceReply);
                return;
            }

            var openSucceeded = _vsCodeBridgeService.TryOpenWorkspace(toolWorkspacePath, out var openReply);
            if (openSucceeded)
            {
                RememberActiveWorkspace(
                    toolWorkspacePath,
                    Path.GetFileName(toolWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
            AddConversation(ConversationRole.Companion, openReply);
            RefreshDashboard(openReply);

            if (openSucceeded && !_permissionProfile.CanReadSelectedWorkspace && _workspaceSources.Count == 0)
            {
                PanelHint = "如果你愿意，后面也可以给团子开放项目目录读取权限。";
            }

            return;
        }

        if (LooksLikeCodexWorkspaceOverviewRequest(input))
        {
            SyncProjectsFromCodexThreadIndex(force: true);
            var overview = _localCodexThreadIndexService.BuildOverview();
            var overviewReply = BuildCodexWorkspaceOverviewReply(overview, GetOrderedProjects());
            AddConversation(ConversationRole.Companion, overviewReply);
            RefreshDashboard(overviewReply);
            return;
        }

        if (LooksLikeCodexDispatchIntent(input))
        {
            if (!TryResolveWorkspaceForExternalTools(input, out var toolWorkspacePath))
            {
                const string missingWorkspaceReply = "我这轮还没从当前对话、活跃工作区或最近 Codex 项目里推到明确目录。你如果是要别的项目，再把路径发我；不然我就默认继续按你现在最活跃的项目来交给 Codex。";
                AddConversation(ConversationRole.Companion, missingWorkspaceReply);
                RefreshDashboard(missingWorkspaceReply);
                return;
            }

            var isCodexInspection = LooksLikeCodexStatusInspectionIntent(input);
            var codexPrompt = BuildCodexTaskPrompt(input);
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
                RememberActiveWorkspace(
                    toolWorkspacePath,
                    Path.GetFileName(toolWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                using var timeoutCts = new CancellationTokenSource(
                    isCodexInspection ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3));
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

        if (TryResolveContextualWorkspaceUnderstandingRequest(input, out var contextualWorkspacePath))
        {
            await HandleWorkspaceUnderstandingAsync(contextualWorkspacePath);
            return;
        }

        if (TryResolveContextualDirectorySurfaceRequest(input, out var contextualDirectoryPath))
        {
            await HandleDirectorySurfaceAsync(input, contextualDirectoryPath);
            return;
        }

        if (LooksLikeDirectoryRecommendationRequest(input)
            && TryBuildDirectoryRecommendationReply(out var recommendationReply))
        {
            AddConversation(ConversationRole.Companion, recommendationReply);
            RefreshDashboard(recommendationReply);
            return;
        }

        if (LooksLikeDirectorySurfaceRequest(input))
        {
            if (!_workspaceIngestionService.TryExtractWorkspacePath(input, out var directoryPath))
            {
                const string missingDirectoryReply = "你要我看本地目录时，直接把具体路径发我也行；如果就是桌面，直接说“看看桌面有什么”，如果是当前项目，也可以直接说“看看现在这个项目”。";
                AddConversation(ConversationRole.Companion, missingDirectoryReply);
                RefreshDashboard(missingDirectoryReply);
                return;
            }

            await HandleDirectorySurfaceAsync(input, directoryPath);
            return;
        }

        if (_workspaceIngestionService.TryExtractWorkspacePath(input, out var workspacePath))
        {
            await HandleWorkspaceUnderstandingAsync(workspacePath);
            return;
        }

        if (_projectCognitionService.LooksLikeProjectDump(input))
        {
            IsAwaitingReply = true;
            AiStatusLabel = "AI 归类中";
            WhisperLine = "团子在替你辨认这批事分别挂在哪条项目线上...";
            RefreshDashboard("团子在替你辨认这批事分别挂在哪条项目线上...");

            try
            {
                provider = await GetProviderAsync();
                var digest = await provider.AnalyzeProjectDumpAsync(
                                 input,
                                 GetOrderedProjects())
                             ?? _projectCognitionService.CreateFallbackDigest(input, GetOrderedProjects());

                _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                await RecomputeProjectStatesAsync(allowExternalSearch: true);
                PersistProjects();

                var projectReply = _projectCognitionService.BuildCompanionReply(digest);
                AiStatusLabel = provider.Label;
                AddConversation(ConversationRole.Companion, projectReply);
                RefreshDashboard(projectReply);
            }
            catch
            {
                InvalidateProviderCache();
                AiStatusLabel = $"{provider?.Label ?? "AI"} 暂时没接上";
                var digest = _projectCognitionService.CreateFallbackDigest(input, GetOrderedProjects());
                _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                await RecomputeProjectStatesAsync(allowExternalSearch: true);
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
        else if (await TryHandleStructuredProjectScopedFallbackAsync(input))
        {
            return;
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
        AiStatusLabel = "AI 连接中";
        WhisperLine = "团子在想怎么回你...";
        RefreshDashboard("团子在想怎么回你...");

        string reply;
        try
        {
            provider = await GetProviderAsync();
            AiStatusLabel = $"{provider.Label} 思考中";
            reply = await provider.GenerateReplyAsync(
                        _conversationHistory,
                        GetOrderedTasks(),
                        GetOrderedProjects(),
                        input,
                        taskFeedback,
                        BuildActiveWorkspaceContextLine(),
                        SupervisionEnabled,
                        _focusSprintEndsAt is not null)
                    ?? fallbackReply;

            AiStatusLabel = provider.Label;
        }
        catch
        {
            InvalidateProviderCache();
            AiStatusLabel = $"{provider?.Label ?? "AI"} 暂时没接上";
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
        const string reply = "自动监督已经关掉了，我不会再按节奏回来催你。";
        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public void ToggleSupervision()
    {
        SupervisionEnabled = false;
        const string reply = "自动监督已经彻底关掉了。后面只有你主动叫我，我才会接着说。";
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
        _permissionProfile.CanBuildPersonalProfileFromPrivateSources = false;
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

        if (_permissionProfile.CanBuildPersonalProfileFromPrivateSources)
        {
            return $"当前权限：可读指定目录、记住路径、梳理项目线，并蒸馏隐私安全用户画像。已记住 {_workspaceSources.Count} 个授权目录。";
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

    public string GetCurrentKernelId()
    {
        return CompanionKernelRuntime.Current.Id;
    }

    public string GetKernelSummary()
    {
        var kernel = CompanionKernelRuntime.Current;
        return $"当前人格内核：{kernel.Label}。{kernel.Summary}";
    }

    public void SwitchKernel(string kernelId)
    {
        var nextKernel = CompanionKernelCatalog.Resolve(kernelId);
        if (nextKernel.Id == CompanionKernelRuntime.Current.Id)
        {
            var sameReply = $"现在已经是“{nextKernel.Label}”了。{nextKernel.Summary}";
            AddConversation(ConversationRole.Companion, sameReply);
            RefreshDashboard(sameReply);
            return;
        }

        CompanionKernelRuntime.SetCurrent(nextKernel.Id);
        _companionKernelStore.SaveSelection(new CompanionKernelSelection
        {
            KernelId = nextKernel.Id,
            UpdatedAt = DateTimeOffset.Now
        });
        InvalidateProviderCache();

        var reply = $"好，内核切到“{nextKernel.Label}”。{nextKernel.Summary}";
        AddConversation(ConversationRole.Companion, reply);
        RefreshDashboard(reply);
    }

    public string OpenLatestWorkspaceInVsCode()
    {
        if (!TryResolveWorkspaceFromKnownContext(
                out var workspacePath,
                forceFreshCodexSync: true,
                allowDriveRootWorkspace: true,
                allowAppRepositoryRoot: false))
        {
            return "我这边还没从当前对话、活跃工作区或最近 Codex 项目里推到明确目录。你如果要切到别的地方，再把路径发我。";
        }

        return _vsCodeBridgeService.TryOpenWorkspace(workspacePath, out var reply)
            ? reply
            : reply;
    }

    public string GetCodexBackendSummary()
    {
        return _vsCodeBridgeService.DescribeCodexBackends();
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
            PanelHint = "第一次可以直接说“看看现在这个项目”；如果你要切去别的目录，再把路径发给团子。她会先读结构和关键文档，再帮你梳理项目线。";
            EnsureRecentCompanionPrompt("第一次启动时，你可以直接说“看看现在这个项目”。如果你要我读别的目录，再把项目文件夹路径发给我。我会先读结构、README、总结和说明文档，再帮你理出项目线。");
        }
    }

    private async Task<ActiveAiProvider> ResolveActiveProviderAsync()
    {
        var preferredProvider = Environment.GetEnvironmentVariable("DESKTOP_COMPANION_AI_PROVIDER")?.Trim().ToLowerInvariant();
        if (_cachedProvider is not null
            && DateTimeOffset.Now < _providerCacheValidUntil
            && string.Equals(_cachedProviderPreference, preferredProvider, StringComparison.Ordinal))
        {
            return _cachedProvider;
        }

        var openAiAvailability = await _openAiChatService.CheckAvailabilityAsync();

        if (preferredProvider == "openai" && openAiAvailability.IsAvailable)
        {
            return CacheResolvedProvider(BuildOpenAiProvider(openAiAvailability.StatusLabel), preferredProvider);
        }

        if (preferredProvider == "ollama")
        {
            var preferredOllamaAvailability = await _ollamaChatService.CheckAvailabilityAsync();
            if (preferredOllamaAvailability.IsAvailable)
            {
                return CacheResolvedProvider(BuildOllamaProvider(preferredOllamaAvailability.StatusLabel), preferredProvider);
            }

            if (openAiAvailability.IsAvailable)
            {
                return CacheResolvedProvider(BuildOpenAiProvider(openAiAvailability.StatusLabel), preferredProvider);
            }

            return CacheResolvedProvider(BuildDisconnectedProvider(), preferredProvider);
        }

        if (openAiAvailability.IsAvailable)
        {
            return CacheResolvedProvider(BuildOpenAiProvider(openAiAvailability.StatusLabel), preferredProvider);
        }

        var ollamaAvailability = await _ollamaChatService.CheckAvailabilityAsync();

        if (ollamaAvailability.IsAvailable)
        {
            return CacheResolvedProvider(BuildOllamaProvider(ollamaAvailability.StatusLabel), preferredProvider);
        }

        return CacheResolvedProvider(BuildDisconnectedProvider(), preferredProvider);
    }

    private ActiveAiProvider BuildOpenAiProvider(string label) =>
        new(
            AiProviderKind.OpenAi,
            label,
            _openAiChatService.GenerateReplyAsync,
            _openAiChatService.AnalyzeProjectDumpAsync,
            _openAiChatService.AnalyzePersonalProfileAsync);

    private ActiveAiProvider BuildOllamaProvider(string label) =>
        new(
            AiProviderKind.Ollama,
            label,
            _ollamaChatService.GenerateReplyAsync,
            _ollamaChatService.AnalyzeProjectDumpAsync,
            _ollamaChatService.AnalyzePersonalProfileAsync);

    private static ActiveAiProvider BuildDisconnectedProvider() =>
        new(
            AiProviderKind.None,
            "AI 未连接",
            (_, _, _, _, _, _, _, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<ProjectCognitionDigest?>(null),
            (_, _) => Task.FromResult<DistilledUserProfile?>(null));

    private ActiveAiProvider CacheResolvedProvider(ActiveAiProvider provider, string? preferredProvider)
    {
        _cachedProvider = provider;
        _cachedProviderPreference = preferredProvider;
        _providerCacheValidUntil = DateTimeOffset.Now.Add(ProviderProbeCacheDuration);
        return provider;
    }

    private void InvalidateProviderCache()
    {
        _cachedProvider = null;
        _cachedProviderPreference = null;
        _providerCacheValidUntil = DateTimeOffset.MinValue;
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
            if (ShouldAppendSupervisionReview(reviewReply, now))
            {
                AddConversation(ConversationRole.Companion, reviewReply);
                RefreshDashboard(reviewReply);
            }
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
        SyncProjectsFromCodexThreadIndex();
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
            StatusLine = $"我还记得你手里挂着“{GetProjectDisplayLabel(GetOrderedProjects().First())}”这条。";
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
        RhythmStatusLine = "自动提醒已关闭。";

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
            var note = !string.IsNullOrWhiteSpace(project.RecentCodexThreadTitles.FirstOrDefault())
                ? $"Codex 最近：{project.RecentCodexThreadTitles.FirstOrDefault()}"
                : !string.IsNullOrWhiteSpace(project.NextAction)
                    ? $"下一步：{project.NextAction}"
                : !string.IsNullOrWhiteSpace(project.Summary)
                    ? project.Summary
                : project.RecentItems.FirstOrDefault() ?? "这条项目线已经被团子记住了。";

            RecentMemoryCards.Add(new RecentMemoryViewModel(
                GetProjectDisplayLabel(project),
                note,
                GetProjectMemoryStateLabel(project),
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
                "可读目录、记住路径并梳理项目线",
                "可读私人资料并沉淀安全画像"
            ];
        }

        if (_distilledUserProfile is null && _permissionProfile.CanBuildPersonalProfileFromPrivateSources)
        {
            return
            [
                "蒸馏一下我",
                "我想让你更了解我",
                "G:\\wechat 和桌面这些资料可以读"
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
        var focusText = TrimForBubble(GetLeadProjectFocus(leadProject), 14);

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
        SyncProjectsFromCodexThreadIndex(force: true);
        RecomputeProjectStates();
        PersistProjects();
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
            .Where(message => !LooksLikeAutomaticSupervisionMessage(message))
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

    private bool ShouldAppendSupervisionReview(string reviewReply, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reviewReply))
        {
            return false;
        }

        var latestCompanion = _conversationHistory.LastOrDefault(message => message.Role == ConversationRole.Companion);
        if (string.Equals(latestCompanion?.Text, reviewReply, StringComparison.Ordinal))
        {
            return false;
        }

        var latestUser = _conversationHistory.LastOrDefault(message => message.Role == ConversationRole.User);
        if (latestUser is null || now - latestUser.CreatedAt > TimeSpan.FromHours(3))
        {
            return false;
        }

        return _tasks.Any(task => task.State is CompanionTaskState.Doing or CompanionTaskState.Blocked)
               || _focusSprintEndsAt is not null;
    }

    private static bool LooksLikeAutomaticSupervisionMessage(ConversationMessage message)
    {
        if (message.Role != ConversationRole.Companion)
        {
            return false;
        }

        return message.Text.StartsWith("我回来", StringComparison.OrdinalIgnoreCase)
               && ContainsAnyIgnoreCase(message.Text, "盯进度", "检查主线", "梳理", "看看");
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

    private void RecomputeProjectStates(IEnumerable<ProjectMemory>? projects = null)
    {
        foreach (var project in projects ?? _projects)
        {
            RecomputeProjectState(project);
        }
    }

    private async Task RecomputeProjectStatesAsync(
        IEnumerable<ProjectMemory>? projects = null,
        bool allowExternalSearch = false,
        CancellationToken cancellationToken = default)
    {
        foreach (var project in projects ?? _projects)
        {
            await RecomputeProjectStateAsync(project, allowExternalSearch, cancellationToken);
        }
    }

    private void RecomputeProjectState(ProjectMemory project)
    {
        var resolution = _projectArchetypeResolverService.Resolve(project);
        ApplyArchetypeResolution(project, resolution);

        var assessment = _internalProjectAssessmentService.Assess(project);
        var externalSignals = BuildBaselineExternalSignals(project, assessment);
        project.ExternalSignals = externalSignals;
        project.StateAssessment = _projectStateScorer.Score(project, assessment, externalSignals);
    }

    private async Task RecomputeProjectStateAsync(
        ProjectMemory project,
        bool allowExternalSearch,
        CancellationToken cancellationToken)
    {
        var resolution = _projectArchetypeResolverService.Resolve(project);
        ApplyArchetypeResolution(project, resolution);

        var assessment = _internalProjectAssessmentService.Assess(project);
        var externalSignals = BuildBaselineExternalSignals(project, assessment);

        if (allowExternalSearch && ShouldRefreshExternalSignals(project, assessment))
        {
            externalSignals = await _externalSignalCollectorService.CollectSignalsAsync(project, assessment, cancellationToken);
        }

        project.ExternalSignals = externalSignals;
        project.StateAssessment = _projectStateScorer.Score(project, assessment, externalSignals);
    }

    private static void ApplyArchetypeResolution(ProjectMemory project, ProjectArchetypeResolution resolution)
    {
        project.ArchetypeLabel = ProjectArchetypes.ToLabel(resolution.Archetype);
        project.ArchetypeConfidence = resolution.Confidence;
        project.ArchetypeReason = resolution.Reason;
    }

    private ProjectExternalSignalSnapshot BuildBaselineExternalSignals(
        ProjectMemory project,
        ProjectStateAssessment assessment)
    {
        var baseline = _externalSignalCollectorService.BuildPlaceholderSnapshot(project, assessment);
        var existing = project.ExternalSignals;
        if (existing is null)
        {
            return baseline;
        }

        if (!string.IsNullOrWhiteSpace(existing.StatusLabel))
        {
            baseline.StatusLabel = existing.StatusLabel;
        }

        if (!string.IsNullOrWhiteSpace(existing.Summary))
        {
            baseline.Summary = existing.Summary;
        }

        if (existing.References.Count > 0)
        {
            baseline.References = existing.References
                .OrderByDescending(reference => reference.ObservedAt)
                .Take(6)
                .ToList();
        }

        baseline.UpdatedAt = existing.UpdatedAt == default ? baseline.UpdatedAt : existing.UpdatedAt;
        return baseline;
    }

    private static bool ShouldRefreshExternalSignals(ProjectMemory project, ProjectStateAssessment assessment)
    {
        if (assessment.SearchStatusLabel == "not_needed")
        {
            return false;
        }

        if (project.ExternalSignals is null || project.ExternalSignals.References.Count == 0)
        {
            return true;
        }

        if (!string.Equals(project.ExternalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var age = DateTimeOffset.Now - project.ExternalSignals.UpdatedAt;
        return assessment.SearchStatusLabel == "required"
            ? age > TimeSpan.FromHours(18)
            : age > TimeSpan.FromDays(2);
    }

    private bool SyncProjectsFromCodexThreadIndex(bool force = false)
    {
        var now = DateTimeOffset.Now;
        if (!force && now - _lastCodexThreadSyncAt < _codexThreadSyncCadence)
        {
            return false;
        }

        _lastCodexThreadSyncAt = now;
        var overview = _localCodexThreadIndexService.BuildOverview(24);
        if (!overview.IsSuccess)
        {
            return false;
        }

        var changed = false;
        foreach (var workspace in overview.Workspaces)
        {
            changed |= UpsertProjectFromCodexWorkspace(workspace);
        }

        if (changed)
        {
            RecomputeProjectStates();
            PersistProjects();
        }

        return changed;
    }

    private bool UpsertProjectFromCodexWorkspace(LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        var project = FindBestProjectForCodexWorkspace(workspace);
        if (ShouldForceSeparateCodexWorkspace(project, workspace))
        {
            var changed = ClearCodexWorkspaceBinding(project!);
            var syntheticProject = _projects.FirstOrDefault(candidate => IsSyntheticCodexProjectForWorkspace(candidate, workspace));
            if (syntheticProject is null)
            {
                _projects.Add(CreateProjectMemoryFromCodexWorkspace(workspace));
                return true;
            }

            return UpsertCodexWorkspaceFields(syntheticProject, workspace) || changed;
        }

        if (project is null)
        {
            _projects.Add(CreateProjectMemoryFromCodexWorkspace(workspace));
            return true;
        }

        return UpsertCodexWorkspaceFields(project, workspace);
    }

    private bool UpsertCodexWorkspaceFields(ProjectMemory project, LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        var changed = false;
        changed |= UpdateProjectString(project.CodexWorkspacePath, workspace.Cwd, value => project.CodexWorkspacePath = value);
        changed |= UpdateProjectString(project.CodexWorkspaceLabel, workspace.Label, value => project.CodexWorkspaceLabel = value);
        changed |= UpdateProjectString(
            project.PrimaryWorkspacePath,
            string.IsNullOrWhiteSpace(project.PrimaryWorkspacePath) ? workspace.Cwd : project.PrimaryWorkspacePath,
            value => project.PrimaryWorkspacePath = value);
        changed |= UpdateProjectString(
            project.PrimaryWorkspaceLabel,
            string.IsNullOrWhiteSpace(project.PrimaryWorkspaceLabel) ? workspace.Label : project.PrimaryWorkspaceLabel,
            value => project.PrimaryWorkspaceLabel = value);
        changed |= UpdateProjectString(
            project.WorkspaceKindLabel,
            string.IsNullOrWhiteSpace(project.WorkspaceKindLabel) ? "Codex workspace" : project.WorkspaceKindLabel,
            value => project.WorkspaceKindLabel = value);

        if (project.CodexThreadCount != workspace.ThreadCount)
        {
            project.CodexThreadCount = workspace.ThreadCount;
            changed = true;
        }

        if (project.LastCodexThreadAt != workspace.LastUpdatedAt)
        {
            project.LastCodexThreadAt = workspace.LastUpdatedAt;
            changed = true;
        }

        var mergedTitles = MergeProjectStrings(project.RecentCodexThreadTitles, workspace.RecentTitles, 6);
        if (!project.RecentCodexThreadTitles.SequenceEqual(mergedTitles))
        {
            project.RecentCodexThreadTitles = mergedTitles;
            changed = true;
        }

        var mergedKeywords = MergeProjectStrings(project.Keywords, [workspace.Label], 10);
        if (!project.Keywords.SequenceEqual(mergedKeywords))
        {
            project.Keywords = mergedKeywords;
            changed = true;
        }

        var mergedRecentItems = MergeProjectStrings(project.RecentItems, workspace.RecentTitles, 8);
        if (!project.RecentItems.SequenceEqual(mergedRecentItems))
        {
            project.RecentItems = mergedRecentItems;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(project.NextAction) && workspace.RecentTitles.Count > 0)
        {
            project.NextAction = workspace.RecentTitles[0];
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(project.CurrentMilestone) && workspace.RecentTitles.Count > 0)
        {
            project.CurrentMilestone = workspace.RecentTitles[0];
            changed = true;
        }

        if (changed && workspace.LastUpdatedAt > project.UpdatedAt)
        {
            project.UpdatedAt = workspace.LastUpdatedAt;
        }

        return changed;
    }

    private bool ShouldForceSeparateCodexWorkspace(
        ProjectMemory? project,
        LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        return project is not null
               && LooksLikeDriveRootWorkspace(workspace)
               && !IsSyntheticCodexProjectForWorkspace(project, workspace);
    }

    private static bool LooksLikeDriveRootWorkspace(LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        return Regex.IsMatch(workspace.Label ?? string.Empty, "^[A-Za-z]:$");
    }

    private static bool IsSyntheticCodexProjectForWorkspace(
        ProjectMemory project,
        LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        return string.Equals(project.CodexWorkspacePath, workspace.Cwd, StringComparison.OrdinalIgnoreCase)
               && string.Equals(project.CodexWorkspaceLabel, workspace.Label, StringComparison.OrdinalIgnoreCase)
               && IsSyntheticCodexProject(project);
    }

    private static bool IsSyntheticCodexProject(ProjectMemory project)
    {
        return string.Equals(project.Name, project.CodexWorkspaceLabel, StringComparison.OrdinalIgnoreCase)
               && project.Summary.StartsWith("Synced from Codex workspace", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ClearCodexWorkspaceBinding(ProjectMemory project)
    {
        var changed = false;

        changed |= UpdateProjectString(project.CodexWorkspacePath, string.Empty, value => project.CodexWorkspacePath = value);
        changed |= UpdateProjectString(project.CodexWorkspaceLabel, string.Empty, value => project.CodexWorkspaceLabel = value);

        if (project.CodexThreadCount != 0)
        {
            project.CodexThreadCount = 0;
            changed = true;
        }

        if (project.LastCodexThreadAt is not null)
        {
            project.LastCodexThreadAt = null;
            changed = true;
        }

        if (project.RecentCodexThreadTitles.Count > 0)
        {
            project.RecentCodexThreadTitles = [];
            changed = true;
        }

        return changed;
    }

    private static bool ShouldIncludeAsRememberedProject(ProjectMemory project)
    {
        if (IsSyntheticCodexProject(project))
        {
            return false;
        }

        if (LooksLikeDriveRootLabel(project))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(project.Name)
               || !string.IsNullOrWhiteSpace(project.Summary)
               || !string.IsNullOrWhiteSpace(project.NextAction);
    }

    private ProjectMemory? FindBestProjectForCodexWorkspace(LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        var normalizedLabel = NormalizeForMatch(workspace.Label);

        return _projects
            .Select(project => new
            {
                Project = project,
                Score = GetCodexWorkspaceProjectScore(project, workspace.Cwd, workspace.Label, normalizedLabel)
            })
            .Where(item => item.Score >= 60)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Project.UpdatedAt)
            .Select(item => item.Project)
            .FirstOrDefault();
    }

    private static int GetCodexWorkspaceProjectScore(
        ProjectMemory project,
        string workspacePath,
        string workspaceLabel,
        string normalizedLabel)
    {
        var hasConflictingPrimaryIdentity =
            !string.IsNullOrWhiteSpace(project.PrimaryWorkspacePath)
            && !string.Equals(project.PrimaryWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(project.PrimaryWorkspaceLabel)
            && !string.Equals(project.PrimaryWorkspaceLabel, workspaceLabel, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(project.Name, workspaceLabel, StringComparison.OrdinalIgnoreCase);

        if (hasConflictingPrimaryIdentity)
        {
            return 0;
        }

        var score = 0;
        if (string.Equals(project.CodexWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (string.Equals(project.PrimaryWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 96;
        }

        score = Math.Max(score, GetNormalizedLabelScore(project.CodexWorkspaceLabel, normalizedLabel, 88));
        score = Math.Max(score, GetNormalizedLabelScore(project.PrimaryWorkspaceLabel, normalizedLabel, 84));
        score = Math.Max(score, GetNormalizedLabelScore(project.Name, normalizedLabel, 78));

        if (project.Keywords.Any(keyword => NormalizeForMatch(keyword) == normalizedLabel))
        {
            score = Math.Max(score, 72);
        }

        if (!string.IsNullOrWhiteSpace(normalizedLabel))
        {
            var normalizedName = NormalizeForMatch(project.Name);
            if (!string.IsNullOrWhiteSpace(normalizedName)
                && (normalizedName.Contains(normalizedLabel, StringComparison.OrdinalIgnoreCase)
                    || normalizedLabel.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                score = Math.Max(score, 62);
            }
        }

        return score;
    }

    private static int GetNormalizedLabelScore(string source, string normalizedLabel, int exactScore)
    {
        return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(normalizedLabel)
            ? 0
            : NormalizeForMatch(source) == normalizedLabel
                ? exactScore
                : 0;
    }

    private static ProjectMemory CreateProjectMemoryFromCodexWorkspace(LocalCodexThreadIndexService.CodexWorkspaceSummary workspace)
    {
        var focus = workspace.RecentTitles.FirstOrDefault() ?? string.Empty;
        return new ProjectMemory
        {
            Name = workspace.Label,
            Summary = $"Synced from Codex workspace {workspace.Label}",
            KindLabel = "Codex 项目",
            NextAction = focus,
            CurrentMilestone = focus,
            PrimaryWorkspacePath = workspace.Cwd,
            PrimaryWorkspaceLabel = workspace.Label,
            WorkspaceKindLabel = "Codex workspace",
            CodexWorkspacePath = workspace.Cwd,
            CodexWorkspaceLabel = workspace.Label,
            CodexThreadCount = workspace.ThreadCount,
            LastCodexThreadAt = workspace.LastUpdatedAt,
            Keywords = MergeProjectStrings([], [workspace.Label], 10),
            RecentItems = MergeProjectStrings([], workspace.RecentTitles, 8),
            RecentCodexThreadTitles = MergeProjectStrings([], workspace.RecentTitles, 6),
            UpdatedAt = workspace.LastUpdatedAt
        };
    }

    private static List<string> MergeProjectStrings(
        IEnumerable<string> existing,
        IEnumerable<string> incoming,
        int maxCount)
    {
        return existing
            .Concat(incoming)
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    private static bool UpdateProjectString(string currentValue, string value, Action<string> assign)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.Equals(currentValue, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        assign(normalized);
        return true;
    }

    private IReadOnlyList<ProjectMemory> GetOrderedProjects()
    {
        return _projects
            .OrderByDescending(project => project.LastCodexThreadAt ?? DateTimeOffset.MinValue)
            .ThenBy(project => GetProjectPriorityRank(project.PriorityLabel))
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
            _permissionProfile.CanBuildPersonalProfileFromPrivateSources = false;
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
            _permissionProfile.CanBuildPersonalProfileFromPrivateSources = false;
            _permissionProfile.PresetLabel = "只读指定目录";
            PersistPermissionProfile();
            reply = "好，我可以读你临时指定的项目目录，但不会记住路径，也不会默认把项目线长期存下来。你现在直接说“看看现在这个项目”也行；如果是别的目录，再把路径发给我。";
            return true;
        }

        if (input.Contains("可读目录并记住授权路径", StringComparison.OrdinalIgnoreCase)
            || input.Contains("读目录并记住", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = true;
            _permissionProfile.CanBuildProjectMemoryFromDocs = false;
            _permissionProfile.CanBuildPersonalProfileFromPrivateSources = false;
            _permissionProfile.PresetLabel = "读目录并记住路径";
            PersistPermissionProfile();
            reply = "好，我可以读你指定的目录，也可以记住你授权过的路径，但我暂时不把文档内容自动沉淀成长期项目记忆。你现在直接说“看看现在这个项目”也行；如果是别的目录，再把路径发给我。";
            return true;
        }

        if (input.Contains("可读目录、记住路径并梳理项目线", StringComparison.OrdinalIgnoreCase)
            || input.Contains("梳理项目线", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = true;
            _permissionProfile.CanBuildProjectMemoryFromDocs = true;
            _permissionProfile.CanBuildPersonalProfileFromPrivateSources = false;
            _permissionProfile.PresetLabel = "读目录并梳理项目线";
            PersistPermissionProfile();
            reply = "好，这套权限下我可以读你指定的目录，记住你授权过的路径，并把文档内容整理成项目线和下一步。你现在直接说“看看现在这个项目”也行；如果是别的目录，再把路径发给我。";
            return true;
        }

        if (input.Contains("私人资料", StringComparison.OrdinalIgnoreCase)
            || input.Contains("安全画像", StringComparison.OrdinalIgnoreCase))
        {
            _permissionProfile.IsConfigured = true;
            _permissionProfile.CanReadSelectedWorkspace = true;
            _permissionProfile.CanRememberWorkspaceSources = true;
            _permissionProfile.CanBuildProjectMemoryFromDocs = true;
            _permissionProfile.CanBuildPersonalProfileFromPrivateSources = true;
            _permissionProfile.PresetLabel = "读私人资料并沉淀安全画像";
            PersistPermissionProfile();
            reply = "好，这档权限下我可以读你明确授权的私人资料来源，但只沉淀隐私安全画像，不长期保存原始聊天内容、联系人细节和敏感身份信息。现在你可以把像 G:\\wechat 这样的来源路径发给我。";
            return true;
        }

        return false;
    }

    private string BuildWorkspaceImportReply(
        WorkspaceIngestionService.WorkspaceScanResult scan,
        ProjectCognitionDigest digest,
        RepoStructureReaderService.RepoStructureSnapshot repoStructure)
    {
        var prefix = repoStructure.IsSuccess
            ? $"我先读了 {scan.RootLabel} 的目录骨架，{_repoStructureReaderService.BuildUserFacingSummary(repoStructure)}，再顺手看了 {scan.Documents.Count} 份资料。"
            : $"我先读了 {scan.RootLabel} 这一路里的 {scan.Documents.Count} 份资料。";

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

    private static string BuildDirectorySurfaceReply(
        string input,
        WorkspaceIngestionService.DirectorySurfaceResult surface)
    {
        if (!surface.IsSuccess)
        {
            return surface.Message;
        }

        var displayLabel = input.Contains("桌面", StringComparison.OrdinalIgnoreCase)
                           || input.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? "桌面"
            : surface.RootLabel;

        var lines = new List<string>
        {
            $"我先看了一眼{displayLabel}，顶层大概有 {surface.Directories.Count} 个文件夹、{surface.Files.Count} 个文件。"
        };

        if (surface.Directories.Count > 0)
        {
            lines.Add(BuildDirectorySurfacePreview("文件夹", surface.Directories));
        }

        if (surface.Files.Count > 0)
        {
            lines.Add(BuildDirectorySurfacePreview("文件", surface.Files));
        }

        lines.Add("你要的话，我下一步可以只展开其中一个目录继续看。");
        return string.Join("\n", lines);
    }

    private static string BuildDirectorySurfacePreview(string label, IReadOnlyList<string> entries)
    {
        const int previewCount = 6;
        var preview = string.Join("、", entries.Take(previewCount));
        return entries.Count > previewCount
            ? $"{label}包括：{preview} 等 {entries.Count} 项。"
            : $"{label}包括：{preview}。";
    }

    private async Task HandleDirectorySurfaceAsync(string input, string directoryPath)
    {
        if (!_permissionProfile.CanReadSelectedWorkspace)
        {
            const string noPermissionReply = "这一步我先不去读你电脑里的目录。你先从左边选一个权限方案，我再按你给的边界做事。";
            AddConversation(ConversationRole.Companion, noPermissionReply);
            RefreshDashboard(noPermissionReply);
            return;
        }

        IsAwaitingReply = true;
        AiStatusLabel = "本地目录中";
        WhisperLine = "团子先去看一眼这个目录顶层有什么。";
        RefreshDashboard("团子先去看一眼这个目录顶层有什么。");

        try
        {
            var surface = await Task.Run(() => _workspaceIngestionService.DescribeDirectorySurface(directoryPath));
            if (surface.IsSuccess)
            {
                RememberDirectorySurface(surface);
            }

            var directoryReply = BuildDirectorySurfaceReply(input, surface);
            AiStatusLabel = surface.IsSuccess ? "本地目录" : "本地目录暂时没接住";
            AddConversation(ConversationRole.Companion, directoryReply);
            RefreshDashboard(directoryReply);
        }
        catch
        {
            const string failureReply = "这个目录我去看了，但这轮没顺利把顶层内容列出来。你把更具体的路径再发我一次，我继续。";
            AiStatusLabel = "本地目录暂时没接住";
            AddConversation(ConversationRole.Companion, failureReply);
            RefreshDashboard(failureReply);
        }
        finally
        {
            IsAwaitingReply = false;
        }
    }

    private async Task HandleWorkspaceUnderstandingAsync(string workspacePath)
    {
        if (!_permissionProfile.CanReadSelectedWorkspace)
        {
            const string noPermissionReply = "这一步我先不去读你电脑里的目录。你先从左边选一个权限方案，我再按你给的边界做事。";
            AddConversation(ConversationRole.Companion, noPermissionReply);
            RefreshDashboard(noPermissionReply);
            return;
        }

        ActiveAiProvider? provider = null;
        async Task<ActiveAiProvider> GetProviderAsync()
        {
            provider ??= await ResolveActiveProviderAsync();
            return provider;
        }

        IsAwaitingReply = true;
        AiStatusLabel = "AI 读工作区中";
        WhisperLine = "团子在顺着目录骨架、入口文件和关键实现文件往里读。";
        RefreshDashboard("团子在顺着目录骨架、入口文件和关键实现文件往里读。");

        try
        {
            provider = await GetProviderAsync();
            var scan = await Task.Run(() => _workspaceIngestionService.ScanWorkspace(workspacePath));
            if (!scan.IsSuccess)
            {
                AiStatusLabel = provider.Label;
                AddConversation(ConversationRole.Companion, scan.Message);
                RefreshDashboard(scan.Message);
                return;
            }

            var repoStructure = _repoStructureReaderService.ReadStructure(scan.RootPath);
            var analysisInput = repoStructure.IsSuccess
                ? _repoStructureReaderService.BuildAnalysisInput(scan, repoStructure)
                : scan.AnalysisInput;

            var digest = await provider.AnalyzeProjectDumpAsync(
                             analysisInput,
                             GetOrderedProjects())
                         ?? _projectCognitionService.CreateFallbackDigest(
                             string.Join(
                                 "\n",
                                 scan.Documents.Select(document => $"{document.FileName}: {document.Excerpt}")),
                             GetOrderedProjects());

            if (_permissionProfile.CanBuildProjectMemoryFromDocs)
            {
                _projectCognitionService.MergeDigestIntoProjects(_projects, digest);
                await RecomputeProjectStatesAsync(allowExternalSearch: true);
                PersistProjects();
            }

            if (_permissionProfile.CanRememberWorkspaceSources)
            {
                UpsertWorkspaceSource(scan);
                PersistWorkspaceSources();
            }

            RememberActiveWorkspace(
                scan.RootPath,
                scan.RootLabel,
                repoStructure.IsSuccess ? repoStructure.KindLabel : null);

            var workspaceReply = BuildWorkspaceImportReply(scan, digest, repoStructure);
            AiStatusLabel = provider.Label;
            AddConversation(ConversationRole.Companion, workspaceReply);
            RefreshDashboard(workspaceReply);
        }
        catch
        {
            const string failureReply = "这个工作区我去读了，但这轮还没稳稳接住。你可以先给我更靠近入口文件、主代码目录或 README 的那一层。";
            InvalidateProviderCache();
            AiStatusLabel = $"{provider?.Label ?? "AI"} 暂时没接住";
            AddConversation(ConversationRole.Companion, failureReply);
            RefreshDashboard(failureReply);
        }
        finally
        {
            IsAwaitingReply = false;
        }
    }

    private void RememberDirectorySurface(WorkspaceIngestionService.DirectorySurfaceResult surface)
    {
        _lastDirectorySurface = surface;
        _pendingDirectoryPath = null;
        _pendingDirectoryLabel = null;
    }

    private void RememberActiveWorkspace(string workspacePath, string workspaceLabel, string? kindLabel = null)
    {
        _activeWorkspacePath = workspacePath;
        _activeWorkspaceLabel = workspaceLabel;
        _activeWorkspaceKindLabel = kindLabel;
        _activeWorkspaceTouchedAt = DateTimeOffset.Now;
        _pendingWorkspacePath = null;
        _pendingWorkspaceLabel = null;
    }

    private bool TryBuildDirectoryRecommendationReply(out string reply)
    {
        reply = string.Empty;
        if (!TryGetLastDirectorySurface(out var surface) || surface.Directories.Count == 0)
        {
            return false;
        }

        var selectedDirectory = surface.Directories
            .OrderByDescending(ScoreDirectoryInterest)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .First();

        _pendingWorkspacePath = Path.Combine(surface.RootPath, selectedDirectory);
        _pendingWorkspaceLabel = selectedDirectory;

        reply = $"我会先看 {selectedDirectory}。它比桌面上的零散文件更像一条成型主线；你说“好的”“继续”或“展开它”，我就直接按工作区方式读它的结构、入口和关键文件。";
        return true;
    }

    private bool TryResolveContextualWorkspaceUnderstandingRequest(string input, out string workspacePath)
    {
        workspacePath = string.Empty;
        var looksLikeUnderstanding = LooksLikeWorkspaceUnderstandingRequest(input);
        var looksLikeContinuation = LooksLikeAffirmativeDirectoryFollowUp(input)
            || LooksLikeWorkspaceContinuationRequest(input);

        if (TryResolveWorkspaceMentionFromRecentSurface(input, out workspacePath))
        {
            return true;
        }

        if (looksLikeUnderstanding)
        {
            if (TryResolveWorkspaceFromKnownContext(
                    out workspacePath,
                    forceFreshCodexSync: true,
                    allowDriveRootWorkspace: true,
                    allowAppRepositoryRoot: false))
            {
                return true;
            }
        }

        if (looksLikeContinuation)
        {
            if (TryResolveWorkspaceFromKnownContext(
                    out workspacePath,
                    forceFreshCodexSync: true,
                    allowDriveRootWorkspace: true,
                    allowAppRepositoryRoot: false))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveContextualDirectorySurfaceRequest(string input, out string directoryPath)
    {
        directoryPath = string.Empty;

        if (TryResolveDirectoryMentionFromRecentSurface(input, out directoryPath))
        {
            return true;
        }

        if (LooksLikeAffirmativeDirectoryFollowUp(input)
            && TryResolvePendingDirectory(out directoryPath))
        {
            return true;
        }

        if (ContainsAnyIgnoreCase(input, "给你授权", "授权", "继续", "展开", "展开它", "读它", "看看它", "怎么样")
            && TryResolvePendingDirectory(out directoryPath))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveWorkspaceMentionFromRecentSurface(string input, out string workspacePath)
    {
        workspacePath = string.Empty;
        if (!LooksLikeWorkspaceUnderstandingRequest(input) || !TryGetLastDirectorySurface(out var surface))
        {
            return false;
        }

        foreach (var directoryName in surface.Directories.OrderByDescending(name => name.Length))
        {
            if (!input.Contains(directoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            workspacePath = Path.Combine(surface.RootPath, directoryName);
            _pendingWorkspacePath = workspacePath;
            _pendingWorkspaceLabel = directoryName;
            return true;
        }

        return false;
    }

    private bool TryResolveDirectoryMentionFromRecentSurface(string input, out string directoryPath)
    {
        directoryPath = string.Empty;
        if (!TryGetLastDirectorySurface(out var surface))
        {
            return false;
        }

        foreach (var directoryName in surface.Directories.OrderByDescending(name => name.Length))
        {
            if (!input.Contains(directoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            directoryPath = Path.Combine(surface.RootPath, directoryName);
            _pendingDirectoryPath = directoryPath;
            _pendingDirectoryLabel = directoryName;
            return true;
        }

        return false;
    }

    private bool TryResolvePendingDirectory(out string directoryPath)
    {
        directoryPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(_pendingDirectoryPath) && Directory.Exists(_pendingDirectoryPath))
        {
            directoryPath = _pendingDirectoryPath;
            return true;
        }

        if (TryResolvePendingDirectoryFromConversation(out directoryPath))
        {
            _pendingDirectoryPath = directoryPath;
            _pendingDirectoryLabel = Path.GetFileName(directoryPath);
            return true;
        }

        return false;
    }

    private bool TryResolvePendingWorkspace(out string workspacePath)
    {
        workspacePath = string.Empty;
        if (!string.IsNullOrWhiteSpace(_pendingWorkspacePath) && Directory.Exists(_pendingWorkspacePath))
        {
            workspacePath = _pendingWorkspacePath;
            return true;
        }

        return false;
    }

    private bool TryResolveActiveWorkspace(out string workspacePath)
    {
        workspacePath = string.Empty;
        if (string.IsNullOrWhiteSpace(_activeWorkspacePath) || !Directory.Exists(_activeWorkspacePath))
        {
            return false;
        }

        if (_activeWorkspaceTouchedAt is null || DateTimeOffset.Now - _activeWorkspaceTouchedAt > TimeSpan.FromMinutes(30))
        {
            return false;
        }

        workspacePath = _activeWorkspacePath;
        return true;
    }

    private bool TryResolveWorkspaceFromKnownContext(
        out string workspacePath,
        bool forceFreshCodexSync = false,
        bool allowDriveRootWorkspace = true,
        bool allowAppRepositoryRoot = false)
    {
        workspacePath = string.Empty;

        if (TryResolvePendingWorkspace(out workspacePath))
        {
            return true;
        }

        if (TryResolveActiveWorkspace(out workspacePath))
        {
            return true;
        }

        if (TryResolveRecentCodexWorkspace(
                out workspacePath,
                allowDriveRootWorkspace: allowDriveRootWorkspace,
                forceFreshCodexSync: forceFreshCodexSync))
        {
            return true;
        }

        workspacePath = _workspaceSources
            .OrderByDescending(source => source.LastScannedAt)
            .Select(source => source.Path)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(workspacePath)
            && (allowDriveRootWorkspace || !LooksLikeDriveRootPath(workspacePath)))
        {
            return true;
        }

        if (allowAppRepositoryRoot
            && TryResolveAppRepositoryRoot(out workspacePath)
            && (allowDriveRootWorkspace || !LooksLikeDriveRootPath(workspacePath)))
        {
            return true;
        }

        workspacePath = string.Empty;
        return false;
    }

    private bool TryResolveRecentCodexWorkspace(
        out string workspacePath,
        bool allowDriveRootWorkspace = true,
        bool forceFreshCodexSync = false)
    {
        workspacePath = string.Empty;
        if (!TryResolveRecentCodexProject(
                out var project,
                allowDriveRootWorkspace: allowDriveRootWorkspace,
                forceFreshCodexSync: forceFreshCodexSync))
        {
            return false;
        }

        workspacePath = GetProjectWorkspacePath(project);
        return !string.IsNullOrWhiteSpace(workspacePath);
    }

    private bool TryResolveRecentCodexProject(
        out ProjectMemory project,
        bool allowDriveRootWorkspace = true,
        bool forceFreshCodexSync = false)
    {
        project = null!;
        SyncProjectsFromCodexThreadIndex(force: forceFreshCodexSync);
        var freshnessCutoff = DateTimeOffset.Now - TimeSpan.FromDays(14);

        project = GetOrderedProjects()
            .FirstOrDefault(candidate =>
            {
                if (candidate.LastCodexThreadAt is null || candidate.LastCodexThreadAt < freshnessCutoff)
                {
                    return false;
                }

                var path = GetProjectWorkspacePath(candidate);
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    return false;
                }

                return allowDriveRootWorkspace || !LooksLikeDriveRootPath(path);
            })!;

        return project is not null;
    }

    private async Task<bool> TryHandleStructuredProjectScopedFallbackAsync(string input)
    {
        if (!LooksLikeProjectScopedConversation(input))
        {
            return false;
        }

        if (!TryResolveWorkspaceFromKnownContext(
                out var workspacePath,
                forceFreshCodexSync: true,
                allowDriveRootWorkspace: true,
                allowAppRepositoryRoot: false))
        {
            const string missingProjectReply = "我听出来你还在说项目，但这轮没把对象锁定下来。我先不把它丢进普通聊天里。你可以直接说“看看现在这个项目”，或者如果你要切别的项目，再给我项目名或路径。";
            AddConversation(ConversationRole.Companion, missingProjectReply);
            RefreshDashboard(missingProjectReply);
            return true;
        }

        if (LooksLikeProjectScopedSocialConversation(input))
        {
            var workspaceLabel = GetWorkspaceDisplayLabel(workspacePath);
            var scopedReply = $"我先按当前项目“{workspaceLabel}”这条线继续记着，不把它丢进普通闲聊里丢上下文。你如果要我继续读结构、看最近线程、判断下一步，或者直接交给 Codex，就直接说；如果你只是想先吐槽，我也继续围着这条项目陪你。";
            AddConversation(ConversationRole.Companion, scopedReply);
            RefreshDashboard(scopedReply);
            return true;
        }

        await HandleWorkspaceUnderstandingAsync(workspacePath);
        return true;
    }

    private bool TryResolvePendingDirectoryFromConversation(out string directoryPath)
    {
        directoryPath = string.Empty;
        foreach (var message in _conversationHistory
                     .Where(message => message.Role == ConversationRole.Companion)
                     .Reverse()
                     .Take(8))
        {
            var match = Regex.Match(
                message.Text,
                @"(?:~[/\\])?Desktop[/\\](?<tail>[^\s，。；;,]+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                continue;
            }

            var candidatePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                match.Groups["tail"].Value.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(candidatePath))
            {
                directoryPath = candidatePath;
                return true;
            }
        }

        return false;
    }

    private bool TryGetLastDirectorySurface(out WorkspaceIngestionService.DirectorySurfaceResult surface)
    {
        if (_lastDirectorySurface is { IsSuccess: true } currentSurface)
        {
            surface = currentSurface;
            return true;
        }

        var hasRecentDesktopSurface = _conversationHistory
            .Where(message => message.Role == ConversationRole.Companion)
            .Reverse()
            .Take(12)
            .Any(message => message.Text.Contains("我先看了一眼桌面", StringComparison.OrdinalIgnoreCase));

        if (hasRecentDesktopSurface
            && _workspaceIngestionService.TryExtractWorkspacePath("看看桌面有什么", out var desktopPath))
        {
            var desktopSurface = _workspaceIngestionService.DescribeDirectorySurface(desktopPath);
            if (desktopSurface.IsSuccess)
            {
                _lastDirectorySurface = desktopSurface;
                surface = desktopSurface;
                return true;
            }
        }

        surface = WorkspaceIngestionService.DirectorySurfaceResult.Failure(string.Empty);
        return false;
    }

    private static bool LooksLikeDirectoryRecommendationRequest(string input)
    {
        return ContainsAnyIgnoreCase(input, "最有意思", "最重要", "先看哪个", "先读哪个", "哪个文件夹", "哪一个文件夹");
    }

    private bool LooksLikeWorkspaceUnderstandingRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_activeWorkspaceLabel)
            && input.Contains(_activeWorkspaceLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_pendingWorkspaceLabel)
            && input.Contains(_pendingWorkspaceLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsAnyIgnoreCase(
            input,
            "这个项目",
            "这个仓库",
            "这个目录",
            "这个工作区",
            "这个代码",
            "主链路",
            "主流程",
            "结构",
            "代码结构",
            "文件结构",
            "入口",
            "入口文件",
            "关键文件",
            "怎么组织",
            "最近改动",
            "最近在做什么",
            "下一步",
            "帮我理解",
            "分析一下",
            "解释一下",
            "这个是干嘛的",
            "这个什么意思");
    }

    private bool LooksLikeProjectScopedConversation(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (LooksLikeWorkspaceUnderstandingRequest(input)
            || LooksLikeWorkspaceContinuationRequest(input)
            || LooksLikeCodexStatusInspectionIntent(input)
            || LooksLikeCodexWorkspaceOverviewRequest(input)
            || LooksLikeCodexDispatchIntent(input)
            || LooksLikeVsCodeOpenRequest(input)
            || LooksLikeDirectorySurfaceRequest(input))
        {
            return true;
        }

        if (ContainsAnyIgnoreCase(
                input,
                "当前项目",
                "现在这个项目",
                "这个项目",
                "这个仓库",
                "这个 workspace",
                "这个workspace",
                "项目",
                "仓库",
                "repo",
                "repository",
                "workspace",
                "codex",
                "主线",
                "线程",
                "入口",
                "结构",
                "readme",
                "目录",
                "代码库"))
        {
            return true;
        }

        return MentionsKnownProjectContext(input);
    }

    private static bool LooksLikeWorkspaceContinuationRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return ContainsAnyIgnoreCase(
            input,
            "然后呢",
            "然后怎么样",
            "接下来呢",
            "接着呢",
            "再看一下",
            "再看看",
            "再往里看",
            "继续往下",
            "继续读",
            "展开一下",
            "展开说",
            "详细一点",
            "再细一点",
            "再具体一点",
            "顺着看",
            "顺着读",
            "这个呢",
            "这块呢",
            "那这个呢",
            "那这块呢",
            "入口在哪",
            "主链路在哪",
            "下一步呢",
            "还有什么",
            "为啥",
            "为什么这样");
    }

    private static bool LooksLikeProjectScopedSocialConversation(string input)
    {
        return ContainsAnyIgnoreCase(
            input,
            "在吗",
            "陪我",
            "聊聊",
            "说说",
            "吐槽",
            "累",
            "困",
            "烦",
            "焦虑",
            "不想做",
            "没劲",
            "难受",
            "崩",
            "谢谢",
            "多谢");
    }

    private static bool LooksLikeAffirmativeDirectoryFollowUp(string input)
    {
        var normalized = input.Trim();
        return normalized is "好" or "好的" or "行" or "可以" or "是" or "对" or "继续" or "嗯" or "嗯嗯";
    }

    private string? BuildActiveWorkspaceContextLine()
    {
        if (!string.IsNullOrWhiteSpace(_activeWorkspacePath))
        {
            var kindLabel = string.IsNullOrWhiteSpace(_activeWorkspaceKindLabel) ? "工作区" : _activeWorkspaceKindLabel;
            return $"当前活跃工作区：{_activeWorkspaceLabel ?? Path.GetFileName(_activeWorkspacePath)}（{kindLabel}，路径：{_activeWorkspacePath}）。如果用户没有明确切换对象，默认继续围绕这个工作区回答，不要要求重新提供路径、截图或命令输出。";
        }

        if (!string.IsNullOrWhiteSpace(_pendingWorkspacePath))
        {
            return $"当前待继续读取的工作区：{_pendingWorkspaceLabel ?? Path.GetFileName(_pendingWorkspacePath)}（路径：{_pendingWorkspacePath}）。如果用户没有明确切换对象，优先继续围绕它回答。";
        }

        if (_lastDirectorySurface is { IsSuccess: true } lastSurface)
        {
            return $"当前最近浏览的目录：{lastSurface.RootLabel}（路径：{lastSurface.RootPath}）。";
        }

        if (TryResolveRecentCodexProject(out var recentCodexProject, allowDriveRootWorkspace: true))
        {
            var workspacePath = GetProjectWorkspacePath(recentCodexProject);
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                return $"当前最活跃的 Codex workspace：{GetProjectDisplayLabel(recentCodexProject)}（路径：{workspacePath}）。如果用户没有明确切换对象，默认继续围绕这个工作区回答，不要要求重新提供路径、截图或命令输出。";
            }
        }

        return null;
    }

    private bool MentionsKnownProjectContext(string input)
    {
        if (!string.IsNullOrWhiteSpace(_activeWorkspaceLabel)
            && input.Contains(_activeWorkspaceLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_pendingWorkspaceLabel)
            && input.Contains(_pendingWorkspaceLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var project in GetOrderedProjects().Take(8))
        {
            var label = GetProjectDisplayLabel(project);
            if (!string.IsNullOrWhiteSpace(label)
                && input.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var recentTitle in project.RecentCodexThreadTitles.Take(4))
            {
                if (!string.IsNullOrWhiteSpace(recentTitle)
                    && input.Contains(recentTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int ScoreDirectoryInterest(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return int.MinValue;
        }

        var normalized = directoryName.ToLowerInvariant();
        var score = 0;

        if (normalized.StartsWith('.') || normalized is "__macosx" or "desktop")
        {
            score -= 100;
        }

        score += normalized switch
        {
            "eeg" => 80,
            "mainland" => 70,
            "learned_slot" => 68,
            "mutitrans" => 62,
            "ooni" => 58,
            "ml project" => 50,
            "light_mechan" => 46,
            "labro" => 44,
            _ => 0
        };

        if (normalized.Contains("project"))
        {
            score += 20;
        }

        if (normalized.Contains("output") || normalized.Contains("backup") || normalized.Contains("副本"))
        {
            score -= 25;
        }

        return score;
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

    private static bool LooksLikeCodexWorkspaceOverviewRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.ToLowerInvariant();
        if (!normalized.Contains("codex"))
        {
            return false;
        }

        return ContainsAnyIgnoreCase(
                   input,
                   "\u591a\u5c11\u4e2a\u9879\u76ee",
                   "\u591a\u5c11\u9879\u76ee",
                   "\u51e0\u4e2a\u9879\u76ee",
                   "\u6709\u4ec0\u4e48\u9879\u76ee",
                   "\u6709\u4ec0\u4e48\u4efb\u52a1",
                   "\u90fd\u5728\u505a\u4ec0\u4e48",
                   "\u5728\u5e72\u4ec0\u4e48",
                   "\u73b0\u5728\u5728\u505a\u4ec0\u4e48")
               || normalized.Contains("go through")
               || normalized.Contains("projects")
               || normalized.Contains("project")
               || normalized.Contains("tasks")
               || normalized.Contains("what is codex doing")
               || normalized.Contains("what's codex doing");
    }

    private string BuildCodexWorkspaceOverviewReply(
        LocalCodexThreadIndexService.CodexWorkspaceOverviewResult overview,
        IReadOnlyList<ProjectMemory> knownProjects)
    {
        if (!overview.IsSuccess)
        {
            return overview.Message;
        }

        var currentWorkspacePaths = new HashSet<string>(
            overview.Workspaces.Select(workspace => workspace.Cwd),
            StringComparer.OrdinalIgnoreCase);

        var rememberedProjects = knownProjects
            .Where(project => !string.IsNullOrWhiteSpace(GetProjectDisplayLabel(project)))
            .Where(project => !currentWorkspacePaths.Contains(project.CodexWorkspacePath ?? string.Empty))
            .Where(ShouldIncludeAsRememberedProject)
            .Select(GetProjectDisplayLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        var lines = new List<string>
        {
            $"我这次先按你本机 Codex 的当前线程来看“现在”的项目，不直接拿旧记忆硬猜。最近活跃的 Codex workspace 大约有 {overview.TotalWorkspaceCount} 个。",
            "当前活跃项目："
        };

        var rank = 1;
        foreach (var workspace in overview.Workspaces)
        {
            lines.Add(BuildCurrentWorkspaceOverviewLine(rank, workspace, knownProjects));
            rank++;
        }

        if (rememberedProjects.Length > 0)
        {
            lines.Add($"另外我还长期记着这些项目线：{string.Join("、", rememberedProjects)}。这部分是背景记忆，不等于它们现在都在活跃推进。");
        }

        lines.Add("如果你要，我下一步可以继续点开其中一个当前项目，只看它最近几条线程在做什么。");
        return string.Join("\n", lines);
    }

    private string BuildCurrentWorkspaceOverviewLine(
        int rank,
        LocalCodexThreadIndexService.CodexWorkspaceSummary workspace,
        IReadOnlyList<ProjectMemory> knownProjects)
    {
        var matchedProject = knownProjects.FirstOrDefault(project =>
                                 string.Equals(project.CodexWorkspacePath, workspace.Cwd, StringComparison.OrdinalIgnoreCase))
                             ?? knownProjects.FirstOrDefault(project =>
                                 string.Equals(project.PrimaryWorkspacePath, workspace.Cwd, StringComparison.OrdinalIgnoreCase));

        var recentThreadPart = workspace.RecentTitles.Count > 0
            ? $"最近线程是“{string.Join(" / ", workspace.RecentTitles)}”"
            : "最近主要是系统派发和巡检线程";

        if (LooksLikeDriveRootWorkspace(workspace))
        {
            return $"{rank}. {workspace.Label}：最近 {workspace.ThreadCount} 条线程，{recentThreadPart}。这条我先按目录级 workspace 看，不硬猜成别的项目名。";
        }

        if (matchedProject is null || IsSyntheticCodexProject(matchedProject))
        {
            return $"{rank}. {workspace.Label}：最近 {workspace.ThreadCount} 条线程，{recentThreadPart}。目前这条我先按 workspace 本身来认。";
        }

        var rememberedLabel = GetProjectDisplayLabel(matchedProject);
        if (string.Equals(rememberedLabel, workspace.Label, StringComparison.OrdinalIgnoreCase))
        {
            return $"{rank}. {workspace.Label}：最近 {workspace.ThreadCount} 条线程，{recentThreadPart}。";
        }

        return $"{rank}. {workspace.Label}：最近 {workspace.ThreadCount} 条线程，{recentThreadPart}。长期记忆里它对应“{rememberedLabel}”这条线。";
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

        if ((payload.ChangedFiles?.Count ?? 0) > 0)
        {
            lines.Add($"改动：{string.Join("、", payload.ChangedFiles!.Take(4))}");
        }

        if ((payload.TestsRun?.Count ?? 0) > 0)
        {
            lines.Add($"验证：{string.Join("；", payload.TestsRun!.Take(3))}");
        }

        if ((payload.Notes?.Count ?? 0) > 0)
        {
            lines.Add($"备注：{string.Join("；", payload.Notes!.Take(2))}");
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

    private static bool LooksLikeDirectorySurfaceRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var mentionsConcreteLocation = input.Contains("桌面", StringComparison.OrdinalIgnoreCase)
                                       || input.Contains("desktop", StringComparison.OrdinalIgnoreCase)
                                       || input.Contains(":\\", StringComparison.OrdinalIgnoreCase);

        if (!mentionsConcreteLocation)
        {
            return false;
        }

        return ContainsAnyIgnoreCase(
                   input,
                   "看一下",
                   "看看",
                   "看一眼",
                   "读一下",
                   "读读",
                   "扫一眼",
                   "翻一下",
                   "列一下",
                   "列出",
                   "有什么",
                   "有哪些",
                   "什么文件",
                   "哪些文件",
                   "文件",
                   "文件夹",
                   "目录",
                   "资料",
                   "内容")
               || input.ToLowerInvariant().Contains("list")
               || input.ToLowerInvariant().Contains("show");
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

        return TryResolveWorkspaceFromKnownContext(
            out workspacePath,
            forceFreshCodexSync: true,
            allowDriveRootWorkspace: true,
            allowAppRepositoryRoot: false);
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

    private bool LooksLikeCodexDispatchIntent(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.ToLowerInvariant();
        if (!normalized.Contains("codex"))
        {
            return false;
        }

        if (ContainsAnyIgnoreCase(
                input,
                "\u4ea4\u7ed9",
                "\u53bb\u505a",
                "\u5904\u7406",
                "\u6267\u884c",
                "\u5e2e\u6211",
                "\u770b\u4e00\u4e0b",
                "\u770b\u770b",
                "\u8bfb\u4e00\u4e0b",
                "\u8bfb\u8bfb",
                "\u8fc7\u4e00\u4e0b",
                "\u8fc7\u4e00\u904d",
                "\u6709\u4ec0\u4e48\u4efb\u52a1",
                "\u5728\u505a\u4ec0\u4e48",
                "\u73b0\u5728\u5728\u505a\u4ec0\u4e48",
                "\u6539",
                "\u4fee"))
        {
            return true;
        }

        return normalized.Contains("go through")
               || normalized.Contains("check codex")
               || normalized.Contains("inspect codex")
               || normalized.Contains("read codex")
               || normalized.Contains("status")
               || normalized.Contains("task")
               || normalized.Contains("tasks")
               || normalized.Contains("doing")
               || normalized.Contains("working on")
               || normalized.Contains("what is codex doing")
               || normalized.Contains("what's codex doing");
    }

    private bool TryResolveWorkspaceForCodex(string input, out string workspacePath)
    {
        if (_workspaceIngestionService.TryExtractWorkspacePath(input, out workspacePath))
        {
            return true;
        }

        return TryResolveWorkspaceFromKnownContext(
            out workspacePath,
            forceFreshCodexSync: true,
            allowDriveRootWorkspace: true,
            allowAppRepositoryRoot: true);
    }

    private static string BuildCodexTaskPrompt(string input)
    {
        if (LooksLikeCodexStatusInspectionIntent(input))
        {
            return "Read the current workspace and summarize the active workstreams in this repository. " +
                   "For each active thread, state what it appears to be doing now, the likely blocker or risk, " +
                   "and the best next step. Cite concrete file or path evidence when possible. Keep it concise.";
        }

        var prompt = input
            .Replace("\u5728 VS Code \u91cc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u5728VS Code\u91cc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u5728 vscode \u91cc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u5728vscode\u91cc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u7528 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u7528 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u4ea4\u7ed9 Codex \u53bb\u505a", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u4ea4\u7ed9 codex \u53bb\u505a", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u4ea4\u7ed9 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u4ea4\u7ed9 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8ba9 Codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8ba9 codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u5e2e\u6211", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u5904\u7406", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u6267\u884c", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u770b\u4e00\u4e0b", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u770b\u770b", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8bfb\u4e00\u4e0b", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8bfb\u8bfb", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8fc7\u4e00\u4e0b", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u8fc7\u4e00\u904d", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("codex", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\uff1a", " ", StringComparison.OrdinalIgnoreCase)
            .Replace(":", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return prompt;
    }

    private static bool LooksLikeCodexStatusInspectionIntent(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.ToLowerInvariant();
        if (!normalized.Contains("codex"))
        {
            return false;
        }

        return ContainsAnyIgnoreCase(
                   input,
                   "\u770b\u4e00\u4e0b",
                   "\u770b\u770b",
                   "\u8bfb\u4e00\u4e0b",
                   "\u8bfb\u8bfb",
                   "\u8fc7\u4e00\u4e0b",
                   "\u8fc7\u4e00\u904d",
                   "\u6709\u4ec0\u4e48\u4efb\u52a1",
                   "\u5728\u505a\u4ec0\u4e48",
                   "\u73b0\u5728\u5728\u505a\u4ec0\u4e48")
               || normalized.Contains("go through")
               || normalized.Contains("status")
               || normalized.Contains("task")
               || normalized.Contains("tasks")
               || normalized.Contains("doing")
               || normalized.Contains("working on");
    }

    private static bool ContainsAnyIgnoreCase(string input, params string[] candidates)
    {
        return candidates.Any(candidate => input.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveAppRepositoryRoot(out string workspacePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var hasGit = Directory.Exists(Path.Combine(current.FullName, ".git"));
            var hasSolution = File.Exists(Path.Combine(current.FullName, "DesktopCompanion.Windows.sln"));
            if (hasGit || hasSolution)
            {
                workspacePath = current.FullName;
                return true;
            }

            current = current.Parent;
        }

        workspacePath = string.Empty;
        return false;
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

    private static bool LooksLikeDriveRootLabel(ProjectMemory project)
    {
        return Regex.IsMatch(project.CodexWorkspaceLabel ?? string.Empty, "^[A-Za-z]:$")
               || Regex.IsMatch(project.PrimaryWorkspaceLabel ?? string.Empty, "^[A-Za-z]:$")
               || Regex.IsMatch(project.Name ?? string.Empty, "^[A-Za-z]:$");
    }

    private static bool LooksLikeDriveRootPath(string path)
    {
        var trimmed = (path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Regex.IsMatch(trimmed, "^[A-Za-z]:$");
    }

    private string GetWorkspaceDisplayLabel(string workspacePath)
    {
        var matchedProject = _projects.FirstOrDefault(project =>
                                 string.Equals(project.CodexWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase))
                             ?? _projects.FirstOrDefault(project =>
                                 string.Equals(project.PrimaryWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase));

        if (matchedProject is not null)
        {
            return GetProjectDisplayLabel(matchedProject);
        }

        var trimmed = workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var label = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(label) ? trimmed : label;
    }

    private static string GetProjectWorkspacePath(ProjectMemory project)
    {
        return !string.IsNullOrWhiteSpace(project.CodexWorkspacePath)
            ? project.CodexWorkspacePath
            : project.PrimaryWorkspacePath;
    }

    private static string GetProjectDisplayLabel(ProjectMemory project)
    {
        if (!string.IsNullOrWhiteSpace(project.CodexWorkspaceLabel))
        {
            return project.CodexWorkspaceLabel;
        }

        if (!string.IsNullOrWhiteSpace(project.PrimaryWorkspaceLabel))
        {
            return project.PrimaryWorkspaceLabel;
        }

        if (!string.IsNullOrWhiteSpace(project.Name))
        {
            return project.Name;
        }

        var path = !string.IsNullOrWhiteSpace(project.CodexWorkspacePath)
            ? project.CodexWorkspacePath
            : project.PrimaryWorkspacePath;

        if (!string.IsNullOrWhiteSpace(path))
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var label = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(label) ? trimmed : label;
        }

        return "未命名项目";
    }

    private static string GetProjectMemoryStateLabel(ProjectMemory project)
    {
        var bucketLabel = ProjectArchetypes.ToDisplayLabel(project.ArchetypeLabel);

        if (project.CodexThreadCount > 0)
        {
            return bucketLabel == "暂未定类"
                ? $"Codex {project.CodexThreadCount} 线程"
                : $"{bucketLabel} · Codex {project.CodexThreadCount} 线程";
        }

        if (bucketLabel != "暂未定类" && !string.IsNullOrWhiteSpace(project.PriorityLabel))
        {
            return $"{bucketLabel} · {project.PriorityLabel}";
        }

        if (bucketLabel != "暂未定类")
        {
            return bucketLabel;
        }

        if (!string.IsNullOrWhiteSpace(project.PriorityLabel))
        {
            return project.PriorityLabel;
        }

        return "项目线";
    }

    private static string? GetLeadProjectFocus(ProjectMemory? project)
    {
        if (project is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(project.RecentCodexThreadTitles.FirstOrDefault())
            ? project.RecentCodexThreadTitles.First()
            : !string.IsNullOrWhiteSpace(project.NextAction)
                ? project.NextAction
                : !string.IsNullOrWhiteSpace(project.CurrentMilestone)
                    ? project.CurrentMilestone
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
        string? activeWorkspaceContext,
        bool supervisionEnabled,
        bool focusSprintActive,
        CancellationToken cancellationToken = default);

    private delegate Task<ProjectCognitionDigest?> ProjectCognitionHandler(
        string userInput,
        IReadOnlyList<ProjectMemory> knownProjects,
        CancellationToken cancellationToken = default);

    private delegate Task<DistilledUserProfile?> PersonalProfileHandler(
        string analysisInput,
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
        ProjectCognitionHandler AnalyzeProjectDumpAsync,
        PersonalProfileHandler AnalyzePersonalProfileAsync);
}
