using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DesktopCompanion.WpfHost.Services;

public sealed class RepoStructureReaderService
{
    private static readonly string[] IgnoredPathFragments =
    [
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}"
    ];

    private static readonly string[] ManifestFileNames =
    [
        "package.json",
        "pnpm-workspace.yaml",
        "pnpm-workspace.yml",
        "turbo.json",
        "pyproject.toml",
        "requirements.txt",
        "go.mod",
        "Cargo.toml",
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "CMakeLists.txt"
    ];

    private static readonly string[] EntryPointFileNames =
    [
        "Program.cs",
        "App.xaml",
        "MainWindow.xaml",
        "Startup.cs",
        "main.ts",
        "main.tsx",
        "main.js",
        "main.jsx",
        "index.ts",
        "index.tsx",
        "index.js",
        "index.jsx",
        "app.py",
        "main.py",
        "server.py",
        "manage.py"
    ];

    private static readonly string[] KeyPathTokens =
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
        "src"
    ];

    public RepoStructureSnapshot ReadStructure(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
        {
            return RepoStructureSnapshot.Failure("workspace not found");
        }

        var allFiles = SafeEnumerateFiles(workspacePath)
            .Where(file => !IsIgnoredPath(file))
            .ToList();

        var topLevelEntries = SafeEnumerateTopLevelEntries(workspacePath)
            .Take(10)
            .ToArray();

        var manifestFiles = allFiles
            .Where(IsManifestFile)
            .Select(file => Path.GetRelativePath(workspacePath, file))
            .OrderBy(ScoreManifestPath)
            .Take(8)
            .ToArray();

        var entryPoints = allFiles
            .Where(IsEntryPointCandidate)
            .Select(file => Path.GetRelativePath(workspacePath, file))
            .OrderBy(ScoreEntryPointPath)
            .Take(8)
            .ToArray();

        var keyFiles = allFiles
            .Where(IsKeyFileCandidate)
            .Select(file => Path.GetRelativePath(workspacePath, file))
            .OrderBy(ScoreKeyFilePath)
            .Take(8)
            .ToArray();

        var testFiles = allFiles
            .Where(IsTestCandidate)
            .Select(file => Path.GetRelativePath(workspacePath, file))
            .OrderBy(path => path.Length)
            .Take(6)
            .ToArray();

        var projectReferences = ExtractProjectReferences(workspacePath, manifestFiles)
            .Take(8)
            .ToArray();

        if (topLevelEntries.Length == 0
            && manifestFiles.Length == 0
            && entryPoints.Length == 0
            && keyFiles.Length == 0)
        {
            return RepoStructureSnapshot.Failure("no structural signals");
        }

        var kindLabel = InferKindLabel(manifestFiles, entryPoints, topLevelEntries);
        return RepoStructureSnapshot.Success(
            kindLabel,
            topLevelEntries,
            manifestFiles,
            entryPoints,
            keyFiles,
            testFiles,
            projectReferences);
    }

    public string BuildAnalysisInput(
        WorkspaceIngestionService.WorkspaceScanResult scan,
        RepoStructureSnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            return scan.AnalysisInput;
        }

        var lines = new List<string>
        {
            $"This workspace path is: {scan.RootPath}",
            $"Workspace label: {scan.RootLabel}",
            $"Structural type: {snapshot.KindLabel}"
        };

        if (snapshot.TopLevelEntries.Count > 0)
        {
            lines.Add($"Top-level entries: {string.Join(", ", snapshot.TopLevelEntries)}");
        }

        if (snapshot.ManifestFiles.Count > 0)
        {
            lines.Add($"Project manifests: {string.Join(", ", snapshot.ManifestFiles)}");
        }

        if (snapshot.EntryPoints.Count > 0)
        {
            lines.Add($"Likely entry points: {string.Join(", ", snapshot.EntryPoints)}");
        }

        if (snapshot.ProjectReferences.Count > 0)
        {
            lines.Add($"Project references: {string.Join(" | ", snapshot.ProjectReferences)}");
        }

        if (snapshot.KeyFiles.Count > 0)
        {
            lines.Add($"Key implementation files: {string.Join(", ", snapshot.KeyFiles)}");
        }

        if (snapshot.TestFiles.Count > 0)
        {
            lines.Add($"Test-related files: {string.Join(", ", snapshot.TestFiles)}");
        }

        lines.Add("Use the repo structure above as the project skeleton. Then use the excerpts below as supporting evidence.");
        lines.AddRange(scan.Documents.Select(document => $"- {document.RelativePath}: {document.Excerpt}"));
        return string.Join("\n", lines);
    }

    public string BuildUserFacingSummary(RepoStructureSnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            return string.Empty;
        }

        var parts = new List<string> { $"先摸到它更像一个{snapshot.KindLabel}" };

        if (snapshot.EntryPoints.Count > 0)
        {
            parts.Add($"入口大概在 {string.Join("、", snapshot.EntryPoints.Take(2))}");
        }
        else if (snapshot.ManifestFiles.Count > 0)
        {
            parts.Add($"核心清单在 {string.Join("、", snapshot.ManifestFiles.Take(2))}");
        }

        if (snapshot.ProjectReferences.Count > 0)
        {
            parts.Add($"里面至少有 {snapshot.ProjectReferences.Count} 条显式项目引用");
        }

        return string.Join("，", parts);
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
                .Cast<string>()
                .OrderBy(name => name.Length);
        }
        catch
        {
            return [];
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        return IgnoredPathFragments.Any(fragment =>
            path.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsManifestFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
               || ManifestFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsEntryPointCandidate(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return EntryPointFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsKeyFileCandidate(string filePath)
    {
        var relativePath = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var extension = Path.GetExtension(filePath);
        if (extension is not (".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".py" or ".xaml"))
        {
            return false;
        }

        return KeyPathTokens.Any(token =>
            relativePath.Contains($"{Path.DirectorySeparatorChar}{token}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestCandidate(string filePath)
    {
        var relativePath = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(filePath);

        return relativePath.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreManifestPath(string relativePath)
    {
        var depth = relativePath.Count(character => character is '\\' or '/');
        var score = depth * 10;

        if (relativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            score -= 40;
        }
        else if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        return score;
    }

    private static int ScoreEntryPointPath(string relativePath)
    {
        var depth = relativePath.Count(character => character is '\\' or '/');
        var score = depth * 10;

        if (relativePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("App.xaml", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("MainWindow.xaml", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        return score;
    }

    private static int ScoreKeyFilePath(string relativePath)
    {
        var depth = relativePath.Count(character => character is '\\' or '/');
        return depth * 10 + relativePath.Length;
    }

    private static string InferKindLabel(
        IReadOnlyList<string> manifestFiles,
        IReadOnlyList<string> entryPoints,
        IReadOnlyList<string> topLevelEntries)
    {
        var hasSln = manifestFiles.Any(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        var hasCsproj = manifestFiles.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasPackageJson = manifestFiles.Any(path => path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));
        var hasPyproject = manifestFiles.Any(path => path.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase));
        var hasWpfEntry = entryPoints.Any(path =>
            path.EndsWith("App.xaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("MainWindow.xaml", StringComparison.OrdinalIgnoreCase));

        if ((hasSln || hasCsproj) && hasWpfEntry)
        {
            return ".NET/WPF 工作区";
        }

        if (hasSln || hasCsproj)
        {
            return ".NET 项目目录";
        }

        if (hasPackageJson)
        {
            return "Node/前端工作区";
        }

        if (hasPyproject)
        {
            return "Python 项目目录";
        }

        if (topLevelEntries.Any(entry => entry.Equals("src", StringComparison.OrdinalIgnoreCase)))
        {
            return "代码仓库目录";
        }

        return "混合项目目录";
    }

    private static IEnumerable<string> ExtractProjectReferences(
        string rootPath,
        IEnumerable<string> manifestFiles)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in manifestFiles.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(fullPath);
                var projectReferenceElements = document.Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference");

                foreach (var element in projectReferenceElements)
                {
                    var include = element.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    var baseDirectory = Path.GetDirectoryName(fullPath) ?? rootPath;
                    var referencedFullPath = Path.GetFullPath(Path.Combine(baseDirectory, include));
                    var referencedRelativePath = Path.GetRelativePath(rootPath, referencedFullPath);
                    references.Add($"{relativePath} -> {referencedRelativePath}");
                }
            }
            catch
            {
            }
        }

        return references.OrderBy(item => item.Length);
    }

    public sealed record RepoStructureSnapshot(
        bool IsSuccess,
        string Message,
        string KindLabel,
        IReadOnlyList<string> TopLevelEntries,
        IReadOnlyList<string> ManifestFiles,
        IReadOnlyList<string> EntryPoints,
        IReadOnlyList<string> KeyFiles,
        IReadOnlyList<string> TestFiles,
        IReadOnlyList<string> ProjectReferences)
    {
        public static RepoStructureSnapshot Failure(string message) =>
            new(false, message, string.Empty, [], [], [], [], [], []);

        public static RepoStructureSnapshot Success(
            string kindLabel,
            IReadOnlyList<string> topLevelEntries,
            IReadOnlyList<string> manifestFiles,
            IReadOnlyList<string> entryPoints,
            IReadOnlyList<string> keyFiles,
            IReadOnlyList<string> testFiles,
            IReadOnlyList<string> projectReferences) =>
            new(true, string.Empty, kindLabel, topLevelEntries, manifestFiles, entryPoints, keyFiles, testFiles, projectReferences);
    }
}
