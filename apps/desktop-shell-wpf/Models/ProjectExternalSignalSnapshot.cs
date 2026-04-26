namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectExternalSignalSnapshot
{
    public string StatusLabel { get; set; } = "not_requested";

    public string Summary { get; set; } = string.Empty;

    public List<string> SuggestedQueries { get; set; } = [];

    public List<string> ReferenceHints { get; set; } = [];

    public List<ProjectExternalReference> References { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
