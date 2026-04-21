namespace DesktopCompanion.WpfHost.Models;

public sealed class CompanionPermissionProfile
{
    public bool IsConfigured { get; set; }

    public bool CanReadSelectedWorkspace { get; set; }

    public bool CanRememberWorkspaceSources { get; set; }

    public bool CanBuildProjectMemoryFromDocs { get; set; }

    public string PresetLabel { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
