namespace CodeReviewAi.Core.Configuration;

public class JiraConfig
{
    public const string SectionName = "Jira";

    public required string BaseUrl { get; init; } // e.g., https://your-domain.atlassian.net
    public required string User { get; init; }
    public required string Token { get; init; }
}