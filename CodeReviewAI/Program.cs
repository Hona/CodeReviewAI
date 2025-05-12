using CodeReviewAI;
using Microsoft.Extensions.Configuration;

var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
var configuration = builder.Build();

Console.WriteLine("Input PR number:");
var rawPrNumber = Console.ReadLine();
if (string.IsNullOrEmpty(rawPrNumber) || !int.TryParse(rawPrNumber, out var prNumber))
{
    Console.WriteLine("Invalid PR number.");
    return;
}

await configuration.CreateBuilder()
    .ForPullRequest(prNumber)
    .AddAzureDevOps()
    .AddJira()
    .OutputMarkdownAsync();