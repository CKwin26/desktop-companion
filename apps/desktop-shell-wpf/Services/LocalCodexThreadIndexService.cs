using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalCodexThreadIndexService
{
    private static readonly string[] InternalCwdFragments =
    [
        Path.DirectorySeparatorChar + ".codex" + Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar + ".codex" + Path.AltDirectorySeparatorChar
    ];

    private readonly string _stateDbPath;

    public LocalCodexThreadIndexService(string? stateDbPath = null)
    {
        _stateDbPath = stateDbPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "state_5.sqlite");
    }

    public CodexWorkspaceOverviewResult BuildOverview(int maxProjects = 6)
    {
        if (!File.Exists(_stateDbPath))
        {
            return CodexWorkspaceOverviewResult.Failure("我这边没找到本机 Codex 的线程索引，所以现在还不能做真正的项目总览。");
        }

        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _stateDbPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString());

            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT cwd, title, updated_at_ms, archived
                FROM threads
                WHERE archived = 0
                  AND cwd IS NOT NULL
                  AND cwd != ''
                ORDER BY updated_at_ms DESC
                LIMIT 400
                """;

            using var reader = command.ExecuteReader();
            var rows = new List<CodexThreadRow>();

            while (reader.Read())
            {
                var cwd = NormalizeCwd(reader.GetString(0));
                if (string.IsNullOrWhiteSpace(cwd) || IsInternalCwd(cwd))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var updatedAtMs = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                rows.Add(new CodexThreadRow(cwd, title, updatedAtMs));
            }

            if (rows.Count == 0)
            {
                return CodexWorkspaceOverviewResult.Failure("我读到了 Codex 索引，但里面暂时没有可用的本地项目线程。");
            }

            var workspaces = rows
                .GroupBy(row => row.Cwd, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var orderedRows = group
                        .OrderByDescending(item => item.UpdatedAtMs)
                        .ToList();

                    var representativeTitles = orderedRows
                        .Select(item => CondenseTitle(item.Title))
                        .Where(title => !string.IsNullOrWhiteSpace(title))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToArray();

                    return new CodexWorkspaceSummary(
                        group.Key,
                        BuildWorkspaceLabel(group.Key),
                        orderedRows.Count,
                        DateTimeOffset.FromUnixTimeMilliseconds(orderedRows[0].UpdatedAtMs),
                        representativeTitles);
                })
                .OrderByDescending(item => item.LastUpdatedAt)
                .ThenByDescending(item => item.ThreadCount)
                .Take(maxProjects)
                .ToArray();

            return CodexWorkspaceOverviewResult.Success(workspaces, rows.Select(row => row.Cwd).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        catch (Exception exception)
        {
            return CodexWorkspaceOverviewResult.Failure($"我去读本机 Codex 线程索引了，但这轮没读稳：{exception.Message}");
        }
    }

    private static bool IsInternalCwd(string cwd)
    {
        return InternalCwdFragments.Any(fragment => cwd.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeCwd(string cwd)
    {
        return cwd.StartsWith(@"\\?\")
            ? cwd[4..]
            : cwd;
    }

    private static string BuildWorkspaceLabel(string cwd)
    {
        var trimmed = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var label = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        var root = Path.GetPathRoot(trimmed);
        return string.IsNullOrWhiteSpace(root)
            ? trimmed
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string CondenseTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var title = rawTitle.Trim();
        var userRequestIndex = title.IndexOf("User request:", StringComparison.OrdinalIgnoreCase);
        if (userRequestIndex >= 0)
        {
            title = title[(userRequestIndex + "User request:".Length)..].Trim();
        }

        title = title
            .Replace("\r", " ")
            .Replace("\n", " ");

        title = Regex.Replace(title, "\\s+", " ").Trim();

        if (title.StartsWith("You are being invoked by a desktop companion", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("Return JSON with summary READY", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("## Memory Writing Agent", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return title.Length <= 90 ? title : $"{title[..90].Trim()}...";
    }

    private sealed record CodexThreadRow(string Cwd, string Title, long UpdatedAtMs);

    public sealed record CodexWorkspaceSummary(
        string Cwd,
        string Label,
        int ThreadCount,
        DateTimeOffset LastUpdatedAt,
        IReadOnlyList<string> RecentTitles);

    public sealed record CodexWorkspaceOverviewResult(
        bool IsSuccess,
        string Message,
        IReadOnlyList<CodexWorkspaceSummary> Workspaces,
        int TotalWorkspaceCount)
    {
        public static CodexWorkspaceOverviewResult Success(
            IReadOnlyList<CodexWorkspaceSummary> workspaces,
            int totalWorkspaceCount) =>
            new(true, string.Empty, workspaces, totalWorkspaceCount);

        public static CodexWorkspaceOverviewResult Failure(string message) =>
            new(false, message, Array.Empty<CodexWorkspaceSummary>(), 0);
    }
}
