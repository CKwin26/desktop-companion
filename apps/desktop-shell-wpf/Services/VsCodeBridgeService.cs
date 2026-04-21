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

    public bool CanRunCodex => !string.IsNullOrWhiteSpace(ResolveCodexPath());

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

    public async Task<CodexDispatchResult> RunCodexTaskAsync(
        string workspacePath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workspacePath))
        {
            return CodexDispatchResult.Failure("这个目录我没找到，所以还没法把任务交给 Codex。");
        }

        var codexPath = ResolveCodexPath();
        if (string.IsNullOrWhiteSpace(codexPath))
        {
            return CodexDispatchResult.Failure("我这边还没找到 codex 可执行文件，所以暂时没法替你派工。");
        }

        var outputFile = Path.Combine(Path.GetTempPath(), $"tuanzi-codex-{Guid.NewGuid():N}.json");
        var schemaFile = Path.Combine(Path.GetTempPath(), $"tuanzi-codex-schema-{Guid.NewGuid():N}.json");
        Process? process = null;

        try
        {
            await WriteSchemaFileAsync(schemaFile, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = codexPath,
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

            var payload = TryParsePayload(finalMessage);

            if (process.ExitCode == 0)
            {
                var reply = payload?.Summary ?? (string.IsNullOrWhiteSpace(finalMessage)
                    ? "Codex 已经跑完了，但这轮没有回我一段可展示的总结。"
                    : finalMessage);

                return CodexDispatchResult.Success(reply, stdout, stderr, payload);
            }

            var failure = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return CodexDispatchResult.Failure(
                string.IsNullOrWhiteSpace(failure)
                    ? "Codex 这次没顺利跑完。"
                    : failure.Trim());
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
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

    private string? ResolveCodexPath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredCodexPath) && File.Exists(_configuredCodexPath))
        {
            return _configuredCodexPath;
        }

        var envPath = Environment.GetEnvironmentVariable("DESKTOP_COMPANION_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var extensionRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode",
            "extensions");

        if (!Directory.Exists(extensionRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(extensionRoot, "codex.exe", SearchOption.AllDirectories)
            .Where(path => path.Contains("openai.chatgpt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path)
            .FirstOrDefault();
    }

    private static string BuildBridgePrompt(string prompt)
    {
        return
            "You are being invoked by a desktop companion to work inside the user's VS Code workspace.\n" +
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
        CodexResultPayload? Payload)
    {
        public static CodexDispatchResult Success(
            string message,
            string standardOutput,
            string standardError,
            CodexResultPayload? payload) =>
            new(true, false, message, standardOutput, standardError, payload);

        public static CodexDispatchResult Timeout(string message) =>
            new(false, true, message, string.Empty, string.Empty, null);

        public static CodexDispatchResult Failure(string message) =>
            new(false, false, message, string.Empty, string.Empty, null);
    }

    public sealed record CodexResultPayload(
        string Status,
        IReadOnlyList<string>? ChangedFiles,
        string Summary,
        IReadOnlyList<string>? TestsRun,
        IReadOnlyList<string>? Notes,
        string NextStep);
}
