namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectExternalReference
{
    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string SourceHost { get; set; } = string.Empty;

    public string Snippet { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.Now;
}
