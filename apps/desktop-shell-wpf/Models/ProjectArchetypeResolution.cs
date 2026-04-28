namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectArchetypeResolution
{
    public ProjectArchetype Archetype { get; set; } = ProjectArchetype.General;

    public int Confidence { get; set; } = 35;

    public string Reason { get; set; } = string.Empty;
}
