namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string KindLabel { get; set; } = "项目线";

    public string PriorityLabel { get; set; } = string.Empty;

    public string NextAction { get; set; } = string.Empty;

    public string CurrentMilestone { get; set; } = string.Empty;

    public string ExpectedDeliverable { get; set; } = string.Empty;

    public string PrimaryWorkspacePath { get; set; } = string.Empty;

    public string PrimaryWorkspaceLabel { get; set; } = string.Empty;

    public string WorkspaceKindLabel { get; set; } = string.Empty;

    public string CodexWorkspacePath { get; set; } = string.Empty;

    public string CodexWorkspaceLabel { get; set; } = string.Empty;

    public int CodexThreadCount { get; set; }

    public DateTimeOffset? LastCodexThreadAt { get; set; }

    public ProjectProgressSnapshot ProgressSnapshot { get; set; } = new();

    public ProjectStateAssessment StateAssessment { get; set; } = new();

    public ProjectExternalSignalSnapshot ExternalSignals { get; set; } = new();

    public int MomentumScore { get; set; } = 30;

    public int ClarityScore { get; set; } = 30;

    public int RiskScore { get; set; } = 25;

    public int DriftScore { get; set; } = 20;

    public int ConfidenceScore { get; set; } = 25;

    public DateTimeOffset? LastEvidenceAt { get; set; }

    public DateTimeOffset? LastMeaningfulProgressAt { get; set; }

    public List<string> Keywords { get; set; } = [];

    public List<string> RecentItems { get; set; } = [];

    public List<string> RecentCodexThreadTitles { get; set; } = [];

    public List<ProjectEvidenceItem> EvidenceItems { get; set; } = [];

    public List<ProjectBlocker> Blockers { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
