using System.IO;
using System.Text.Json;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class LocalProjectStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storagePath;

    public LocalProjectStore()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCompanion");

        _storagePath = Path.Combine(baseDirectory, "projects.json");
    }

    public IReadOnlyList<ProjectMemory> LoadProjects()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            var json = File.ReadAllText(_storagePath);
            var projects = JsonSerializer.Deserialize<List<ProjectMemory>>(json, _jsonOptions);
            if (projects is null)
            {
                return [];
            }

            foreach (var project in projects)
            {
                NormalizeProject(project);
            }

            return projects;
        }
        catch
        {
            return [];
        }
    }

    public void SaveProjects(IEnumerable<ProjectMemory> projects)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        var normalizedProjects = projects
            .Select(project =>
            {
                NormalizeProject(project);
                return project;
            })
            .ToList();

        var json = JsonSerializer.Serialize(normalizedProjects, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }

    private static void NormalizeProject(ProjectMemory project)
    {
        project.Name = project.Name?.Trim() ?? string.Empty;
        project.Summary = project.Summary?.Trim() ?? string.Empty;
        project.KindLabel = string.IsNullOrWhiteSpace(project.KindLabel) ? "项目线" : project.KindLabel.Trim();
        project.PriorityLabel = project.PriorityLabel?.Trim() ?? string.Empty;
        project.NextAction = project.NextAction?.Trim() ?? string.Empty;
        project.CurrentMilestone = string.IsNullOrWhiteSpace(project.CurrentMilestone)
            ? project.NextAction
            : project.CurrentMilestone.Trim();
        project.ExpectedDeliverable = project.ExpectedDeliverable?.Trim() ?? string.Empty;
        project.PrimaryWorkspacePath = project.PrimaryWorkspacePath?.Trim() ?? string.Empty;
        project.PrimaryWorkspaceLabel = project.PrimaryWorkspaceLabel?.Trim() ?? string.Empty;
        project.WorkspaceKindLabel = project.WorkspaceKindLabel?.Trim() ?? string.Empty;
        project.CodexWorkspacePath = project.CodexWorkspacePath?.Trim() ?? string.Empty;
        project.CodexWorkspaceLabel = project.CodexWorkspaceLabel?.Trim() ?? string.Empty;
        project.ArchetypeLabel = ProjectArchetypes.ToLabel(ProjectArchetypes.ParseLabel(project.ArchetypeLabel));
        project.ArchetypeConfidence = ClampScore(project.ArchetypeConfidence == 0 ? 35 : project.ArchetypeConfidence);
        project.ArchetypeReason = project.ArchetypeReason?.Trim() ?? string.Empty;
        project.Keywords = NormalizeStrings(project.Keywords, 10);
        project.RecentItems = NormalizeStrings(project.RecentItems, 10);
        project.RecentCodexThreadTitles = NormalizeStrings(project.RecentCodexThreadTitles, 6);
        project.EvidenceItems ??= [];
        project.Blockers ??= [];
        project.ProgressSnapshot ??= new ProjectProgressSnapshot();
        project.StateAssessment ??= new ProjectStateAssessment();
        project.ExternalSignals ??= new ProjectExternalSignalSnapshot();
        project.ProgressSnapshot.StatusLabel = project.ProgressSnapshot.StatusLabel?.Trim() ?? string.Empty;
        project.ProgressSnapshot.FocusSummary = project.ProgressSnapshot.FocusSummary?.Trim() ?? string.Empty;
        project.ProgressSnapshot.HealthSummary = project.ProgressSnapshot.HealthSummary?.Trim() ?? string.Empty;
        project.StateAssessment.SearchStatusLabel = project.StateAssessment.SearchStatusLabel?.Trim() ?? string.Empty;
        project.StateAssessment.SearchDecisionReason = project.StateAssessment.SearchDecisionReason?.Trim() ?? string.Empty;
        project.StateAssessment.Summary = project.StateAssessment.Summary?.Trim() ?? string.Empty;
        project.ExternalSignals.StatusLabel = project.ExternalSignals.StatusLabel?.Trim() ?? string.Empty;
        project.ExternalSignals.Summary = project.ExternalSignals.Summary?.Trim() ?? string.Empty;
        project.ExternalSignals.SuggestedQueries = NormalizeStrings(project.ExternalSignals.SuggestedQueries, 6);
        project.ExternalSignals.ReferenceHints = NormalizeStrings(project.ExternalSignals.ReferenceHints, 6);
        project.ExternalSignals.References ??= [];
        project.MomentumScore = ClampScore(project.MomentumScore == 0 ? InferMomentum(project) : project.MomentumScore);
        project.ClarityScore = ClampScore(project.ClarityScore == 0 ? InferClarity(project) : project.ClarityScore);
        project.RiskScore = ClampScore(project.RiskScore == 0 ? InferRisk(project) : project.RiskScore);
        project.DriftScore = ClampScore(project.DriftScore == 0 ? InferDrift(project) : project.DriftScore);
        project.ConfidenceScore = ClampScore(project.ConfidenceScore == 0 ? InferConfidence(project) : project.ConfidenceScore);

        foreach (var reference in project.ExternalSignals.References)
        {
            reference.Title = reference.Title?.Trim() ?? string.Empty;
            reference.Url = reference.Url?.Trim() ?? string.Empty;
            reference.SourceHost = reference.SourceHost?.Trim() ?? string.Empty;
            reference.Snippet = reference.Snippet?.Trim() ?? string.Empty;
        }

        project.ExternalSignals.References = project.ExternalSignals.References
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Title) && !string.IsNullOrWhiteSpace(reference.Url))
            .GroupBy(reference => reference.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(reference => reference.ObservedAt).First())
            .OrderByDescending(reference => reference.ObservedAt)
            .Take(6)
            .ToList();

        foreach (var evidence in project.EvidenceItems)
        {
            evidence.Id = string.IsNullOrWhiteSpace(evidence.Id) ? Guid.NewGuid().ToString("N") : evidence.Id;
            evidence.SourceType = evidence.SourceType?.Trim() ?? string.Empty;
            evidence.SourceLabel = evidence.SourceLabel?.Trim() ?? string.Empty;
            evidence.SourcePath = evidence.SourcePath?.Trim() ?? string.Empty;
            evidence.Summary = evidence.Summary?.Trim() ?? string.Empty;
            evidence.Detail = evidence.Detail?.Trim() ?? string.Empty;
            evidence.Weight = ClampScore(evidence.Weight);
        }

        project.EvidenceItems = project.EvidenceItems
            .Where(evidence => !string.IsNullOrWhiteSpace(evidence.Summary))
            .OrderByDescending(evidence => evidence.ObservedAt)
            .Take(20)
            .ToList();

        foreach (var blocker in project.Blockers)
        {
            blocker.Summary = blocker.Summary?.Trim() ?? string.Empty;
            blocker.OwnerHint = blocker.OwnerHint?.Trim() ?? string.Empty;
            blocker.SeverityLabel = string.IsNullOrWhiteSpace(blocker.SeverityLabel) ? "medium" : blocker.SeverityLabel.Trim();
        }

        project.Blockers = project.Blockers
            .Where(blocker => !string.IsNullOrWhiteSpace(blocker.Summary))
            .OrderByDescending(blocker => blocker.UpdatedAt)
            .Take(8)
            .ToList();

        if (project.LastEvidenceAt is null && project.EvidenceItems.Count > 0)
        {
            project.LastEvidenceAt = project.EvidenceItems.Max(evidence => evidence.ObservedAt);
        }

        if (project.LastMeaningfulProgressAt is null)
        {
            project.LastMeaningfulProgressAt = project.LastEvidenceAt ?? project.UpdatedAt;
        }

        if (project.LastCodexThreadAt is not null)
        {
            project.UpdatedAt = project.LastCodexThreadAt > project.UpdatedAt
                ? project.LastCodexThreadAt.Value
                : project.UpdatedAt;
        }

        if (string.IsNullOrWhiteSpace(project.ProgressSnapshot.StatusLabel))
        {
            project.ProgressSnapshot.StatusLabel = project.Blockers.Count > 0 ? "blocked" : "active";
        }

        if (string.IsNullOrWhiteSpace(project.ProgressSnapshot.FocusSummary))
        {
            project.ProgressSnapshot.FocusSummary = !string.IsNullOrWhiteSpace(project.CurrentMilestone)
                ? project.CurrentMilestone
                : !string.IsNullOrWhiteSpace(project.NextAction)
                    ? project.NextAction
                    : project.Name;
        }

        if (string.IsNullOrWhiteSpace(project.ProgressSnapshot.HealthSummary))
        {
            project.ProgressSnapshot.HealthSummary = project.Blockers.Count > 0
                ? $"Blocked by {project.Blockers[0].Summary}"
                : $"Momentum {project.MomentumScore}/100";
        }
    }

    private static List<string> NormalizeStrings(IEnumerable<string>? values, int maxCount)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    private static int ClampScore(int score)
    {
        return Math.Max(0, Math.Min(100, score));
    }

    private static int InferMomentum(ProjectMemory project)
    {
        var score = 20;
        if (!string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 20;
        }

        if (project.RecentItems.Count > 0)
        {
            score += Math.Min(20, project.RecentItems.Count * 5);
        }

        if (project.Blockers.Count > 0)
        {
            score -= 15;
        }

        return score;
    }

    private static int InferClarity(ProjectMemory project)
    {
        var score = 15;
        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(project.CurrentMilestone))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(project.ExpectedDeliverable))
        {
            score += 20;
        }

        if (project.Keywords.Count >= 3)
        {
            score += 10;
        }

        return score;
    }

    private static int InferRisk(ProjectMemory project)
    {
        var score = 20;
        score += project.Blockers.Count switch
        {
            >= 2 => 35,
            1 => 20,
            _ => 0
        };

        if (string.IsNullOrWhiteSpace(project.NextAction))
        {
            score += 10;
        }

        return score;
    }

    private static int InferDrift(ProjectMemory project)
    {
        var score = string.IsNullOrWhiteSpace(project.ExpectedDeliverable) ? 28 : 15;
        if (project.RecentItems.Count > 3 && string.IsNullOrWhiteSpace(project.CurrentMilestone))
        {
            score += 12;
        }

        return score;
    }

    private static int InferConfidence(ProjectMemory project)
    {
        var score = 20;
        if (!string.IsNullOrWhiteSpace(project.Summary))
        {
            score += 15;
        }

        score += Math.Min(25, project.EvidenceItems.Count * 5);
        score += Math.Min(20, project.Keywords.Count * 4);
        return score;
    }
}
