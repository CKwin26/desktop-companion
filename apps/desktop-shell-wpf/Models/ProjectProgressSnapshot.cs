namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectProgressSnapshot
{
    public string StatusLabel { get; set; } = string.Empty;

    public string FocusSummary { get; set; } = string.Empty;

    public string HealthSummary { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
