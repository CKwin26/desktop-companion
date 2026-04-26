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
        var evidenceCount = project.EvidenceItems.Count;
        score += Math.Min(35, evidenceCount * 6);

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

        if (string.Equals(project.PriorityLabel, "先动", StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.PriorityLabel, "鍏堝姩", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        return ClampScore(score);
    }

    private static int CalculateExternalRelevance(ProjectMemory project)
    {
        var signalText = string.Join(
            " ",
            [
                project.Name,
                project.KindLabel,
                project.ExpectedDeliverable,
                project.WorkspaceKindLabel,
                ..project.Keywords,
                ..project.RecentItems
            ])
            .ToLowerInvariant();

        if (ContainsAny(signalText, "phd", "application", "proposal", "sop", "school", "recommendation"))
        {
            return 88;
        }

        if (ContainsAny(signalText, "research", "paper", "benchmark", "evaluation", "publication"))
        {
            return 78;
        }

        if (ContainsAny(signalText, "product", "ui", "ux", "design", "landing"))
        {
            return 68;
        }

        if (ContainsAny(signalText, "repo", "code", "api", "service", "library"))
        {
            return 58;
        }

        return 42;
    }

    private static int CalculateDecisionStakes(ProjectMemory project)
    {
        var signalText = string.Join(
            " ",
            [
                project.Name,
                project.ExpectedDeliverable,
                project.WorkspaceKindLabel,
                ..project.Keywords
            ])
            .ToLowerInvariant();

        var score = 24;

        if (string.Equals(project.PriorityLabel, "先动", StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.PriorityLabel, "鍏堝姩", StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }

        if (project.Blockers.Count > 0)
        {
            score += 10;
        }

        if (ContainsAny(signalText, "phd", "application", "deadline", "submission", "proposal", "grant"))
        {
            score += 32;
        }
        else if (ContainsAny(signalText, "research", "paper", "benchmark", "evaluation"))
        {
            score += 22;
        }
        else if (ContainsAny(signalText, "release", "prod", "launch"))
        {
            score += 18;
        }

        return ClampScore(score);
    }

    private static string BuildSearchDecisionReason(
        int cognitionConfidence,
        int evidenceStrength,
        int freshnessRisk,
        int externalRelevance,
        int decisionStakes,
        int needSearchScore)
    {
        var reasons = new List<string>();

        if (cognitionConfidence < 55)
        {
            reasons.Add("internal understanding is still weak");
        }

        if (evidenceStrength < 55)
        {
            reasons.Add("local evidence coverage is thin");
        }

        if (freshnessRisk >= 50)
        {
            reasons.Add("current picture may be stale");
        }

        if (externalRelevance >= 70)
        {
            reasons.Add("outside standards likely matter");
        }

        if (decisionStakes >= 70)
        {
            reasons.Add("the project is high-stakes");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("current local understanding looks sufficient");
        }

        return $"NeedSearch={needSearchScore}/100 because {string.Join(", ", reasons)}.";
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
