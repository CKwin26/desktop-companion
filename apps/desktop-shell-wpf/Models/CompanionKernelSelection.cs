namespace DesktopCompanion.WpfHost.Models;

public sealed class CompanionKernelSelection
{
    public string KernelId { get; set; } = "balanced";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
