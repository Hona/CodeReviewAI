using CodeReviewAi.Core.Models;

namespace CodeReviewAi.Core.Interfaces;

public interface ISourceControlProvider
{
    Task<PullRequestDetails?> GetPullRequestDetailsAsync(
        int pullRequestId,
        CancellationToken cancellationToken = default
    );
    Task<IEnumerable<DiffInfo>> GetDiffsAsync(
        PullRequestDetails prDetails,
        CancellationToken cancellationToken = default
    );
}
