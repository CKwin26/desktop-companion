namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string KindLabel { get; set; } = "项目线";

    public string PriorityLabel { get; set; } = string.Empty;

    public string NextAction { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public List<string> RecentItems { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
