namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectBlocker
{
    public string Summary { get; set; } = string.Empty;

    public string OwnerHint { get; set; } = string.Empty;

    public string SeverityLabel { get; set; } = "medium";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
