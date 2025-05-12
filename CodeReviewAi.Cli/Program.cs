using System.CommandLine;
using System.CommandLine.Invocation;
using CodeReviewAi.Core;
using CodeReviewAi.Core.Configuration;
using CodeReviewAi.Core.Interfaces;
using CodeReviewAi.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// Set up DI
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AzureDevOpsOptions>()
            .Bind(configuration.GetSection(AzureDevOpsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JiraOptions>()
            .Bind(configuration.GetSection(JiraOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CodeReviewOptions>()
            .Bind(configuration.GetSection(CodeReviewOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure HttpClientFactory
        services.AddHttpClient();
        // Named client for AzDO (can add specific policies like Polly later)
        services.AddHttpClient("AzureDevOps");
        // Named client for Jira (can add specific policies)
        services.AddHttpClient("Jira");
        // Named client for Jira Attachments (might have different base URL or auth needs)
        services.AddHttpClient("JiraAttachmentDownloader");

        // Register services
        services.AddSingleton<ISourceControlProvider, AzureDevOpsService>();
        services.AddSingleton<IIssueTrackerProvider, JiraService>();
        services.AddSingleton<IOutputFormatter, MarkdownOutputFormatter>();
        // Add ILlmProvider implementation here when ready
        // services.AddSingleton<ILlmProvider, ManualPromptProvider>();

        // Register the builder itself
        services.AddTransient<CodeReviewBuilder>();
    })
    .Build();

var serviceProvider = host.Services;

// Set up System.CommandLine
var prOption = new Option<int>(
    "--pr",
    description: "The Azure DevOps Pull Request ID."
)
{ IsRequired = true };

var rootCommand = new RootCommand(
    "Generates context for AI code review from AzDO PRs and linked Jira issues."
);
rootCommand.AddOption(prOption);

rootCommand.SetHandler(
    async (InvocationContext context) =>
    {
        var prId = context.ParseResult.GetValueForOption(prOption);
        var cancellationToken = context.GetCancellationToken();

        try
        {
            var builder = serviceProvider.GetRequiredService<CodeReviewBuilder>();

            var reviewContext = await (await (await ((await (await builder.SetPullRequestId(prId)
                .AddAzureDevOpsDetailsAsync(cancellationToken))
                .AddJiraDetailsAsync(cancellationToken)) // Assumes Jira links are in PR description
                .AddDiffsAsync(cancellationToken)))
                .FormatOutputAsync(cancellationToken))
                .BuildAsync(writeToFile: true, cancellationToken); // Build and write file

            // In manual mode, the markdown is generated.
            // In autonomous mode, you'd pass reviewContext to an LLM provider here.
            // e.g., var llmProvider = serviceProvider.GetRequiredService<ILlmProvider>();
            // var reviewComments = await llmProvider.GenerateReviewAsync(reviewContext, cancellationToken);
            // Then push comments back via ISourceControlProvider.AddReviewCommentsAsync(...)

            Console.WriteLine("Code review context generation complete.");
            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace); // Optional: for debugging
            Console.ResetColor();
            context.ExitCode = 1;
        }
    }
);

// Execute command line app
return await rootCommand.InvokeAsync(args);
