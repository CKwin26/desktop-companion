using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DesktopCompanion.WpfHost.Services;

public sealed class WorkspaceIngestionService
{
    private static readonly Regex WindowsPathRegex = new(
        @"(?:""(?<path>[A-Za-z]:\\[^""]+)""|(?<path>[A-Za-z]:\\[^\r\n]+))",
        RegexOptions.Compiled);

    private static readonly Regex DesktopSubpathRegex = new(
        @"(?:(?:~|%USERPROFILE%)[/\\]Desktop|Desktop|桌面)[/\\](?<tail>[^""'\s，。；;,]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] SupportedExtensions =
    [
        ".md",
        ".txt",
        ".json",
        ".toml",
        ".csv",
        ".yaml",
        ".yml",
        ".docx",
        ".sln",
        ".csproj",
        ".fsproj",
        ".vbproj",
        ".cs",
        ".py",
        ".ts",
        ".tsx",
        ".js",
        ".jsx",
        ".html",
        ".css",
        ".xml"
    ];

    private static readonly string[] PreferredNameTokens =
    [
        "readme",
        "summary",
        "overview",
        "project",
        "portfolio",
        "work",
        "notes",
        "report",
        "plan",
        "pitch",
        "patent",
        "proposal",
        "sop",
        "cv"
    ];

    private static readonly string[] StructuralFileNames =
    [
        "Program.cs",
        "App.xaml",
        "MainWindow.xaml",
        "Startup.cs",
        "package.json",
        "pyproject.toml",
        "requirements.txt",
        "Cargo.toml",
        "go.mod",
        "pom.xml",
        "CMakeLists.txt"
    ];

    private static readonly string[] StructuralPathTokens =
    [
        "services",
        "viewmodels",
        "views",
        "controllers",
        "routes",
        "api",
        "backend",
        "frontend",
        "pages",
        "components",
        "src",
        "tests",
        "test"
    ];

    private static readonly string[] DesktopAliasHints =
    [
        "桌面文件",
        "桌面资料",
        "桌面目录",
        "桌面文件夹",
        "桌面上的",
        "桌面上",
        "桌面里",
        "桌面内容",
        "desktop files",
        "desktop file",
        "desktop folder",
        "desktop directory",
        "on desktop",
        "from desktop"
    ];

    public bool TryExtractWorkspacePath(string input, out string workspacePath)
    {
        workspacePath = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var matches = WindowsPathRegex.Matches(input);
        foreach (Match match in matches)
        {
            var rawPath = match.Groups["path"].Value.Trim().TrimEnd('.', '。', ',', '，', ';', '；', ')', '）');
            if (TryResolveWorkspacePath(rawPath, out workspacePath))
            {
                return true;
            }
        }

        foreach (var candidatePath in BuildDesktopSubpathCandidates(input))
        {
            if (TryResolveWorkspacePath(candidatePath, out workspacePath))
            {
                return true;
            }
        }

        if (TryResolveDesktopAlias(input, out workspacePath))
        {
            return true;
        }

        return false;
    }

    public WorkspaceScanResult ScanWorkspace(string workspacePath)
    {
        if (!TryResolveWorkspacePath(workspacePath, out var resolvedPath))
        {
            return WorkspaceScanResult.Failure("这个地址我没找到，你可以把文件夹完整路径再发我一次。");
        }

        var allFiles = SafeEnumerateFiles(resolvedPath)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !IsIgnoredPath(file))
            .ToList();

        if (allFiles.Count == 0)
        {
            return WorkspaceScanResult.Failure("这个目录里我暂时没找到我能读的资料或代码骨架。你给我一个项目目录、代码目录，或带 README 的目录都可以。");
        }

        var selectedFiles = allFiles
            .OrderByDescending(file => ScoreFile(file, resolvedPath))
            .ThenBy(file => file.Length)
            .Take(18)
            .ToList();

        var documents = new List<WorkspaceDocumentSnippet>();
        var totalExtractedCharacters = 0;

        foreach (var file in selectedFiles)
        {
            var text = TryReadDocument(file);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var excerpt = BuildExcerpt(text, 520);
            if (string.IsNullOrWhiteSpace(excerpt))
            {
                continue;
            }

            totalExtractedCharacters += excerpt.Length;
            documents.Add(new WorkspaceDocumentSnippet(
                Path.GetRelativePath(resolvedPath, file),
                Path.GetFileName(file),
                excerpt));

            if (documents.Count >= 12 || totalExtractedCharacters >= 5200)
            {
                break;
            }
        }

        if (documents.Count == 0)
        {
            return WorkspaceScanResult.Failure("这个目录我扫到了，但这轮还没抽出稳定可读的文档或代码片段。你可以先给我更靠近入口文件、主代码目录或 README 的那一层。");
        }

        var rootLabel = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var topLevelEntries = SafeEnumerateTopLevelEntries(resolvedPath).Take(8).ToList();
        var folderSummary = topLevelEntries.Count == 0
            ? "没有明显的子目录结构。"
            : $"顶层结构包括：{string.Join("、", topLevelEntries)}。";

        var analysisInput = string.Join(
            "\n",
            [
                $"这是一个项目目录：{resolvedPath}",
                $"目录名：{rootLabel}",
                folderSummary,
                "下面是我从目录里抽出的文档摘要，请把它们当作项目资料来梳理，识别正在推进的项目线、优先级和下一步：",
                ..documents.Select(document => $"- {document.RelativePath}: {document.Excerpt}")
            ]);

        return WorkspaceScanResult.Success(resolvedPath, rootLabel, documents, analysisInput);
    }

