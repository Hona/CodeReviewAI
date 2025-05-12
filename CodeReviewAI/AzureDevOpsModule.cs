using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Configuration;

namespace CodeReviewAI;

public class AzureDevOpsModule : ICodeReviewModule
{
    private readonly HttpClient _http;
    private readonly AzureDevOpsConfig _config;

    public AzureDevOpsModule(IConfiguration configuration)
    {
        _config = configuration.GetSection("AzureDevOps").Get<AzureDevOpsConfig>() ?? throw new InvalidOperationException(
            "AzureDevOps configuration section is missing or invalid."
        );
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.ASCII.GetBytes($":{_config.PersonalAccessToken}")
            )
        );
    }

    public async Task ProcessAsync(CodeReviewContext context)
    {
        var pr = await GetPullRequestAsync(context.PrNumber);
        context.PrTitle = pr.Title;
        context.PrDescription = pr.Description;

        var (baseCommit, targetCommit) = await GetCommitDiffAsync(pr.SourceRef, pr.TargetRef);
        var diff = await GenerateDiffAsync(baseCommit, targetCommit);
        
        context.Sections.Add("## Diff\n```diff\n" + diff + "\n```");
        context.Sections.Add(
            "## Review Prompt\n\nPlease review the above changes for correctness, clarity, and adherence to project standards."
        );
    }

    private async Task<PullRequestInfo> GetPullRequestAsync(int prNumber)
    {
        var url = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/git/repositories/{_config.RepositoryId}/pullRequests/{prNumber}?api-version=7.2-preview";
        var response = await _http.GetFromJsonAsync<JsonElement>(url);
        
        return new PullRequestInfo
        {
            Title = response.GetProperty("title").GetString() ?? string.Empty,
            Description = response.GetProperty("description").GetString() ?? string.Empty,
            SourceRef = CleanBranchName(response.GetProperty("sourceRefName").GetString() ?? string.Empty),
            TargetRef = CleanBranchName(response.GetProperty("targetRefName").GetString() ?? string.Empty),
        };
    }

    private string CleanBranchName(string refName) => 
        refName.StartsWith("refs/heads/") ? refName["refs/heads/".Length..] : refName;

    private record PullRequestInfo
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string SourceRef { get; init; }
        public required string TargetRef { get; init; }
    }

    private record AzureDevOpsConfig
    {
        public required string Organization { get; init; }
        public required string Project { get; init; }
        public required string RepositoryId { get; init; }
        public required string PersonalAccessToken { get; init; }
    }
private async Task<(string BaseCommit, string TargetCommit)> GetCommitDiffAsync(
    string sourceRef,
    string targetRef
)
{
    // This URL should use the branch names directly
    var url = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/git/repositories/{_config.RepositoryId}/diffs/commits?baseVersion={Uri.EscapeDataString(targetRef)}&targetVersion={Uri.EscapeDataString(sourceRef)}&diffCommonCommit=true&api-version=7.2-preview";
    
    var diffResp = await _http.GetAsync(url);
    if (!diffResp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Failed to get diff: {diffResp.StatusCode}");
    }
    
    var diffJson = await diffResp.Content.ReadAsStringAsync();
    var diffDoc = JsonDocument.Parse(diffJson);
    
    var baseCommit = diffDoc.RootElement.GetProperty("baseCommit").GetString();
    var targetCommit = diffDoc.RootElement.GetProperty("targetCommit").GetString();
    
    if (string.IsNullOrEmpty(baseCommit) || string.IsNullOrEmpty(targetCommit))
    {
        throw new InvalidOperationException("Failed to get base or target commit.");
    }
    
    return (baseCommit, targetCommit);
}

private async Task<string> GenerateDiffAsync(string baseCommit, string targetCommit)
{
    // Specify versionType=commit to indicate we're using commit hashes
    var diffUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/git/repositories/{_config.RepositoryId}/diffs/commits?baseVersionType=commit&baseVersion={baseCommit}&targetVersionType=commit&targetVersion={targetCommit}&diffCommonCommit=true&api-version=7.2-preview";
    Console.WriteLine($"Fetching diff using URL: {diffUrl}");
    
    var diffResp = await _http.GetAsync(diffUrl);
    if (!diffResp.IsSuccessStatusCode)
    {
        Console.WriteLine($"Failed to get diff: {diffResp.StatusCode}");
        var errorContent = await diffResp.Content.ReadAsStringAsync();
        Console.WriteLine($"Error content: {errorContent}");
        return string.Empty;
    }
    
    var diffJson = await diffResp.Content.ReadAsStringAsync();
    var diffDoc = JsonDocument.Parse(diffJson);
    var result = new StringBuilder();

    foreach (var change in diffDoc.RootElement.GetProperty("changes").EnumerateArray())
    {
        var item = change.GetProperty("item");
        var path = item.GetProperty("path").GetString();
        var gitObjectType = item.GetProperty("gitObjectType").GetString();

        if (gitObjectType != "blob")
            continue;

        var baseUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/git/repositories/{_config.RepositoryId}/items?path={Uri.EscapeDataString(path)}&versionType=commit&version={baseCommit}&api-version=7.2-preview.1";
        var targetUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/git/repositories/{_config.RepositoryId}/items?path={Uri.EscapeDataString(path)}&versionType=commit&version={targetCommit}&api-version=7.2-preview.1";

        var baseResp = await _http.GetAsync(baseUrl);
        var targetResp = await _http.GetAsync(targetUrl);

        var baseContent = baseResp.IsSuccessStatusCode
            ? await baseResp.Content.ReadAsStringAsync()
            : string.Empty;
        var targetContent = targetResp.IsSuccessStatusCode
            ? await targetResp.Content.ReadAsStringAsync()
            : string.Empty;

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(baseContent, targetContent);

        result.AppendLine($"diff --git a{path} b{path}");
        result.AppendLine($"--- a{path}");
        result.AppendLine($"+++ b{path}");

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    result.AppendLine($"+{line.Text}");
                    break;
                case ChangeType.Deleted:
                    result.AppendLine($"-{line.Text}");
                    break;
                case ChangeType.Unchanged:
                    result.AppendLine($" {line.Text}");
                    break;
                case ChangeType.Imaginary:
                    break;
            }
        }
    }

    return result.ToString();
}


}
