using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class InternalProjectAssessmentService
{
    public ProjectStateAssessment Assess(ProjectMemory project)
    {
        var understanding = CalculateInternalUnderstanding(project);
        var evidenceStrength = CalculateEvidenceStrength(project);
        var cognitionConfidence = ClampScore((int)Math.Round((understanding * 0.55) + (evidenceStrength * 0.45)));
        var freshnessRisk = CalculateFreshnessRisk(project);
        var externalRelevance = CalculateExternalRelevance(project);
        var decisionStakes = CalculateDecisionStakes(project);
        var needSearchScore = ClampScore((int)Math.Round(
            (0.35 * (100 - cognitionConfidence)) +
            (0.20 * (100 - evidenceStrength)) +
            (0.20 * freshnessRisk) +
            (0.15 * externalRelevance) +
            (0.10 * decisionStakes)));

        var searchStatus = needSearchScore >= 60
            ? "required"
            : needSearchScore >= 40
                ? "suggested"
                : "not_needed";

        return new ProjectStateAssessment
        {
            InternalUnderstandingScore = understanding,
            EvidenceStrengthScore = evidenceStrength,
            CognitionConfidence = cognitionConfidence,
            EvidenceCoverage = evidenceStrength,
            FreshnessRisk = freshnessRisk,
            ExternalRelevance = externalRelevance,
            DecisionStakes = decisionStakes,
            NeedSearchScore = needSearchScore,
            NeedsExternalSearch = needSearchScore >= 60,
            SearchStatusLabel = searchStatus,
            SearchDecisionReason = BuildSearchDecisionReason(
                project,
                cognitionConfidence,
                evidenceStrength,
                freshnessRisk,
                externalRelevance,
                decisionStakes,
                needSearchScore),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static int CalculateInternalUnderstanding(ProjectMemory project)
    {
        var score = 12;

        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(project.CurrentMilestone))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(project.ExpectedDeliverable))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(project.PrimaryWorkspacePath))
        {
            score += 12;
        }

        if (!string.IsNullOrWhiteSpace(project.WorkspaceKindLabel))
        {
            score += 10;
        }

        score += Math.Min(12, project.Keywords.Count * 3);
        return ClampScore(score);
    }

    private static int CalculateEvidenceStrength(ProjectMemory project)
    {
        var score = 10;
        score += Math.Min(35, project.EvidenceItems.Count * 6);

        var sourceTypeCount = project.EvidenceItems
            .Select(item => item.SourceType?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        score += Math.Min(18, sourceTypeCount * 6);

        if (project.EvidenceItems.Any(item => item.SourceType.Equals("repo-structure", StringComparison.OrdinalIgnoreCase)))
        {
            score += 12;
        }

        if (project.EvidenceItems.Any(item => item.SourceType.Equals("workspace-docs", StringComparison.OrdinalIgnoreCase)))
        {
            score += 10;
        }

        if (project.Blockers.Count > 0)
        {
            score += 5;
        }

        return ClampScore(score);
    }

    private static int CalculateFreshnessRisk(ProjectMemory project)
    {
        var lastSignalAt = project.LastEvidenceAt ?? project.LastMeaningfulProgressAt ?? project.UpdatedAt;
        var age = DateTimeOffset.Now - lastSignalAt;

        var score = age.TotalDays switch
        {
            > 21 => 80,
            > 10 => 65,
            > 5 => 48,
            > 2 => 32,
            _ => 16
        };

        return ClampScore(score);
    }

    private static int CalculateExternalRelevance(ProjectMemory project)
    {
        var archetype = ProjectArchetypes.ParseLabel(project.ArchetypeLabel);
        var score = archetype switch
        {
            ProjectArchetype.ApplicationOps => 88,
            ProjectArchetype.ResearchEvaluation => 76,
            ProjectArchetype.ProductResearch => 68,
            ProjectArchetype.EngineeringExecution => 54,
            ProjectArchetype.OperationsAdmin => 38,
            ProjectArchetype.LifeEntertainment => 18,
            _ => 42
        };

        if (project.ExternalSignals.References.Count > 0)
        {
            score -= 4;
        }

        if (ContainsAny(project.ExpectedDeliverable, "proposal", "paper", "benchmark", "release", "submission"))
        {
            score += 6;
        }

        return ClampScore(score);
    }

    private static int CalculateDecisionStakes(ProjectMemory project)
    {
        var score = 24;
        var archetype = ProjectArchetypes.ParseLabel(project.ArchetypeLabel);

        if (project.Blockers.Count > 0)
        {
            score += 10;
        }

        score += archetype switch
        {
            ProjectArchetype.ApplicationOps => 30,
            ProjectArchetype.ResearchEvaluation => 20,
            ProjectArchetype.ProductResearch => 24,
            ProjectArchetype.EngineeringExecution => 16,
            ProjectArchetype.OperationsAdmin => 12,
            ProjectArchetype.LifeEntertainment => 4,
            _ => 0
        };

        if (ContainsAny(project.ExpectedDeliverable, "deadline", "submission", "proposal", "grant"))
        {
            score += 12;
        }

        if (ContainsAny(project.ExpectedDeliverable, "prototype", "hardware", "device", "sensor", "pillow", "ergonomic"))
        {
            score += 10;
        }

        return ClampScore(score);
    }

    private static string BuildSearchDecisionReason(
        ProjectMemory project,
        int cognitionConfidence,
        int evidenceStrength,
        int freshnessRisk,
        int externalRelevance,
        int decisionStakes,
        int needSearchScore)
    {
        var reasons = new List<string>();
        var archetype = ProjectArchetypes.ParseLabel(project.ArchetypeLabel);
        var bucketLabel = ProjectArchetypes.ToDisplayLabel(archetype);

        if (cognitionConfidence < 55)
        {
            reasons.Add("当前理解还不够稳");
        }

        if (evidenceStrength < 55)
        {
            reasons.Add("本地证据还偏薄");
        }

        if (freshnessRisk >= 50)
        {
            reasons.Add("手上的判断可能已经有点旧了");
        }

        if (externalRelevance >= 70)
        {
            reasons.Add("这条线很依赖外部标准或公开参照");
        }

        if (decisionStakes >= 70)
        {
            reasons.Add("这条线的决策代价比较高");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("目前本地理解基本够用");
        }

        return $"外部核验建议 {needSearchScore}/100：这条线现在按“{bucketLabel}”理解；{string.Join("，", reasons)}。";
    }

    private static bool ContainsAny(string input, params string[] needles)
    {
        return needles.Any(needle => input.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static int ClampScore(int score)
    {
        return Math.Max(0, Math.Min(100, score));
    }
}
