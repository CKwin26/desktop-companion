using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalConversationStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalConversationStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "conversation.json");
    }

    public IReadOnlyList<ConversationMessage> LoadMessages()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            var json = File.ReadAllText(_storagePath);
            var messages = JsonSerializer.Deserialize<List<ConversationMessage>>(json, _jsonOptions);
            return messages ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveMessages(IEnumerable<ConversationMessage> messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var json = JsonSerializer.Serialize(messages, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }
}
