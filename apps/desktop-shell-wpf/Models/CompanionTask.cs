namespace DesktopCompanion.WpfHost.Models;

public sealed class CompanionTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public CompanionTaskState State { get; set; } = CompanionTaskState.Todo;

    public string Category { get; set; } = "日常";

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
