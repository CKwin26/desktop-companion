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

    private static readonly string[] SupportedExtensions =
    [
        ".md",
        ".txt",
        ".json",
        ".csv",
        ".yaml",
        ".yml",
        ".docx",
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
            return WorkspaceScanResult.Failure("这个目录里我暂时没找到我能读的文档。先给我一个包含 README、总结或说明文档的文件夹会更稳。");
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
            return WorkspaceScanResult.Failure("这个目录里的文档我扫到了，但还没抽出稳定可读的内容。你可以先给我 README 或总结文档所在目录。");
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

    private static bool IsIgnoredPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
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

        score += extension.ToLowerInvariant() switch
        {
            ".md" => 24,
            ".docx" => 20,
            ".txt" => 16,
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
}
