using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalWorkspaceSourceStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalWorkspaceSourceStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "workspace-sources.json");
    }

    public IReadOnlyList<WorkspaceSourceMemory> LoadSources()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            var json = File.ReadAllText(_storagePath);
            var sources = JsonSerializer.Deserialize<List<WorkspaceSourceMemory>>(json, _jsonOptions);
            return sources ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveSources(IEnumerable<WorkspaceSourceMemory> sources)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(sources, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
