using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalUserProfileStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalUserProfileStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "distilled-user-profile.json");
    }

    public DistilledUserProfile? LoadProfile()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return null;
            }

            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<DistilledUserProfile>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveProfile(DistilledUserProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }
        }
        catch
        {
        }
    }
}
