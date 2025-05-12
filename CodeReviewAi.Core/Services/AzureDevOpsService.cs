using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Models;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Options;

namespace CodeReviewAi.Core.Services;

public class AzureDevOpsService : ISourceControlProvider
{
    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string RefPrefix = "refs/heads/";

    public AzureDevOpsService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureDevOpsOptions> options
    )
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _httpClient = CreateHttpClient();
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}")
                )
            );
        return client;
    }

    public async Task<PullRequestDetails?> GetPullRequestDetailsAsync(
        int pullRequestId,
        CancellationToken cancellationToken = default
    )
    {
        var prUrl =
            $"https://dev.azure.com/{_options.Organization}/{_options.Project}/_apis/git/repositories/{_options.RepositoryId}/pullRequests/{pullRequestId}?api-version=7.2-preview";
        var prResp = await _httpClient.GetAsync(prUrl, cancellationToken);
        if (!prResp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"Failed to get PR {pullRequestId}: {prResp.StatusCode}"
            );
            return null;
        }

        var prJson = await prResp.Content.ReadAsStringAsync(cancellationToken);
        using var prDoc = JsonDocument.Parse(prJson);
        var root = prDoc.RootElement;

        var sourceRef = root.GetProperty("sourceRefName").GetString()!;
        var targetRef = root.GetProperty("targetRefName").GetString()!;
        var sourceCommit = root.GetProperty("lastMergeSourceCommit")
            .GetProperty("commitId")
            .GetString()!;
        var targetCommit = root.GetProperty("lastMergeTargetCommit")
            .GetProperty("commitId")
            .GetString()!;

        var cleanSourceRef = sourceRef.StartsWith(RefPrefix)
            ? sourceRef[RefPrefix.Length..]
            : sourceRef;
        var cleanTargetRef = targetRef.StartsWith(RefPrefix)
            ? targetRef[RefPrefix.Length..]
            : targetRef;

        // Fetch diff to get base commit AND the list of changes
        var diffUrl =
            $"https://dev.azure.com/{_options.Organization}/{_options.Project}/_apis/git/repositories/{_options.RepositoryId}/diffs/commits?baseVersion={Uri.EscapeDataString(cleanTargetRef)}&targetVersion={Uri.EscapeDataString(cleanSourceRef)}&diffCommonCommit=true&api-version=7.2-preview";
        var diffResp = await _httpClient.GetAsync(diffUrl, cancellationToken);
        if (!diffResp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"Failed to get diff for base commit/changes (PR {pullRequestId}): {diffResp.StatusCode} using URL: {diffUrl}"
            );
            return null;
        }
        var diffJson = await diffResp.Content.ReadAsStringAsync(
            cancellationToken
        );
        using var diffDoc = JsonDocument.Parse(diffJson);
        var baseCommit = diffDoc.RootElement
            .GetProperty("baseCommit")
            .GetString()!;

        // Parse the changes from this response
        var changes = new List<FileChangeInfo>();
        if (
            diffDoc.RootElement.TryGetProperty("changes", out var changesElement)
            && changesElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var change in changesElement.EnumerateArray())
            {
                if (
                    change.TryGetProperty("item", out var item)
                    && item.TryGetProperty("path", out var pathElement)
                    && item.TryGetProperty(
                        "gitObjectType",
                        out var gotElement
                    )
                    && gotElement.GetString() == "blob" // Only include files
                    && change.TryGetProperty(
                        "changeType",
                        out var ctElement
                    )
                )
                {
                    var path = pathElement.GetString();
                    var changeType = ctElement.GetString();
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(changeType))
                    {
                         changes.Add(new FileChangeInfo(path, changeType));
                    }
                }
            }
        }
        else
        {
             Console.Error.WriteLine($"No 'changes' array found in diff response for PR {pullRequestId}.");
             // Decide if this is an error or just an empty PR
        }


        return new PullRequestDetails(
            pullRequestId,
            root.GetProperty("title").GetString()!,
            root.GetProperty("description").GetString() ?? string.Empty,
            sourceRef,
            targetRef,
            sourceCommit,
            targetCommit,
            baseCommit,
            changes // Pass the parsed changes
        );
    }

    public async Task<IEnumerable<DiffInfo>> GetDiffsAsync(
        PullRequestDetails prDetails,
        CancellationToken cancellationToken = default
    )
    {
        // No API call here - use the changes from prDetails
        var diffs = new List<DiffInfo>();
        var diffBuilder = new InlineDiffBuilder(new Differ());

        if (prDetails.Changes == null || !prDetails.Changes.Any())
        {
             Console.WriteLine($"No file changes found to diff for PR {prDetails.Id}.");
             return Enumerable.Empty<DiffInfo>();
        }

        foreach (var change in prDetails.Changes)
        {
            string? baseContent = null;
            string? targetContent = null;

            // Fetch base content only if not added
            if (change.ChangeType != "add")
            {
                baseContent = await GetItemContentAsync(
                    change.Path,
                    prDetails.BaseCommit, // Content at the common ancestor
                    cancellationToken
                );
            }

            // Fetch target content only if not deleted
            if (change.ChangeType != "delete")
            {
                targetContent = await GetItemContentAsync(
                    change.Path,
                    prDetails.SourceCommit, // Content at the source branch tip commit
                    cancellationToken
                );
            }

            var diffModel = diffBuilder.BuildDiffModel(
                baseContent ?? string.Empty,
                targetContent ?? string.Empty
            );
            diffs.Add(new DiffInfo(change.Path, diffModel));
        }

        return diffs;
    }

    private async Task<string?> GetItemContentAsync(
        string path,
        string commitId,
        CancellationToken cancellationToken
    )
    {
        var itemUrl =
            $"https://dev.azure.com/{_options.Organization}/{_options.Project}/_apis/git/repositories/{_options.RepositoryId}/items?path={Uri.EscapeDataString(path)}&versionType=commit&version={commitId}&$format=text&api-version=7.2-preview.1";

        using var request = new HttpRequestMessage(HttpMethod.Get, itemUrl);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain")
        );
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/octet-stream")
        );

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Expected for added/deleted files relative to the commit being queried
            return string.Empty; // Return empty string instead of null for DiffPlex
        }

        Console.Error.WriteLine(
            $"Error fetching content for '{path}' at commit {commitId}: {response.StatusCode}"
        );
        return string.Empty; // Return empty string on error to avoid null issues
    }
}
