namespace DesktopCompanion.WpfHost.Models;

public sealed class DistilledUserProfile
{
    public string Summary { get; set; } = string.Empty;

    public List<string> StableTraits { get; set; } = [];

    public List<string> KnownWorkLanes { get; set; } = [];

    public List<string> LikelyFailureModes { get; set; } = [];

    public List<string> BestSupportStyle { get; set; } = [];

    public List<string> SourceLabels { get; set; } = [];

    public List<string> PrivacyBoundaries { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
