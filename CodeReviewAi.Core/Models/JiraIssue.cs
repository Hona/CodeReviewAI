namespace CodeReviewAi.Core.Models;

public record JiraAttachment(string FileName, string Url, byte[] Content);

public record JiraComment(string Author, string Body);

public record JiraIssue(
    string Key,
    string Url,
    string Summary,
    string Description,
    IReadOnlyList<JiraAttachment> Attachments,
    IReadOnlyList<JiraComment> Comments
);