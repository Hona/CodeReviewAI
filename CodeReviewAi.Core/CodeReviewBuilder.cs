using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Models;
using Microsoft.Extensions.Options;

namespace CodeReviewAi.Core;

public class CodeReviewBuilder
{
    private readonly ISourceControlProvider _sourceControlProvider;
    private readonly IIssueTrackerProvider _jiraProvider;
    private readonly IOutputFormatter _outputFormatter;

    public CodeReviewContext CurrentContext { get; } = new();

    public CodeReviewBuilder(
        ISourceControlProvider sourceControlProvider,
        IIssueTrackerProvider jiraProvider,
        IOutputFormatter outputFormatter,
        IOptions<CodeReviewConfig> codeReviewOptions
    )
    {
        _sourceControlProvider = sourceControlProvider;
        _jiraProvider = jiraProvider;
        _outputFormatter = outputFormatter;
        var codeReviewConfig = codeReviewOptions.Value;
        CurrentContext.ReviewPrompt = codeReviewConfig.ReviewPrompt;
        CurrentContext.OutputDirectory = codeReviewConfig.OutputDirectory;
    }

    public CodeReviewBuilder SetPullRequestId(int prId)
    {
        CurrentContext.PullRequestId = prId;
        CurrentContext.OutputFileName = $"PR-{CurrentContext.PullRequestId}-review.md";
        return this;
    }

    public async Task<CodeReviewBuilder> AddAzureDevOpsDetailsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (CurrentContext.PullRequestId <= 0)
            throw new InvalidOperationException(
                "Pull Request ID must be set before fetching details."
            );

        CurrentContext.PullRequestDetails =
            await _sourceControlProvider.GetPullRequestDetailsAsync(
                CurrentContext.PullRequestId,
                cancellationToken
            );

        if (CurrentContext.PullRequestDetails == null)
            throw new InvalidOperationException(
                $"Failed to retrieve details for PR {CurrentContext.PullRequestId}."
            );
        return this;
    }

    public async Task<CodeReviewBuilder> AddDiffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (CurrentContext.PullRequestDetails == null)
            throw new InvalidOperationException(
                "Pull Request details must be fetched before adding diffs."
            );

        var diffs = await _sourceControlProvider.GetDiffsAsync(
            CurrentContext.PullRequestDetails,
            cancellationToken
        );
        CurrentContext.Diffs.AddRange(diffs);
        return this;
    }

    public async Task<CodeReviewBuilder> AddJiraDetailsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (CurrentContext.PullRequestDetails == null)
            throw new InvalidOperationException(
                "Pull Request details must be fetched before adding Jira details."
            );
        if (string.IsNullOrEmpty(CurrentContext.OutputDirectory))
            throw new InvalidOperationException(
                "Output directory must be configured."
            );

        var prDescription = CurrentContext.PullRequestDetails.Description;
        var prFolder = Path.Combine(
            CurrentContext.OutputDirectory,
            $"PR-{CurrentContext.PullRequestId}"
        );
        Directory.CreateDirectory(prFolder);

        var issues = await _jiraProvider.GetIssuesFromTextAsync(
            prDescription,
            prFolder,
            cancellationToken
        );
        CurrentContext.JiraIssues.AddRange(issues);
        return this;
    }

    public async Task FormatOutputAsync(
        CancellationToken cancellationToken = default
    ) =>
        await _outputFormatter.FormatAsync(CurrentContext, cancellationToken);

    public async Task<CodeReviewContext> BuildAsync(
        bool writeToFile = true,
        CancellationToken cancellationToken = default
    )
    {
        if (!writeToFile
            || string.IsNullOrEmpty(CurrentContext.OutputDirectory)
            || string.IsNullOrEmpty(CurrentContext.OutputFileName)
            || string.IsNullOrEmpty(CurrentContext.GeneratedMarkdown))
        {
            if (writeToFile)
            {
                await Console.Error.WriteLineAsync(
                    "Warning: Could not write output file. Context properties missing (OutputDirectory, OutputFileName, GeneratedMarkdown)."
                );
            }
            
            return CurrentContext;
        }

        var prFolder = Path.Combine(
            CurrentContext.OutputDirectory,
            $"PR-{CurrentContext.PullRequestId}"
        );
        Directory.CreateDirectory(prFolder);
        var fullPath = Path.Combine(prFolder, CurrentContext.OutputFileName);
        await File.WriteAllTextAsync(
            fullPath,
            CurrentContext.GeneratedMarkdown,
            cancellationToken
        );
        Console.WriteLine($"Review markdown written to: {fullPath}");

        return CurrentContext;
    }
}
