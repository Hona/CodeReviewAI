namespace CodeReviewAi.Core.Models;

// Represents a single file change from the diff summary
public record FileChangeInfo(string Path, string ChangeType);
