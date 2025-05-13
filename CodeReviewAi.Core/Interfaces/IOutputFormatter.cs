using CodeReviewAi.Core.Models;

namespace CodeReviewAi.Core.Interfaces;

public interface IOutputFormatter
{
    Task FormatAsync(CodeReviewContext context, CancellationToken cancellationToken = default);
}
