using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Models;
using Microsoft.Extensions.Options;

namespace CodeReviewAi.Core;

public class CodeReviewBuilder
{
    private readonly CodeReviewContext _context = new();
    private readonly ISourceControlProvider _sourceControlProvider;
    private readonly IIssueTrackerProvider _jiraProvider; // Specific for now
    private readonly IOutputFormatter _outputFormatter;
    private readonly CodeReviewOptions _codeReviewOptions;

    // Inject other providers (GitHub, LLM, etc.) as needed
    public CodeReviewBuilder(
        ISourceControlProvider sourceControlProvider,
        IIssueTrackerProvider jiraProvider,
        IOutputFormatter outputFormatter,
        IOptions<CodeReviewOptions> codeReviewOptions
    )
    {
        _sourceControlProvider = sourceControlProvider;
        _jiraProvider = jiraProvider;
        _outputFormatter = outputFormatter;
        _codeReviewOptions = codeReviewOptions.Value;
        _context.ReviewPrompt = _codeReviewOptions.ReviewPrompt;
        _context.OutputDirectory = _codeReviewOptions.OutputDirectory;
    }

    public CodeReviewBuilder SetPullRequestId(int prId)
    {
        _context.PullRequestId = prId;
        return this;
    }

    public async Task<CodeReviewBuilder> AddAzureDevOpsDetailsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_context.PullRequestId <= 0)
            throw new InvalidOperationException(
                "Pull Request ID must be set before fetching details."
            );

        _context.PullRequestDetails =
            await _sourceControlProvider.GetPullRequestDetailsAsync(
                _context.PullRequestId,
                cancellationToken
            );

        if (_context.PullRequestDetails == null)
            throw new InvalidOperationException(
                $"Failed to retrieve details for PR {_context.PullRequestId}."
            );

        _context.OutputFileName = $"PR-{_context.PullRequestId}-review.md";

        return this;
    }

    public async Task<CodeReviewBuilder> AddDiffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_context.PullRequestDetails == null)
            throw new InvalidOperationException(
                "Pull Request details must be fetched before adding diffs."
            );

        var diffs = await _sourceControlProvider.GetDiffsAsync(
            _context.PullRequestDetails,
            cancellationToken
        );
        _context.Diffs.AddRange(diffs);
        return this;
    }

    public async Task<CodeReviewBuilder> AddJiraDetailsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_context.PullRequestDetails == null)
            throw new InvalidOperationException(
                "Pull Request details must be fetched before adding Jira details."
            );
        if (string.IsNullOrEmpty(_context.OutputDirectory))
            throw new InvalidOperationException(
                "Output directory must be configured."
            );

        var prDescription = _context.PullRequestDetails.Description ?? "";
        var prFolder = Path.Combine(
            _context.OutputDirectory,
            $"PR-{_context.PullRequestId}"
        );

        var issues = await _jiraProvider.GetIssuesFromTextAsync(
            prDescription,
            prFolder, // Pass directory for attachments
            cancellationToken
        );
        _context.JiraIssues.AddRange(issues);
        return this;
    }

    public async Task<CodeReviewBuilder> FormatOutputAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _outputFormatter.FormatAsync(_context, cancellationToken);
        return this;
    }

    public async Task<CodeReviewContext> BuildAsync(
        bool writeToFile = true,
        CancellationToken cancellationToken = default
    )
    {
        // Ensure output directory exists
        if (
            writeToFile
            && !string.IsNullOrEmpty(_context.OutputDirectory)
            && !string.IsNullOrEmpty(_context.OutputFileName)
            && !string.IsNullOrEmpty(_context.GeneratedMarkdown)
        )
        {
            var prFolder = Path.Combine(
                _context.OutputDirectory,
                $"PR-{_context.PullRequestId}"
            );
            Directory.CreateDirectory(prFolder);
            var fullPath = Path.Combine(prFolder, _context.OutputFileName);
            await File.WriteAllTextAsync(
                fullPath,
                _context.GeneratedMarkdown,
                cancellationToken
            );
            Console.WriteLine($"Review markdown written to: {fullPath}");
        }
        else if (writeToFile)
        {
            Console.Error.WriteLine(
                "Warning: Could not write output file. Context properties missing (OutputDirectory, OutputFileName, GeneratedMarkdown)."
            );
        }

        return _context;
    }
}
