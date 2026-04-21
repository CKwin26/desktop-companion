using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class PersonalDistillationService
{
    private static readonly Regex WindowsPathRegex = new(
        @"(?:""(?<path>[A-Za-z]:\\[^""]+)""|(?<path>[A-Za-z]:\\[^\r\n]+))",
        RegexOptions.Compiled);

    private static readonly string[] SupportedExtensions =
    [
        ".md",
        ".txt",
        ".json",
        ".csv",
        ".yaml",
        ".yml",
        ".docx",
        ".html",
        ".htm"
    ];

    private static readonly string[] PreferredNameTokens =
    [
        "summary",
        "overview",
        "profile",
        "work",
        "project",
        "plan",
        "notes",
        "chat",
        "wechat",
        "conversation",
        "history",
        "session",
        "export",
        "message",
        "resume",
        "cv",
        "sop",
        "portfolio"
    ];

    private static readonly string[] GenericStopTokens =
    [
        "wechat",
        "chat",
        "message",
        "messages",
        "history",
        "session",
        "export",
        "file",
        "files",
        "doc",
        "docx",
        "pdf",
        "ppt",
        "pptx",
        "xlsx",
        "xls",
        "json",
        "html",
        "txt",
        "md",
        "the",
        "and",
        "for",
        "with",
        "from",
        "bookmarks"
    ];

    private static readonly string[] FilenameCueExtensions =
    [
        ".docx",
        ".doc",
        ".pdf",
        ".pptx",
        ".ppt",
        ".xlsx",
        ".xls",
        ".txt",
        ".md",
        ".html",
        ".htm",
        ".json",
        ".csv"
    ];

    public bool LooksLikePersonalDistillationRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var hasIntent =
            input.Contains("蒸馏我", StringComparison.OrdinalIgnoreCase)
            || input.Contains("了解我", StringComparison.OrdinalIgnoreCase)
            || input.Contains("认识我", StringComparison.OrdinalIgnoreCase)
            || input.Contains("个人画像", StringComparison.OrdinalIgnoreCase)
            || input.Contains("私人资料", StringComparison.OrdinalIgnoreCase)
            || input.Contains("聊天记录", StringComparison.OrdinalIgnoreCase)
            || input.Contains("微信", StringComparison.OrdinalIgnoreCase)
            || input.Contains("wechat", StringComparison.OrdinalIgnoreCase);

        return hasIntent && ExtractSourcePaths(input).Count > 0;
    }

    public IReadOnlyList<string> ExtractSourcePaths(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WindowsPathRegex.Matches(input))
        {
            var rawPath = match.Groups["path"].Value.Trim().TrimEnd('.', '。', ',', '，', ';', '；', ')', '）');
            if (!TryResolveExistingPath(rawPath, out var resolvedPath))
            {
                continue;
            }

            resolvedPath = Path.GetFullPath(resolvedPath);
            if (seen.Add(resolvedPath))
            {
                results.Add(resolvedPath);
            }
        }

        return results;
    }

    public PersonalScanResult ScanSources(IEnumerable<string> sourcePaths)
    {
        var resolvedSources = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (resolvedSources.Count == 0)
        {
            return PersonalScanResult.Failure("这轮我还没拿到可读的个人蒸馏来源路径。");
        }

        var sourceSummaries = new List<PersonalSourceSummary>();
        var analysisSections = new List<string>
        {
            "下面是用户明确授权的个人蒸馏来源。",
            "你的任务不是复述隐私原文，而是提炼隐私安全的长期工作画像。",
            "不要输出任何邮箱、电话、地址、账号、身份证明、聊天原文或可识别联系人细节。",
            "只提炼工作风格、常见项目线、常见失速方式、最适合的支持方式。"
        };

        foreach (var sourcePath in resolvedSources)
        {
            if (!TryResolveExistingPath(sourcePath, out var resolvedPath))
            {
                continue;
            }

            var files = SafeEnumerateFiles(resolvedPath, 12000).ToList();
            var topEntries = SafeEnumerateTopLevelEntries(resolvedPath).Take(8).ToList();
            var extensionSummary = files
                .GroupBy(file => string.IsNullOrWhiteSpace(Path.GetExtension(file)) ? "[no extension]" : Path.GetExtension(file).ToLowerInvariant())
                .OrderByDescending(group => group.Count())
                .Take(10)
                .Select(group => $"{group.Key}:{group.Count()}")
                .ToList();

            var readableFiles = files
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Where(file => !IsIgnoredPath(file))
                .OrderByDescending(file => ScoreFile(file, resolvedPath))
                .Take(8)
                .ToList();

            var representativeFileNames = files
                .Where(file => FilenameCueExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Where(file => !IsIgnoredPath(file))
                .OrderByDescending(file => ScoreFile(file, resolvedPath))
                .ThenByDescending(GetLastWriteTicks)
                .Take(8)
                .Select(file => Path.GetFileName(file))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var topicKeywords = ExtractTopicKeywords(
                representativeFileNames,
                topEntries,
                readableFiles.Select(Path.GetFileName));

            var documents = new List<PersonalDocumentSnippet>();
            foreach (var file in readableFiles)
            {
                var content = TryReadDocument(file);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                var excerpt = BuildExcerpt(content, 420);
                if (string.IsNullOrWhiteSpace(excerpt))
                {
                    continue;
                }

                documents.Add(new PersonalDocumentSnippet(
                    Path.GetRelativePath(resolvedPath, file),
                    Path.GetFileName(file),
                    excerpt));

                if (documents.Count >= 5)
                {
                    break;
                }
            }

            var sourceLabel = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            sourceSummaries.Add(new PersonalSourceSummary(
                resolvedPath,
                sourceLabel,
                topEntries,
                extensionSummary,
                representativeFileNames,
                topicKeywords,
                documents));

            analysisSections.Add($"来源：{resolvedPath}");
            analysisSections.Add(IsLikelyWeChatSource(resolvedPath, topEntries)
                ? "来源判断：这看起来像微信聊天资料源，结构和文件名比正文更重要。"
                : "来源判断：这更像普通资料目录。");
            analysisSections.Add(topEntries.Count == 0
                ? "顶层结构：无明显顶层条目。"
                : $"顶层结构：{string.Join("、", topEntries)}");
            analysisSections.Add(extensionSummary.Count == 0
                ? "文件类型分布：无法统计。"
                : $"文件类型分布：{string.Join("；", extensionSummary)}");
            analysisSections.Add(representativeFileNames.Count == 0
                ? "代表性文件名线索：暂无。"
                : $"代表性文件名线索：{string.Join("；", representativeFileNames)}");
            analysisSections.Add(topicKeywords.Count == 0
                ? "主题关键词线索：暂无。"
                : $"主题关键词线索：{string.Join("、", topicKeywords)}");

            if (documents.Count == 0)
            {
                analysisSections.Add("可读文档摘录：这一来源里没有稳定可读的文本导出，只能参考结构和文件类型。");
            }
            else
            {
                analysisSections.Add("可读文档摘录：");
                analysisSections.AddRange(documents.Select(document => $"- {document.RelativePath}: {document.Excerpt}"));
            }
        }

        if (sourceSummaries.Count == 0)
        {
            return PersonalScanResult.Failure("这些路径我能看到，但这轮还没稳定抽出可用于蒸馏的来源。");
        }

        return PersonalScanResult.Success(
            sourceSummaries,
            string.Join("\n", analysisSections));
    }

    public DistilledUserProfile CreateFallbackProfile(PersonalScanResult scan)
    {
        var sourceLabels = scan.Sources.Select(source => source.SourceLabel).Distinct().Take(8).ToList();
        var normalizedTokens = sourceLabels
            .Concat(scan.Sources.SelectMany(source => source.TopEntries))
            .Concat(scan.Sources.SelectMany(source => source.RepresentativeFileNames))
            .Concat(scan.Sources.SelectMany(source => source.TopicKeywords))
            .SelectMany(label => label.Split([' ', '-', '_', '(', ')', '（', '）', '.', ':'], StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();

        var knownLanes = new List<string>();
        if (ContainsAny(normalizedTokens, "wechat", "微信"))
        {
            knownLanes.Add("聊天里推进的协作与任务承诺");
        }

        if (ContainsAny(normalizedTokens, "pitch", "proposal", "patent", "商业", "样品", "专利"))
        {
            knownLanes.Add("商业材料、专利与样品推进");
        }

        if (ContainsAny(normalizedTokens, "cv", "resume", "sop", "portfolio", "申请", "文书"))
        {
            knownLanes.Add("申请材料、经历叙事与个人归档");
        }

        if (ContainsAny(normalizedTokens, "desktop", "Desktop"))
        {
            knownLanes.Add("桌面上并行推进的多个项目线");
        }

        if (ContainsAny(normalizedTokens, "phdapply", "申请", "research"))
        {
            knownLanes.Add("申请材料、研究叙事与项目归档");
        }

        if (ContainsAny(normalizedTokens, "EEG", "ML", "project", "mainland"))
        {
            knownLanes.Add("研究、产品原型和技术实现并行推进");
        }

        if (knownLanes.Count == 0)
        {
            knownLanes.Add("多个并行工作主线");
        }

        return new DistilledUserProfile
        {
            Summary = $"这是一份从 {string.Join("、", sourceLabels)} 提炼出的隐私安全工作画像。用户经常在多个项目线之间切换，很多上下文散落在聊天记录、桌面资料和项目文件夹里。",
            StableTraits =
            [
                "这是一个会同时推进多个任务主线的人。",
                "她的重要上下文经常分散在聊天、桌面资料和项目目录里。",
                "她更需要被帮忙认出当前有哪些主线，而不是再被塞进一个单线程待办系统。"
            ],
            KnownWorkLanes = knownLanes,
            LikelyFailureModes =
            [
                "重要承诺和细节留在聊天里，后面容易丢上下文。",
                "多个主线同时推进时，顺位会短暂失真。",
                "如果没人替她把混合信息重新归线，她会感觉所有事情同时在抢注意力。"
            ],
            BestSupportStyle =
            [
                "先按主线归并，再决定优先级。",
                "遇到聊天来源时，提炼承诺和下一步，不复述私密原话。",
                "把下一步绑定到具体主线，而不是给一个泛泛的待办。"
            ],
            SourceLabels = sourceLabels,
            PrivacyBoundaries =
            [
                "不保留原始聊天内容。",
                "不保留联系人实名、邮箱、电话、地址、账号等敏感信息。",
                "只保留长期稳定的工作风格、主线结构、卡点模式和支持偏好。"
            ],
            UpdatedAt = DateTimeOffset.Now
        };
    }

    public string BuildCompanionReply(DistilledUserProfile profile, PersonalScanResult scan)
    {
        var sourceNames = string.Join("、", scan.Sources.Select(source => source.SourceLabel).Take(4));
        var laneNames = profile.KnownWorkLanes.Count == 0
            ? "多条并行主线"
            : string.Join("、", profile.KnownWorkLanes.Take(3));

        return $"我先从 {sourceNames} 这些你明确授权的来源里，提了一版隐私安全画像。以后我会更偏向把你的信息理解成 {laneNames} 这几路主线来接，不去长期记原始聊天内容。";
    }

    public static bool TryDeserializeProfile(string rawText, out DistilledUserProfile? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        try
        {
            profile = JsonSerializer.Deserialize<DistilledUserProfile>(rawText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (profile is null)
            {
                return false;
            }

            profile.StableTraits = NormalizeStrings(profile.StableTraits, 6);
            profile.KnownWorkLanes = NormalizeStrings(profile.KnownWorkLanes, 8);
            profile.LikelyFailureModes = NormalizeStrings(profile.LikelyFailureModes, 6);
            profile.BestSupportStyle = NormalizeStrings(profile.BestSupportStyle, 6);
            profile.SourceLabels = NormalizeStrings(profile.SourceLabels, 8);
            profile.PrivacyBoundaries = NormalizeStrings(profile.PrivacyBoundaries, 6);
            profile.Summary = (profile.Summary ?? string.Empty).Trim();
            profile.UpdatedAt = DateTimeOffset.Now;

            return !string.IsNullOrWhiteSpace(profile.Summary)
                   || profile.StableTraits.Count > 0
                   || profile.KnownWorkLanes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> NormalizeStrings(List<string>? values, int maxCount)
    {
        return (values ?? [])
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Cast<string>()
            .ToList();
    }

    private static bool ContainsAny(IEnumerable<string> values, params string[] targets)
    {
        return values.Any(value => targets.Any(target => value.Contains(target, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryResolveExistingPath(string candidatePath, out string existingPath)
    {
        existingPath = string.Empty;

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(candidatePath.Trim().Trim('"'));
            if (File.Exists(expandedPath))
            {
                existingPath = Path.GetDirectoryName(expandedPath) ?? expandedPath;
                return true;
            }

            if (Directory.Exists(expandedPath))
            {
                existingPath = expandedPath;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath, int maxFiles)
    {
        try
        {
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).Take(maxFiles);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateTopLevelEntries(string rootPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(rootPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}temp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreFile(string filePath, string rootPath)
    {
        var score = 0;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var depth = relativePath.Count(character => character is '\\' or '/');

        foreach (var token in PreferredNameTokens)
        {
            if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 16;
            }

            if (relativePath.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
        }

        score += extension.ToLowerInvariant() switch
        {
            ".md" => 22,
            ".docx" => 20,
            ".txt" => 18,
            ".json" => 14,
            ".csv" => 10,
            ".html" => 12,
            ".htm" => 12,
            _ => 4
        };

        score -= depth * 2;
        score += ScoreRelativePath(relativePath);
        score += ScoreRecency(filePath);
        return score;
    }

    private static string TryReadDocument(string filePath)
    {
        try
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".docx" => ReadDocx(filePath),
                ".json" => ReadJson(filePath),
                ".html" => ReadHtml(filePath),
                ".htm" => ReadHtml(filePath),
                _ => File.ReadAllText(filePath)
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadDocx(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace namespaceValue = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var texts = document.Descendants(namespaceValue + "t")
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" ", texts);
    }

    private static string ReadJson(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var document = JsonDocument.Parse(stream);
        var values = new List<string>();
        CollectJsonStrings(document.RootElement, values, 0);
        return string.Join(" ", values.Take(120));
    }

    private static string ReadHtml(string filePath)
    {
        var html = File.ReadAllText(filePath);
        html = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return html;
    }

    private static string BuildExcerpt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        return $"{cleaned[..maxLength].Trim()}...";
    }

    private static void CollectJsonStrings(JsonElement element, List<string> values, int depth)
    {
        if (values.Count >= 120 || depth > 8)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = element.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length >= 3)
                {
                    values.Add(text);
                }

                break;
            }
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (values.Count >= 120)
                    {
                        break;
                    }

                    CollectJsonStrings(property.Value, values, depth + 1);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (values.Count >= 120)
                    {
                        break;
                    }

                    CollectJsonStrings(item, values, depth + 1);
                }

                break;
        }
    }

    private static int ScoreRelativePath(string relativePath)
    {
        var score = 0;

        if (relativePath.Contains("msg\\file", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("msg/file", StringComparison.OrdinalIgnoreCase))
        {
            score += 16;
        }

        if (relativePath.Contains("wechat", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (relativePath.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        return score;
    }

    private static int ScoreRecency(string filePath)
    {
        try
        {
            var age = DateTimeOffset.Now - File.GetLastWriteTimeUtc(filePath);
            if (age.TotalDays <= 30)
            {
                return 12;
            }

            if (age.TotalDays <= 180)
            {
                return 8;
            }

            if (age.TotalDays <= 365)
            {
                return 4;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static long GetLastWriteTicks(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsLikelyWeChatSource(string resolvedPath, IReadOnlyList<string> topEntries)
    {
        return resolvedPath.Contains("wechat", StringComparison.OrdinalIgnoreCase)
               || resolvedPath.Contains("wxid_", StringComparison.OrdinalIgnoreCase)
               || topEntries.Any(entry => entry.Equals("msg", StringComparison.OrdinalIgnoreCase))
               || topEntries.Any(entry => entry.Equals("db_storage", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ExtractTopicKeywords(
        IEnumerable<string?> representativeFileNames,
        IEnumerable<string?> topEntries,
        IEnumerable<string?> additionalNames)
    {
        var tokens = representativeFileNames
            .Concat(topEntries)
            .Concat(additionalNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(SplitIntoTopicTokens)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 2)
            .Where(token => !Regex.IsMatch(token, @"^\d+$"))
            .Where(token => !GenericStopTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Length)
            .Take(12)
            .Select(group => group.Key)
            .ToList();

        return tokens;
    }

    private static IEnumerable<string> SplitIntoTopicTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return Regex.Matches(value, @"[\p{IsCJKUnifiedIdeographs}]{2,}|[A-Za-z][A-Za-z0-9+#-]{2,}")
            .Select(match => match.Value);
    }

    public sealed record PersonalDocumentSnippet(
        string RelativePath,
        string FileName,
        string Excerpt);

    public sealed record PersonalSourceSummary(
        string RootPath,
        string SourceLabel,
        IReadOnlyList<string> TopEntries,
        IReadOnlyList<string> ExtensionSummary,
        IReadOnlyList<string> RepresentativeFileNames,
        IReadOnlyList<string> TopicKeywords,
        IReadOnlyList<PersonalDocumentSnippet> Documents);

    public sealed record PersonalScanResult(
        bool IsSuccess,
        string Message,
        IReadOnlyList<PersonalSourceSummary> Sources,
        string AnalysisInput)
    {
        public static PersonalScanResult Failure(string message) =>
            new(false, message, [], string.Empty);

        public static PersonalScanResult Success(
            IReadOnlyList<PersonalSourceSummary> sources,
            string analysisInput) =>
            new(true, string.Empty, sources, analysisInput);
    }
}
