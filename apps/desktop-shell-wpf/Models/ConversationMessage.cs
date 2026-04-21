namespace DesktopCompanion.WpfHost.Models;

public sealed class ConversationMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public ConversationRole Role { get; set; } = ConversationRole.Companion;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
