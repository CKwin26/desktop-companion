using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalProjectStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalProjectStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "projects.json");
    }

    public IReadOnlyList<ProjectMemory> LoadProjects()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            var json = File.ReadAllText(_storagePath);
            var projects = JsonSerializer.Deserialize<List<ProjectMemory>>(json, _jsonOptions);
            return projects ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveProjects(IEnumerable<ProjectMemory> projects)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(projects, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
