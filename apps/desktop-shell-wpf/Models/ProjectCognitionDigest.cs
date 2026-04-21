namespace DesktopCompanion.WpfHost.Models;

public sealed class ProjectCognitionDigest
{
    public string Summary { get; set; } = string.Empty;

    public string FollowUpPrompt { get; set; } = string.Empty;

    public string SuggestedFocus { get; set; } = string.Empty;

    public List<string> NowItems { get; set; } = [];

    public List<string> NextItems { get; set; } = [];

    public List<string> LaterItems { get; set; } = [];

    public List<ProjectDigestProject> Projects { get; set; } = [];
}

public sealed class ProjectDigestProject
{
    public string Name { get; set; } = string.Empty;

    public string MatchType { get; set; } = "candidate";

    public string Summary { get; set; } = string.Empty;

    public string Priority { get; set; } = "next";

    public string NextAction { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public List<string> Items { get; set; } = [];
}
