using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class ProjectStateScorer
{
    public ProjectStateAssessment Score(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        ProjectExternalSignalSnapshot externalSignals)
    {
        var executionHealth = CalculateExecutionHealth(project, assessment);
        var risk = CalculateRisk(project, assessment, externalSignals);
        var externalSupport = CalculateExternalSupport(project, externalSignals);
        var confidence = CalculateConfidence(project, assessment, externalSignals, externalSupport);
        var drift = CalculateDrift(project, assessment, externalSignals, externalSupport);

        assessment.ExecutionHealthScore = executionHealth;
        assessment.ExternalSupportScore = externalSupport;
        assessment.RiskScore = risk;
        assessment.ConfidenceScore = confidence;
        assessment.Summary = BuildAssessmentSummary(project, assessment, externalSignals);
        assessment.UpdatedAt = DateTimeOffset.Now;

        project.MomentumScore = executionHealth;
        project.ClarityScore = assessment.InternalUnderstandingScore;
        project.RiskScore = risk;
        project.DriftScore = drift;
        project.ConfidenceScore = confidence;

        project.ProgressSnapshot ??= new ProjectProgressSnapshot();
        project.ProgressSnapshot.StatusLabel = project.Blockers.Count > 0
            ? "blocked"
            : assessment.SearchStatusLabel == "required" && !string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase)
                ? "needs-validation"
                : "active";
        project.ProgressSnapshot.FocusSummary = !string.IsNullOrWhiteSpace(project.CurrentMilestone)
            ? project.CurrentMilestone
            : !string.IsNullOrWhiteSpace(project.NextAction)
                ? project.NextAction
                : project.Name;
        project.ProgressSnapshot.HealthSummary = assessment.Summary;
        project.ProgressSnapshot.UpdatedAt = assessment.UpdatedAt;

        return assessment;
    }

    private static int CalculateExecutionHealth(ProjectMemory project, ProjectStateAssessment assessment)
    {
        var score = 18;

        if (!string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 22;
        }

        if (!string.IsNullOrWhiteSpace(project.CurrentMilestone))
        {
            score += 16;
        }

        score += Math.Min(12, project.RecentItems.Count * 3);
        score += Math.Min(10, project.EvidenceItems.Count * 2);

        if (project.Blockers.Count > 0)
        {
            score -= project.Blockers.Count >= 2 ? 28 : 16;
        }

        if (assessment.SearchStatusLabel == "required")
        {
            score -= 8;
        }

        return ClampScore(score);
    }

    private static int CalculateRisk(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        ProjectExternalSignalSnapshot externalSignals)
    {
        var score = 12;
        score += project.Blockers.Count switch
        {
            >= 2 => 38,
            1 => 22,
            _ => 0
        };

        score += (int)Math.Round(assessment.FreshnessRisk * 0.35);

        if (string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 12;
        }

        if (externalSignals.StatusLabel == "required")
        {
            score += 16;
        }
        else if (externalSignals.StatusLabel == "suggested")
        {
            score += 8;
        }
        else if (string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase))
        {
            score -= Math.Min(12, externalSignals.References.Count * 3);
        }

        return ClampScore(score);
    }

    private static int CalculateExternalSupport(
        ProjectMemory project,
        ProjectExternalSignalSnapshot externalSignals)
    {
        if (string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase))
        {
            var hostCount = externalSignals.References
                .Select(reference => reference.SourceHost)
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var score = 48;
            score += Math.Min(18, externalSignals.References.Count * 4);
            score += Math.Min(12, hostCount * 4);
            score += externalSignals.References.Any(reference => IsAuthoritativeReference(project, reference)) ? 12 : 0;
            return ClampScore(score);
        }

        return externalSignals.StatusLabel switch
        {
            "required" => 32,
            "suggested" => 44,
            "not_needed" => 58,
            _ => 50
        };
    }

    private static int CalculateConfidence(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        ProjectExternalSignalSnapshot externalSignals,
        int externalSupport)
    {
        var score = (int)Math.Round(
            (0.45 * assessment.CognitionConfidence) +
            (0.20 * assessment.EvidenceStrengthScore) +
            (0.20 * (100 - assessment.FreshnessRisk)) +
            (0.15 * externalSupport));

        score -= externalSignals.StatusLabel switch
        {
            "required" => 12,
            "suggested" => 6,
            _ => 0
        };

        if (string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase)
            && externalSignals.References.Any(reference => IsAuthoritativeReference(project, reference)))
        {
            score += 8;
        }

        return ClampScore(score);
    }

    private static int CalculateDrift(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        ProjectExternalSignalSnapshot externalSignals,
        int externalSupport)
    {
        var score = 12;

        if (string.IsNullOrWhiteSpace(project.ExpectedDeliverable))
        {
            score += 20;
        }

        if (string.IsNullOrWhiteSpace(project.CurrentMilestone))
        {
            score += 18;
        }

        if (project.RecentItems.Count >= 4 && string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 16;
        }

        if (externalSignals.StatusLabel == "required")
        {
            score += 18;
        }
        else if (externalSignals.StatusLabel == "suggested")
        {
            score += 8;
        }
        else if (string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase))
        {
            score -= Math.Min(10, externalSignals.References.Count * 2);
        }

        score += project.Blockers.Count * 5;
        score += (int)Math.Round((100 - externalSupport) * 0.15);
        return ClampScore(score);
    }

    private static string BuildAssessmentSummary(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        ProjectExternalSignalSnapshot externalSignals)
    {
        if (project.Blockers.Count > 0)
        {
            return $"Blocked by {project.Blockers[0].Summary}";
        }

        if (string.Equals(externalSignals.StatusLabel, "collected", StringComparison.OrdinalIgnoreCase))
        {
            return externalSignals.Summary;
        }

        if (externalSignals.StatusLabel == "required")
        {
            return "Needs external validation before the current state should be trusted.";
        }

        if (!string.IsNullOrWhiteSpace(project.ExpectedDeliverable))
        {
            return $"Targeting {project.ExpectedDeliverable}";
        }

        return $"Internal understanding {assessment.InternalUnderstandingScore}/100";
    }

    private static int ClampScore(int score)
    {
        return Math.Max(0, Math.Min(100, score));
    }

    private static bool IsAuthoritativeReference(ProjectMemory project, ProjectExternalReference reference)
    {
        var host = reference.SourceHost.ToLowerInvariant();
        var signalText = string.Join(
                " ",
                [
                    project.Name,
                    project.ExpectedDeliverable,
                    project.WorkspaceKindLabel,
                    ..project.Keywords
                ])
            .ToLowerInvariant();

        if (ContainsAny(signalText, "phd", "application", "sop", "proposal", "school"))
        {
            return host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase)
                   || host.Contains("admission")
                   || host.Contains("graduate")
                   || host.Contains("university");
        }

        if (ContainsAny(signalText, "research", "paper", "benchmark", "evaluation"))
        {
            return host.Contains("openreview")
                   || host.Contains("arxiv")
                   || host.Contains("ieee")
                   || host.Contains("acm")
                   || host.Contains("nature")
                   || host.Contains("springer");
        }

        return host.Contains("github.com")
               || host.Contains("docs.")
               || host.Contains("learn.")
               || host.Contains("developer.")
               || host.Contains("official");
    }

    private static bool ContainsAny(string input, params string[] needles)
    {
        return needles.Any(needle => input.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
