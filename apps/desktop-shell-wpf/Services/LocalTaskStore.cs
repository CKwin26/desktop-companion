using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalTaskStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalTaskStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "tasks.json");
    }

    public IReadOnlyList<CompanionTask> LoadTasks()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            var json = File.ReadAllText(_storagePath);
            var tasks = JsonSerializer.Deserialize<List<CompanionTask>>(json, _jsonOptions);
            return tasks ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveTasks(IEnumerable<CompanionTask> tasks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(tasks, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
