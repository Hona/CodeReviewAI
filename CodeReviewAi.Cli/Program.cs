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
using Microsoft.Extensions.Options;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services
                .AddOptions<AzureDevOpsConfig>()
                .Bind(configuration.GetSection(AzureDevOpsConfig.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services
                .AddOptions<JiraConfig>()
                .Bind(configuration.GetSection(JiraConfig.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services
                .AddOptions<CodeReviewConfig>()
                .Bind(configuration.GetSection(CodeReviewConfig.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHttpClient();
            services.AddHttpClient("AzureDevOps");
            services.AddHttpClient("Jira");
            services.AddHttpClient("JiraAttachmentDownloader");

            services.AddSingleton<ISourceControlProvider, AzureDevOpsService>();
            services.AddSingleton<IIssueTrackerProvider, JiraService>();
            services.AddSingleton<IOutputFormatter, MarkdownOutputFormatter>();

            services.AddTransient<CodeReviewBuilder>();

            // Add ILlmProvider implementation here when ready
            // services.AddSingleton<ILlmProvider, ManualPromptProvider>();
        }
    )
    .Build();

var serviceProvider = host.Services;

var prOption = new Option<int>("--pr", description: "The Azure DevOps Pull Request ID.")
{
    IsRequired = true,
};

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

            builder.SetPullRequestId(prId);
            await builder.AddAzureDevOpsDetailsAsync(cancellationToken);

            // Check if PR details were successfully fetched using the public property
            if (builder.CurrentContext.PullRequestDetails != null)
            {
                await builder.AddJiraDetailsAsync(cancellationToken);
                await builder.AddDiffsAsync(cancellationToken);
                await builder.FormatOutputAsync(cancellationToken);
            }
            else
            {
                // Handle the case where PR details could not be fetched,
                // e.g., log a specific message. The exception in AddAzureDevOpsDetailsAsync
                // would have already been thrown and caught by the outer try-catch.
                // This block is more for logic that depends on PR details being present.
                Console.Error.WriteLine(
                    $"Skipping further processing as PR details for {prId} could not be fetched."
                );
            }

            var reviewContext = await builder.BuildAsync(writeToFile: true, cancellationToken);

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
            Console.Error.WriteLine(ex.StackTrace);
            Console.ResetColor();
            context.ExitCode = 1;
        }
    }
);

return await rootCommand.InvokeAsync(args);
