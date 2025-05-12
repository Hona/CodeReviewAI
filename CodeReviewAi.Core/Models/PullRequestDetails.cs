namespace CodeReviewAi.Core.Models;

public record PullRequestDetails(
    int Id,
    string Title,
    string Description,
    string SourceRefName,
    string TargetRefName,
    string SourceCommit,
    string TargetCommit,
    string BaseCommit, // Common ancestor commit for diff
    IReadOnlyList<FileChangeInfo> Changes // List of changed files
);