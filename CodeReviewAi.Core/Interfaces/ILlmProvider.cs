using CodeReviewAi.Core.Models;

namespace CodeReviewAi.Core.Interfaces;

// Placeholder for future LLM integration
public interface ILlmProvider
{
    Task<string> GenerateReviewAsync(
        CodeReviewContext context,
        CancellationToken cancellationToken = default
    );
}
