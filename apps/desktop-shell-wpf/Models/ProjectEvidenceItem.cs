namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectEvidenceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SourceType { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public bool IndicatesProgress { get; set; } = true;

    public int Weight { get; set; } = 50;

    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.Now;
}
