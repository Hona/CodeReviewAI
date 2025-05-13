using System.Text;
using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Models;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Options;

namespace CodeReviewAi.Core.Services;

public class MarkdownOutputFormatter(IOptions<CodeReviewConfig> codeReviewOptions)
    : IOutputFormatter
{
    private readonly CodeReviewConfig _codeReviewConfig = codeReviewOptions.Value;

    public Task FormatAsync(
        CodeReviewContext context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context.PullRequestDetails);
        ArgumentException.ThrowIfNullOrEmpty(context.OutputDirectory);
        ArgumentException.ThrowIfNullOrEmpty(context.OutputFileName);
        ArgumentException.ThrowIfNullOrEmpty(context.ReviewPrompt);

        var sb = new StringBuilder();

        // PR Title and Description
        sb.AppendLine($"# {context.PullRequestDetails.Title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(context.PullRequestDetails.Description))
        {
            sb.AppendLine(context.PullRequestDetails.Description);
            sb.AppendLine();
        }

        // Jira Issues
        foreach (var issue in context.JiraIssues)
        {
            sb.AppendLine($"## JIRA: [{issue.Key}]({issue.Url})");
            sb.AppendLine();
            sb.AppendLine($"**{issue.Summary}**");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(issue.Description))
            {
                // Basic Jira wiki markup to Markdown conversion (can be expanded)
                var markdownDesc = issue
                    .Description.Replace("{code}", "```")
                    .Replace("{noformat}", "```");
                sb.AppendLine(markdownDesc);
                sb.AppendLine();
            }

            // Attachments
            if (issue.Attachments.Count > 0)
            {
                sb.AppendLine("### Attachments");
                foreach (var att in issue.Attachments)
                {
                    var ext = Path.GetExtension(att.FileName).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")
                    {
                        // Assumes attachments are saved relative to the markdown file
                        sb.AppendLine($"![{att.FileName}]({att.FileName})");
                    }
                    else
                    {
                        sb.AppendLine($"[Attachment: {att.FileName}]({att.FileName})");
                    }
                }
                sb.AppendLine();
            }

            // Comments
            if (issue.Comments.Count > 0)
            {
                sb.AppendLine("### Comments");
                foreach (var comment in issue.Comments)
                {
                    sb.AppendLine($"- **{comment.Author}**: {comment.Body}"); // Basic formatting
                }
                sb.AppendLine();
            }
        }

        // Diffs
        if (context.Diffs.Count > 0)
        {
            sb.AppendLine("## Diff");
            sb.AppendLine();
            sb.AppendLine("```diff");
            foreach (var diffInfo in context.Diffs)
            {
                sb.AppendLine($"diff --git a{diffInfo.FilePath} b{diffInfo.FilePath}");
                sb.AppendLine($"--- a{diffInfo.FilePath}");
                sb.AppendLine($"+++ b{diffInfo.FilePath}");
                foreach (var line in diffInfo.DiffModel.Lines)
                {
                    switch (line.Type)
                    {
                        case ChangeType.Inserted:
                            sb.AppendLine($"+{line.Text}");
                            break;
                        case ChangeType.Deleted:
                            sb.AppendLine($"-{line.Text}");
                            break;
                        case ChangeType.Unchanged:
                            if (_codeReviewConfig.IncludeUnchangedLinesInDiff)
                            {
                                sb.AppendLine($" {line.Text}");
                            }
                            break;
                        case ChangeType.Imaginary: // Typically not shown in standard diffs
                            break;
                        case ChangeType.Modified: // InlineDiffBuilder doesn't produce this directly
                            break;
                    }
                }
                sb.AppendLine(); // Add space between file diffs
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Review Prompt
        sb.AppendLine("## Review Prompt");
        sb.AppendLine();
        sb.AppendLine(context.ReviewPrompt);

        context.GeneratedMarkdown = sb.ToString();

        // File writing is handled by the builder/caller to keep formatter pure
        return Task.CompletedTask;
    }
}
