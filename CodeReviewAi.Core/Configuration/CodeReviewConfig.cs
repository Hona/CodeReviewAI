namespace CodeReviewAi.Core.Configuration;

public class CodeReviewConfig
{
    public const string SectionName = "CodeReview";

    public string OutputDirectory { get; init; } = "CodeReviews";
    public required string ReviewPrompt { get; init; }
    public bool IncludeUnchangedLinesInDiff { get; init; } = false;
}
