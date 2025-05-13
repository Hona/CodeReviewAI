using CodeReviewAi.Core.Models;

namespace CodeReviewAi.Core.Interfaces;

public interface IIssueTrackerProvider
{
    Task<IEnumerable<JiraIssue>> GetIssuesFromTextAsync(
        string text,
        string targetDirectory, // For downloading attachments
        CancellationToken cancellationToken = default
    );
}
