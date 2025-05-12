namespace CodeReviewAi.Core.Configuration;

public class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public required string Organization { get; init; }
    public required string Project { get; init; }
    public required string RepositoryId { get; init; }
    public required string PersonalAccessToken { get; init; }
}
