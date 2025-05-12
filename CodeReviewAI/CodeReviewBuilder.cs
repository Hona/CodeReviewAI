using Microsoft.Extensions.Configuration;

namespace CodeReviewAI;

public class CodeReviewBuilder
{
    private readonly IConfiguration _configuration;
    private readonly List<ICodeReviewModule> _modules = new();
    private string _outputPath = Environment.CurrentDirectory;
    private int _prNumber;

    public CodeReviewBuilder(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public CodeReviewBuilder ForPullRequest(int prNumber)
    {
        _prNumber = prNumber;
        _outputPath = Path.Combine($"PR-{prNumber}", "review.md");
        Directory.CreateDirectory($"PR-{prNumber}");
        return this;
    }

    public CodeReviewBuilder AddAzureDevOps()
    {
        _modules.Add(new AzureDevOpsModule(_configuration));
        return this;
    }

    public CodeReviewBuilder AddJira()
    {
        _modules.Add(new JiraModule(_configuration));
        return this;
    }

    public async Task OutputMarkdownAsync()
    {
        var context = new CodeReviewContext { PrNumber = _prNumber };
        
        foreach (var module in _modules)
        {
            await module.ProcessAsync(context);
        }

        await using var writer = new StreamWriter(_outputPath);
        await writer.WriteLineAsync($"# {context.PrTitle}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(context.PrDescription);
        
        foreach (var section in context.Sections)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(section);
        }
    }
}

public interface ICodeReviewModule
{
    Task ProcessAsync(CodeReviewContext context);
}

public class CodeReviewContext
{
    public int PrNumber { get; set; }
    public string PrTitle { get; set; } = string.Empty;
    public string PrDescription { get; set; } = string.Empty;
    public List<string> Sections { get; } = new();
}

public static class CodeReviewBuilderExtensions 
{
    public static CodeReviewBuilder CreateBuilder(this IConfiguration configuration)
        => new(configuration);
}
