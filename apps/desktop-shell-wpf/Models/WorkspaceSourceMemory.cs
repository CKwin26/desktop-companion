namespace DesktopCompanion.WpfHost.Models;

public sealed class WorkspaceSourceMemory
{
    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int ImportedDocumentCount { get; set; }

    public string LastSummary { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset LastScannedAt { get; set; } = DateTimeOffset.Now;
}
