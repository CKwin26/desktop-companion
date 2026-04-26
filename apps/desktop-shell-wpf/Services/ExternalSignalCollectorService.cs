using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using DesktopCompanion.WpfHost.Models;

namespace DesktopCompanion.WpfHost.Services;

public sealed class ExternalSignalCollectorService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public ProjectExternalSignalSnapshot BuildPlaceholderSnapshot(
        ProjectMemory project,
        ProjectStateAssessment assessment)
    {
        var suggestedQueries = BuildSuggestedQueries(project);
        var referenceHints = BuildReferenceHints(project);

        var statusLabel = assessment.SearchStatusLabel switch
        {
            "required" => "required",
            "suggested" => "suggested",
            "not_needed" => "not_needed",
            _ => "not_requested"
        };

        var summary = statusLabel switch
        {
            "required" => "Needs external verification before treating the current project state as reliable.",
            "suggested" => "Would benefit from checking external standards or public feedback.",
            "not_needed" => "Local evidence is currently sufficient; no external check is needed yet.",
            _ => string.Empty
        };

        return new ProjectExternalSignalSnapshot
        {
            StatusLabel = statusLabel,
            Summary = summary,
            SuggestedQueries = suggestedQueries,
            ReferenceHints = referenceHints,
            References = [],
            UpdatedAt = DateTimeOffset.Now
        };
    }

    public async Task<ProjectExternalSignalSnapshot> CollectSignalsAsync(
        ProjectMemory project,
        ProjectStateAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        var snapshot = BuildPlaceholderSnapshot(project, assessment);
        if (assessment.SearchStatusLabel == "not_needed")
        {
            return snapshot;
        }

        var queries = snapshot.SuggestedQueries
            .Take(assessment.SearchStatusLabel == "required" ? 2 : 1)
            .ToList();

        var references = new List<ProjectExternalReference>();
        foreach (var query in queries)
        {
            var results = await SearchQueryAsync(query, cancellationToken);
            references.AddRange(results);

            references = references
                .GroupBy(reference => reference.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(6)
                .ToList();

            if (references.Count >= 4)
            {
                break;
            }
        }

        snapshot.References = RankReferences(project, references)
            .Take(6)
            .ToList();

        if (snapshot.References.Count == 0)
        {
            snapshot.Summary = assessment.SearchStatusLabel == "required"
                ? "External search was triggered, but no public references were captured in time."
                : "External search did not add enough public references yet.";
            snapshot.UpdatedAt = DateTimeOffset.Now;
            return snapshot;
        }

        snapshot.StatusLabel = "collected";
        snapshot.Summary = BuildCollectedSummary(project, snapshot.References);
        snapshot.UpdatedAt = DateTimeOffset.Now;
        return snapshot;
    }

    private static List<string> BuildSuggestedQueries(ProjectMemory project)
    {
        var queries = new List<string>();
        var queryRoot = string.IsNullOrWhiteSpace(project.Name) ? "current project" : project.Name;
        var lower = string.Join(
            " ",
            [
                project.Name,
                project.ExpectedDeliverable,
                project.WorkspaceKindLabel,
                ..project.Keywords
            ])
            .ToLowerInvariant();

        if (ContainsAny(lower, "phd", "application", "sop", "proposal", "school"))
        {
            queries.Add($"{queryRoot} application requirements best practices");
            queries.Add($"{queryRoot} statement of purpose common evaluation criteria");
            queries.Add($"{queryRoot} recommendation letters submission expectations");
        }
        else if (ContainsAny(lower, "research", "paper", "benchmark", "evaluation"))
        {
            queries.Add($"{queryRoot} benchmark evaluation best practices");
            queries.Add($"{queryRoot} common failure modes critique");
            queries.Add($"{queryRoot} literature external evaluation");
        }
        else
        {
            queries.Add($"{queryRoot} official docs");
            queries.Add($"{queryRoot} issues discussions review");
            queries.Add($"{queryRoot} alternatives community feedback");
        }

        return queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static List<string> BuildReferenceHints(ProjectMemory project)
    {
        var lower = string.Join(
            " ",
            [
                project.Name,
                project.ExpectedDeliverable,
                project.WorkspaceKindLabel,
                ..project.Keywords
            ])
            .ToLowerInvariant();

        if (ContainsAny(lower, "phd", "application", "sop", "proposal", "school"))
        {
            return ["official school pages", "program FAQs", "application guides", "faculty/lab pages"];
        }

        if (ContainsAny(lower, "research", "paper", "benchmark", "evaluation"))
        {
            return ["papers", "benchmarks", "official docs", "community critique"];
        }

        return ["official docs", "issue trackers", "discussion threads", "product/community reviews"];
    }

    private static async Task<List<ProjectExternalReference>> SearchQueryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"https://www.bing.com/search?format=rss&q={Uri.EscapeDataString(query)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd("DesktopCompanion/0.1");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = XDocument.Parse(xml);

            return document
                .Descendants("item")
                .Select(item =>
                {
                    var title = WebUtility.HtmlDecode(item.Element("title")?.Value?.Trim() ?? string.Empty);
                    var url = item.Element("link")?.Value?.Trim() ?? string.Empty;
                    var snippet = WebUtility.HtmlDecode(item.Element("description")?.Value?.Trim() ?? string.Empty);
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                    {
                        return null;
                    }

                    return new ProjectExternalReference
                    {
                        Title = title,
                        Url = url,
                        SourceHost = parsed.Host,
                        Snippet = snippet,
                        ObservedAt = DateTimeOffset.Now
                    };
                })
                .Where(reference => reference is not null)
                .Cast<ProjectExternalReference>()
                .Where(reference => !string.IsNullOrWhiteSpace(reference.Title))
                .Take(5)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<ProjectExternalReference> RankReferences(
        ProjectMemory project,
        IEnumerable<ProjectExternalReference> references)
    {
        return references
            .OrderByDescending(reference => IsAuthoritativeReference(project, reference))
            .ThenBy(reference => reference.SourceHost.Length)
            .ThenBy(reference => reference.Title.Length);
    }

    private static string BuildCollectedSummary(ProjectMemory project, IReadOnlyList<ProjectExternalReference> references)
    {
        var hostCount = references
            .Select(reference => reference.SourceHost)
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var authoritativeCount = references.Count(reference => IsAuthoritativeReference(project, reference));
        var hostSummary = string.Join(
            ", ",
            references
                .Select(reference => reference.SourceHost)
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3));

        return authoritativeCount > 0
            ? $"Collected {references.Count} external references across {hostCount} hosts, including {authoritativeCount} higher-trust sources ({hostSummary})."
            : $"Collected {references.Count} external references across {hostCount} hosts ({hostSummary}).";
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

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
    }
}
