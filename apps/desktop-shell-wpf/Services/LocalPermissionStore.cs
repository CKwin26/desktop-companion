using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalPermissionStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalPermissionStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "permissions.json");
    }

    public CompanionPermissionProfile LoadProfile()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return new CompanionPermissionProfile();
            }

            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<CompanionPermissionProfile>(json, _jsonOptions)
                   ?? new CompanionPermissionProfile();
        }
        catch
        {
            return new CompanionPermissionProfile();
        }
    }

    public void SaveProfile(CompanionPermissionProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
