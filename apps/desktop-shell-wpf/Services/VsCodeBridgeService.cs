using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DesktopCompanion.WpfHost.Services;

public sealed class VsCodeBridgeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _configuredCodePath;
    private readonly string? _configuredCodexPath;

    public VsCodeBridgeService(string? codePath = null, string? codexPath = null)
    {
        _configuredCodePath = codePath;
        _configuredCodexPath = codexPath;
    }

    public bool CanOpenVsCode => !string.IsNullOrWhiteSpace(ResolveCodePath());

    public bool CanRunCodex => EnumerateCodexCandidates().Any();

    public bool TryOpenWorkspace(string workspacePath, out string message)
    {
        message = string.Empty;

        if (!Directory.Exists(workspacePath))
        {
            message = "这个目录我没找到，所以还没法替你在 VS Code 里打开。";
            return false;
        }

        var codePath = ResolveCodePath();
        if (string.IsNullOrWhiteSpace(codePath))
        {
            message = "我这边还没找到 VS Code 的可执行文件，所以暂时打不开。";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = codePath,
                Arguments = $"\"{workspacePath}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
            var label = Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            message = $"我已经把 {label} 打开到 VS Code 里了。";
            return true;
        }
        catch
        {
            message = "我试着替你打开 VS Code 了，但这次没有成功。";
            return false;
        }
    }

    public string DescribeCodexBackends()
    {
        var candidates = EnumerateCodexCandidates().ToList();
        if (candidates.Count == 0)
        {
            return "暂时没找到可用的 codex exec 后端。";
        }

        return string.Join(
            Environment.NewLine,
            candidates.Select(candidate => $"{candidate.SourceLabel}: {candidate.Executable}"));
    }

    public async Task<CodexDispatchResult> RunCodexTaskAsync(
        string workspacePath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workspacePath))
        {
            return CodexDispatchResult.Failure("这个目录我没找到，所以还没法把任务交给 Codex。");
        }

        var candidates = EnumerateCodexCandidates().ToList();
        if (candidates.Count == 0)
        {
            return CodexDispatchResult.Failure("我这边还没找到可用的 codex exec，所以暂时没法替你派活。");
        }

        var bridgeDirectory = Path.Combine(workspacePath, ".codex-bridge");
        Directory.CreateDirectory(bridgeDirectory);

        var outputFile = Path.Combine(bridgeDirectory, $"out-{Guid.NewGuid():N}.json");
        var schemaFile = Path.Combine(bridgeDirectory, $"schema-{Guid.NewGuid():N}.json");
        var failures = new List<string>();

        try
        {
            await WriteSchemaFileAsync(schemaFile, cancellationToken);

            foreach (var candidate in candidates)
            {
                var result = await TryRunCodexTaskWithCandidateAsync(
                    candidate,
                    workspacePath,
                    prompt,
                    schemaFile,
                    outputFile,
                    cancellationToken);

                if (result.Outcome is CodexCandidateOutcome.Success or CodexCandidateOutcome.Timeout)
                {
                    return result.Result!;
                }

                if (!string.IsNullOrWhiteSpace(result.FailureMessage))
                {
                    failures.Add($"{candidate.SourceLabel}: {result.FailureMessage}");
                }

                TryDeleteFile(outputFile);
            }
        }
        catch (OperationCanceledException)
        {
            return CodexDispatchResult.Timeout("这次交给 Codex 的任务超时了，我已经先把它收掉。你可以把任务再缩小一点重试。");
        }
        catch (Exception exception)
        {
            return CodexDispatchResult.Failure($"这次我把任务交给 Codex 时出了点问题：{exception.Message}");
        }
        finally
        {
            TryDeleteFile(outputFile);
            TryDeleteFile(schemaFile);
        }

        return CodexDispatchResult.Failure(
            failures.Count == 0
                ? "Codex 这次没顺利跑完。"
                : string.Join(Environment.NewLine, failures));
    }

    private string? ResolveCodePath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredCodePath) && File.Exists(_configuredCodePath))
        {
            return _configuredCodePath;
        }

        var envPath = Environment.GetEnvironmentVariable("DESKTOP_COMPANION_CODE_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe");

        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private IReadOnlyList<CodexExecutableCandidate> EnumerateCodexCandidates()
    {
        var candidates = new List<CodexExecutableCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? executable, string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                return;
            }

            var normalized = executable.Trim();
            if (!seen.Add(normalized))
            {
                return;
            }

            candidates.Add(new CodexExecutableCandidate(normalized, sourceLabel));
        }

        if (!string.IsNullOrWhiteSpace(_configuredCodexPath) && File.Exists(_configuredCodexPath))
        {
            AddCandidate(_configuredCodexPath, "configured path");
        }

        var envPath = Environment.GetEnvironmentVariable("DESKTOP_COMPANION_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            AddCandidate(envPath, "DESKTOP_COMPANION_CODEX_PATH");
        }

        var extensionRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode",
            "extensions");

        if (Directory.Exists(extensionRoot))
        {
            foreach (var extensionCodexPath in Directory.EnumerateFiles(extensionRoot, "codex.exe", SearchOption.AllDirectories)
                         .Where(path => path.Contains("openai.chatgpt", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(path => path))
            {
                AddCandidate(extensionCodexPath, "VS Code extension codex");
            }
        }

        AddCandidate("codex", "PATH codex");

        return candidates;
    }

    private async Task<CodexCandidateRunResult> TryRunCodexTaskWithCandidateAsync(
        CodexExecutableCandidate candidate,
        string workspacePath,
        string prompt,
        string schemaFile,
        string outputFile,
        CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = candidate.Executable,
                Arguments =
                    $"exec --cd {Quote(workspacePath)} --skip-git-repo-check --full-auto --output-schema {Quote(schemaFile)} --output-last-message {Quote(outputFile)} --color never {Quote(BuildBridgePrompt(prompt))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process = new Process { StartInfo = startInfo };
            using var processScope = process;
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var finalMessage = File.Exists(outputFile)
                ? (await File.ReadAllTextAsync(outputFile, cancellationToken)).Trim()
                : string.Empty;

            var payload = TryParsePayload(finalMessage) ?? TryExtractPayloadFromText(stdout);

            if (process.ExitCode == 0)
            {
                var reply = payload?.Summary ?? ExtractReadableReply(finalMessage, stdout);

                return CodexCandidateRunResult.Success(
                    CodexDispatchResult.Success(reply, stdout, stderr, payload, candidate.SourceLabel, candidate.Executable));
            }

            var failure = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return CodexCandidateRunResult.Failure(
                string.IsNullOrWhiteSpace(failure)
                    ? "Codex 这次没顺利跑完。"
                    : failure.Trim());
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            return CodexCandidateRunResult.Timeout(
                CodexDispatchResult.Timeout(
                    "这次交给 Codex 的任务超时了，我已经先把它收掉。你可以把任务再缩小一点重试。",
                    candidate.SourceLabel,
                    candidate.Executable));
        }
        catch (Exception exception)
        {
            return CodexCandidateRunResult.Failure(exception.Message);
        }
    }

    private static string BuildBridgePrompt(string prompt)
    {
        return
            "You are being invoked by a desktop companion to work inside the user's workspace.\n" +
            "Complete the user's request directly in the workspace when appropriate.\n" +
            "Your final response must follow the provided JSON schema.\n" +
            "Field guidance:\n" +
            "- status: completed, partial, or blocked.\n" +
            "- summary: concise plain-language summary for the user.\n" +
            "- changed_files: relative paths you edited. Use [] if none.\n" +
            "- tests_run: commands or checks you ran. Use [] if none.\n" +
            "- notes: short caveats, risks, or useful observations. Use [] if none.\n" +
            "- next_step: the single best next step for the user. Use an empty string if none.\n\n" +
            "User request:\n" +
            prompt;
    }

    private static async Task WriteSchemaFileAsync(string schemaFile, CancellationToken cancellationToken)
    {
        const string schema = """
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "status": {
      "type": "string",
      "enum": ["completed", "partial", "blocked"]
    },
    "summary": {
      "type": "string"
    },
    "changed_files": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "tests_run": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "notes": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "next_step": {
      "type": "string"
    }
  },
  "required": ["status", "summary", "changed_files", "tests_run", "notes", "next_step"]
}
""";

        await File.WriteAllTextAsync(schemaFile, schema, new UTF8Encoding(false), cancellationToken);
    }

    private static CodexResultPayload? TryParsePayload(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CodexResultPayload>(rawJson, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static CodexResultPayload? TryExtractPayloadFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var line in text
                     .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Reverse())
        {
            var payload = TryParsePayload(line);
            if (payload is not null)
            {
                return payload;
            }
        }

        return null;
    }

    private static string ExtractReadableReply(string finalMessage, string stdout)
    {
        if (!string.IsNullOrWhiteSpace(finalMessage))
        {
            return finalMessage;
        }

        var payload = TryExtractPayloadFromText(stdout);
        if (!string.IsNullOrWhiteSpace(payload?.Summary))
        {
            return payload.Summary;
        }

        var compactLines = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                !line.StartsWith("Reading additional input from stdin", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("tokens used", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("202", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("{\"status\":", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(line, "codex", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        return compactLines.Length > 0
            ? string.Join("\n", compactLines)
            : "Codex 已经跑完了，但这轮没有回我一段可展示的总结。";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKillProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public sealed record CodexDispatchResult(
        bool IsSuccess,
        bool IsTimedOut,
        string Message,
        string StandardOutput,
        string StandardError,
        CodexResultPayload? Payload,
        string BackendLabel,
        string ExecutablePath)
    {
        public static CodexDispatchResult Success(
            string message,
            string standardOutput,
            string standardError,
            CodexResultPayload? payload,
            string backendLabel = "unknown",
            string executablePath = "") =>
            new(true, false, message, standardOutput, standardError, payload, backendLabel, executablePath);

        public static CodexDispatchResult Timeout(
            string message,
            string backendLabel = "unknown",
            string executablePath = "") =>
            new(false, true, message, string.Empty, string.Empty, null, backendLabel, executablePath);

        public static CodexDispatchResult Failure(string message) =>
            new(false, false, message, string.Empty, string.Empty, null, "none", string.Empty);
    }

    public sealed record CodexResultPayload(
        string Status,
        IReadOnlyList<string>? ChangedFiles,
        string Summary,
        IReadOnlyList<string>? TestsRun,
        IReadOnlyList<string>? Notes,
        string NextStep);

    private sealed record CodexExecutableCandidate(string Executable, string SourceLabel);

    private enum CodexCandidateOutcome
    {
        Success,
        Timeout,
        Failure
    }

    private sealed record CodexCandidateRunResult(
        CodexCandidateOutcome Outcome,
        CodexDispatchResult? Result,
        string? FailureMessage)
    {
        public static CodexCandidateRunResult Success(CodexDispatchResult result) =>
            new(CodexCandidateOutcome.Success, result, null);

        public static CodexCandidateRunResult Timeout(CodexDispatchResult result) =>
            new(CodexCandidateOutcome.Timeout, result, null);

        public static CodexCandidateRunResult Failure(string message) =>
            new(CodexCandidateOutcome.Failure, null, message);
    }
}
