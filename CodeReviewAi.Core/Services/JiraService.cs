using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Models;
using Microsoft.Extensions.Options;

namespace CodeReviewAi.Core.Services;

public partial class JiraService : IIssueTrackerProvider
{
    private readonly HttpClient _httpClient;
    private readonly JiraConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    // Regex to find Jira issue URLs (adjust if your URL structure differs)
    [GeneratedRegex(@"https?://[^\s/]+\.atlassian\.net/browse/([A-Z]+-\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex JiraIssueUrlRegex();

    public JiraService(
        IHttpClientFactory httpClientFactory,
        IOptions<JiraConfig> options
    )
    {
        _httpClientFactory = httpClientFactory;
        _config = options.Value;
        _httpClient = CreateHttpClient();
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("Jira");
        client.BaseAddress = new Uri($"{_config.BaseUrl}/rest/api/2/"); // Use API v2
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_config.User}:{_config.Token}")
                )
            );
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
        return client;
    }

    public async Task<IEnumerable<JiraIssue>> GetIssuesFromTextAsync(
        string text,
        string targetDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var issueKeys = JiraIssueUrlRegex()
            .Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var issues = new List<JiraIssue>();
        foreach (var key in issueKeys)
        {
            var issue = await GetIssueDetailsAsync(
                key,
                targetDirectory,
                cancellationToken
            );
            if (issue != null)
            {
                issues.Add(issue);
            }
        }
        return issues;
    }

    private async Task<JiraIssue?> GetIssueDetailsAsync(
        string issueKey,
        string targetDirectory,
        CancellationToken cancellationToken
    )
    {
        var issueUrl = $"issue/{issueKey}?fields=summary,description,attachment,comment";
        var response = await _httpClient.GetAsync(issueUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Log error
            await Console.Error.WriteLineAsync(
                $"Failed to get Jira issue {issueKey}: {response.StatusCode}"
            );
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fields = root.GetProperty("fields");

        var summary = fields.GetProperty("summary").GetString() ?? "N/A";
        var description = fields.GetProperty("description").GetString() ?? "";
        var issuePageUrl = $"{_config.BaseUrl}/browse/{issueKey}";

        var attachments = await ProcessAttachmentsAsync(
            fields,
            targetDirectory,
            cancellationToken
        );
        var comments = ProcessComments(fields);

        return new JiraIssue(
            issueKey,
            issuePageUrl,
            summary,
            description,
            attachments,
            comments
        );
    }

    private async Task<List<JiraAttachment>> ProcessAttachmentsAsync(
        JsonElement fields,
        string targetDirectory,
        CancellationToken cancellationToken
    )
    {
        var attachments = new List<JiraAttachment>();
        if (
            fields.TryGetProperty("attachment", out var attachmentArray)
            && attachmentArray.ValueKind == JsonValueKind.Array
        )
        {
            Directory.CreateDirectory(targetDirectory); // Ensure dir exists
            foreach (var att in attachmentArray.EnumerateArray())
            {
                var filename = att.GetProperty("filename").GetString();
                var contentUrl = att.GetProperty("content").GetString();
                if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(contentUrl))
                    continue;

                // Use a separate client for downloading content which might not need Jira base URL/auth
                using var contentClient = _httpClientFactory.CreateClient(
                    "JiraAttachmentDownloader"
                );
                // Jira attachment URLs often require auth
                contentClient.DefaultRequestHeaders.Authorization =
                    _httpClient.DefaultRequestHeaders.Authorization;

                var attResp = await contentClient.GetAsync(
                    contentUrl,
                    cancellationToken
                );
                if (attResp.IsSuccessStatusCode)
                {
                    var attBytes = await attResp.Content.ReadAsByteArrayAsync(
                        cancellationToken
                    );
                    var localPath = Path.Combine(targetDirectory, filename);
                    await File.WriteAllBytesAsync(
                        localPath,
                        attBytes,
                        cancellationToken
                    );
                    // Store relative path or just filename for markdown link
                    attachments.Add(
                        new JiraAttachment(filename, contentUrl, attBytes)
                    );
                }
                else
                {
                    await Console.Error.WriteLineAsync(
                        $"Failed to download attachment {filename}: {attResp.StatusCode}"
                    );
                }
            }
        }
        return attachments;
    }

    private List<JiraComment> ProcessComments(JsonElement fields)
    {
        var comments = new List<JiraComment>();
        if (
            fields.TryGetProperty("comment", out var commentRoot)
            && commentRoot.TryGetProperty("comments", out var commentArray)
            && commentArray.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var comment in commentArray.EnumerateArray())
            {
                var author = comment.GetProperty("author")
                    .GetProperty("displayName")
                    .GetString();
                var body = comment.GetProperty("body").GetString();
                if (!string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(body))
                {
                    comments.Add(new JiraComment(author, body));
                }
            }
        }
        return comments;
    }
}
