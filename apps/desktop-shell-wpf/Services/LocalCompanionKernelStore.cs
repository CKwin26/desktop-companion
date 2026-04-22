using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalCompanionKernelStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalCompanionKernelStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "persona-kernel.json");
    }

    public CompanionKernelSelection LoadSelection()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return new CompanionKernelSelection();
            }

            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<CompanionKernelSelection>(json, _jsonOptions)
                   ?? new CompanionKernelSelection();
        }
        catch
        {
            return new CompanionKernelSelection();
        }
    }

    public void SaveSelection(CompanionKernelSelection selection)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(selection, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
