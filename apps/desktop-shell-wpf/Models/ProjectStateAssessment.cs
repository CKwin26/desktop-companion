namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectStateAssessment
{
    public int InternalUnderstandingScore { get; set; } = 20;

    public int EvidenceStrengthScore { get; set; } = 20;

    public int ExternalSupportScore { get; set; } = 50;

    public int ExecutionHealthScore { get; set; } = 25;

    public int RiskScore { get; set; } = 25;

    public int ConfidenceScore { get; set; } = 20;

    public int CognitionConfidence { get; set; } = 20;

    public int EvidenceCoverage { get; set; } = 20;

    public int FreshnessRisk { get; set; } = 20;

    public int ExternalRelevance { get; set; } = 20;

    public int DecisionStakes { get; set; } = 20;

    public int NeedSearchScore { get; set; }

    public bool NeedsExternalSearch { get; set; }

    public string SearchStatusLabel { get; set; } = "not_assessed";

    public string SearchDecisionReason { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