    public DirectorySurfaceResult DescribeDirectorySurface(string workspacePath)
    {
        if (!TryResolveWorkspacePath(workspacePath, out var resolvedPath))
        {
            return DirectorySurfaceResult.Failure("这个目录我没找到，你把完整路径再发我一次。");
        }

        var visibleEntries = SafeEnumerateVisibleTopLevelEntries(resolvedPath).ToList();
        var directories = visibleEntries
            .Where(Directory.Exists)
            .Select(GetSurfaceEntryLabel)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        var files = visibleEntries
            .Where(File.Exists)
            .Select(GetSurfaceEntryLabel)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        if (directories.Count == 0 && files.Count == 0)
        {
            return DirectorySurfaceResult.Failure("这个目录我看到了，但顶层暂时没扫到明显可见的文件或文件夹。");
        }

        var rootLabel = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootLabel))
        {
            rootLabel = resolvedPath;
        }

        return DirectorySurfaceResult.Success(resolvedPath, rootLabel, directories, files);
    }

    private static bool TryResolveWorkspacePath(string rawPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
            if (!TryResolveExistingPath(expandedPath, out var existingPath))
            {
                return false;
            }

            resolvedPath = Path.GetFullPath(existingPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveDesktopAlias(string input, out string workspacePath)
    {
        workspacePath = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var mentionsDesktop = input.Contains("桌面", StringComparison.OrdinalIgnoreCase)
                              || input.Contains("desktop", StringComparison.OrdinalIgnoreCase);

        if (!mentionsDesktop)
        {
            return false;
        }

        var looksLikeDesktopDataRequest = DesktopAliasHints.Any(hint =>
                input.Contains(hint, StringComparison.OrdinalIgnoreCase))
            || ContainsAnyIgnoreCase(
                input,
                "看看",
                "看一下",
                "看一眼",
                "读一下",
                "读读",
                "翻一下",
                "扫一眼",
                "列一下",
                "列出",
                "有什么",
                "有哪些",
                "文件",
                "文件夹",
                "目录",
                "资料",
                "内容");

        if (!looksLikeDesktopDataRequest)
        {
            return false;
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (TryResolveWorkspacePath(desktopPath, out workspacePath))
        {
            return true;
        }

        var fallbackDesktopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop");

        return TryResolveWorkspacePath(fallbackDesktopPath, out workspacePath);
    }

    private static IEnumerable<string> BuildDesktopSubpathCandidates(string input)
    {
        foreach (Match match in DesktopSubpathRegex.Matches(input))
        {
            var tail = match.Groups["tail"].Value.Trim().TrimEnd('.', '。', ',', '，', ';', '；', ')', '）');
            if (string.IsNullOrWhiteSpace(tail))
            {
                continue;
            }

            foreach (var desktopPath in GetDesktopPathCandidates())
            {
                yield return Path.Combine(desktopPath, tail.Replace('/', Path.DirectorySeparatorChar));
            }
        }
    }

    private static IEnumerable<string> GetDesktopPathCandidates()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktopPath))
        {
            yield return desktopPath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, "Desktop");
        }
    }

    private static bool TryResolveExistingPath(string candidatePath, out string existingPath)
    {
        existingPath = string.Empty;

        if (File.Exists(candidatePath))
        {
            existingPath = Path.GetDirectoryName(candidatePath) ?? candidatePath;
            return true;
        }

        if (Directory.Exists(candidatePath))
        {
            existingPath = candidatePath;
            return true;
        }

        for (var index = candidatePath.Length - 1; index >= 0; index--)
        {
            var character = candidatePath[index];
            if (!char.IsWhiteSpace(character) && character is not '。' and not '，' and not '；' and not ',' and not ';' and not ')' and not '）')
            {
                continue;
            }

            var shortened = candidatePath[..index].TrimEnd('.', '。', ',', '，', ';', '；', ')', '）', ' ');
            if (string.IsNullOrWhiteSpace(shortened))
            {
                continue;
            }

            if (File.Exists(shortened))
            {
                existingPath = Path.GetDirectoryName(shortened) ?? shortened;
                return true;
            }

            if (Directory.Exists(shortened))
            {
                existingPath = shortened;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
    {
        try
        {
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
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

    private static IEnumerable<string> SafeEnumerateVisibleTopLevelEntries(string rootPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(rootPath)
                .Where(path => !IsHiddenOrSystemEntry(path))
                .OrderBy(path => Directory.Exists(path) ? 0 : 1)
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
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
               || path.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHiddenOrSystemEntry(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Hidden) != 0
                   || (attributes & FileAttributes.System) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetSurfaceEntryLabel(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        return Path.GetFileName(path);
    }

    private static bool ContainsAnyIgnoreCase(string input, params string[] candidates)
    {
        return candidates.Any(candidate => input.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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
                score += 18;
            }

            if (relativePath.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
        }

        if (StructuralFileNames.Contains(Path.GetFileName(filePath), StringComparer.OrdinalIgnoreCase))
        {
            score += 36;
        }

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".toml", StringComparison.OrdinalIgnoreCase))
        {
            score += 26;
        }

        if (StructuralPathTokens.Any(token =>
                relativePath.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            score += 12;
        }

        if (depth <= 1)
        {
            score += 8;
        }

        score += extension.ToLowerInvariant() switch
        {
            ".md" => 24,
            ".docx" => 20,
            ".txt" => 16,
            ".sln" => 20,
            ".csproj" => 18,
            ".toml" => 14,
            ".json" => 10,
            ".csv" => 8,
            _ => 4
        };

        score -= depth * 3;

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 1_500_000)
            {
                score -= 18;
            }
            else if (fileInfo.Length < 250_000)
            {
                score += 6;
            }
        }
        catch
        {
        }

        return score;
    }

    private static string TryReadDocument(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".docx" => ReadDocx(filePath),
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

    public sealed record WorkspaceDocumentSnippet(
        string RelativePath,
        string FileName,
        string Excerpt);

    public sealed record WorkspaceScanResult(
        bool IsSuccess,
        string Message,
        string RootPath,
        string RootLabel,
        IReadOnlyList<WorkspaceDocumentSnippet> Documents,
        string AnalysisInput)
    {
        public static WorkspaceScanResult Failure(string message) =>
            new(false, message, string.Empty, string.Empty, [], string.Empty);

        public static WorkspaceScanResult Success(
            string rootPath,
            string rootLabel,
            IReadOnlyList<WorkspaceDocumentSnippet> documents,
            string analysisInput) =>
            new(true, string.Empty, rootPath, rootLabel, documents, analysisInput);
    }

    public sealed record DirectorySurfaceResult(
        bool IsSuccess,
        string Message,
        string RootPath,
        string RootLabel,
        IReadOnlyList<string> Directories,
        IReadOnlyList<string> Files)
    {
        public static DirectorySurfaceResult Failure(string message) =>
            new(false, message, string.Empty, string.Empty, [], []);

        public static DirectorySurfaceResult Success(
            string rootPath,
            string rootLabel,
            IReadOnlyList<string> directories,
            IReadOnlyList<string> files) =>
            new(true, string.Empty, rootPath, rootLabel, directories, files);
    }
}
