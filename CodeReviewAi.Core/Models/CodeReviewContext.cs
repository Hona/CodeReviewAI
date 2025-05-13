namespace CodeReviewAi.Core.Models;

public class CodeReviewContext
{
    public int PullRequestId { get; set; }
    public PullRequestDetails? PullRequestDetails { get; set; }
    public List<DiffInfo> Diffs { get; } = [];
    public List<JiraIssue> JiraIssues { get; } = [];
    public string? ReviewPrompt { get; set; }
    public string? OutputDirectory { get; set; }
    public string? OutputFileName { get; set; }
    public string? GeneratedMarkdown { get; set; }
    // Add properties for other providers (GitHub, Confluence, etc.) as needed
}
