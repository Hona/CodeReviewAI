using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CodeReviewAI;

public class JiraModule : ICodeReviewModule
{
    private readonly HttpClient _http;

    public JiraModule(IConfiguration configuration)
    {
        var config = configuration.GetSection("Jira").Get<JiraConfig>() 
                     ?? throw new InvalidOperationException("Jira configuration is missing");;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{config.User}:{config.Token}")
            )
        );
    }

    public async Task ProcessAsync(CodeReviewContext context)
    {
        var jiraUrl = Regex.Match(
            context.PrDescription,
            @"https?://[^\s]+/browse/[A-Z]+-\d+"
        ).Value;
        
        if (string.IsNullOrEmpty(jiraUrl)) return;

        var issueKey = jiraUrl.Split('/').Last();
        var issue = await GetJiraIssueAsync(jiraUrl, issueKey);
        
        var sb = new StringBuilder();
        sb.AppendLine($"## JIRA: [{issueKey}]({jiraUrl})");
        sb.AppendLine();
        sb.AppendLine($"**{issue.Summary}**");
        sb.AppendLine();
        sb.AppendLine(issue.Description);

        if (issue.Attachments?.Any() == true)
        {
            sb.AppendLine();
            foreach (var att in issue.Attachments)
            {
                await DownloadAttachmentAsync(att, context.PrNumber);
                sb.AppendLine(FormatAttachmentMarkdown(att));
            }
        }

        if (issue.Comments?.Any() == true)
        {
            sb.AppendLine();
            sb.AppendLine("### JIRA Comments");
            foreach (var comment in issue.Comments)
            {
                sb.AppendLine($"- **{comment.Author}**: {comment.Body}");
            }
        }

        context.Sections.Add(sb.ToString());
    }

    private async Task<JiraIssue> GetJiraIssueAsync(string jiraUrl, string issueKey)
    {
        var baseUrl = jiraUrl[..jiraUrl.IndexOf("/browse/")];
        var apiUrl = $"{baseUrl}/rest/api/2/issue/{issueKey}";
        
        var response = await _http.GetFromJsonAsync<JsonElement>(apiUrl);
        var fields = response.GetProperty("fields");

        return new JiraIssue
        {
            Summary = fields.GetProperty("summary").GetString(),
            Description = fields.GetProperty("description").GetString(),
            Attachments = GetAttachments(fields),
            Comments = GetComments(fields)
        };
    }

    private IEnumerable<AttachmentInfo> GetAttachments(JsonElement fields)
    {
        if (!fields.TryGetProperty("attachment", out var attachments)) 
            return Enumerable.Empty<AttachmentInfo>();

        return attachments.EnumerateArray().Select(a => new AttachmentInfo
        {
            Filename = a.GetProperty("filename").GetString(),
            Url = a.GetProperty("content").GetString()
        });
    }

    private IEnumerable<CommentInfo> GetComments(JsonElement fields)
    {
        if (!fields.TryGetProperty("comment", out var comments) || 
            !comments.TryGetProperty("comments", out var commentArr))
            return Enumerable.Empty<CommentInfo>();

        return commentArr.EnumerateArray().Select(c => new CommentInfo
        {
            Author = c.GetProperty("author").GetProperty("displayName").GetString(),
            Body = c.GetProperty("body").GetString()
        });
    }

    private async Task DownloadAttachmentAsync(AttachmentInfo attachment, int prNumber)
    {
        var response = await _http.GetAsync(attachment.Url);
        if (!response.IsSuccessStatusCode) return;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var path = Path.Combine($"PR-{prNumber}", attachment.Filename);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private string FormatAttachmentMarkdown(AttachmentInfo attachment)
    {
        var isImage = new[] { ".png", ".jpg", ".jpeg", ".gif" }
            .Any(ext => attachment.Filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        return isImage
            ? $"![{attachment.Filename}]({attachment.Filename})"
            : $"[Attachment: {attachment.Filename}]({attachment.Filename})";
    }

    private record JiraConfig
    {
        public string User { get; init; }
        public string Token { get; init; }
    }

    private record JiraIssue
    {
        public string Summary { get; init; }
        public string Description { get; init; }
        public IEnumerable<AttachmentInfo> Attachments { get; init; }
        public IEnumerable<CommentInfo> Comments { get; init; }
    }

    private record AttachmentInfo
    {
        public string Filename { get; init; }
        public string Url { get; init; }
    }

    private record CommentInfo
    {
        public string Author { get; init; }
        public string Body { get; init; }
    }
}
