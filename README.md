# Code Review AI 🤖

Tired of the copy-paste dance? You know the one: grab the PR title, description, hunt down the linked Jira ticket, copy *its* details, generate a diff, and *then* finally paste it all into your AI assistant for a code review? 😩

Yeah, that's tedious. This tool is the first step to automating that pain away.

## What's the Point?

This little console app acts like your **PR Context Butler**. Give it an Azure DevOps Pull Request ID, and it does the legwork:

1.  **Fetches PR Details:** Grabs the title and description from Azure DevOps.
2.  **Finds Linked Issues:** Scans the PR description for Jira issue URLs (like `https://your-co.atlassian.net/browse/TASK-123`).
3.  **Gets Jira Context:** Pulls the Jira issue's summary, description, comments, and even downloads any attachments right next to the output file!
4.  **Generates the Diff:** Calculates the code changes between the source and target branches.
5.  **Bundles Everything:** Creates a clean Markdown file (`.md`) containing all the gathered info, perfectly formatted for an LLM.

## The Current Workflow (Semi-Manual)

Right now, it handles the *gathering* part. The *review* part still needs you (and your favorite AI):

1.  Run the tool: `code-review --pr <YourPrId>`
2.  It connects to AzDO & Jira, does its magic ✨.
3.  It creates a folder like `CodeReviews/PR-<YourPrId>/` containing:
    *   `PR-<YourPrId>-review.md`: The main context file.
    *   Any attachments downloaded from linked Jira issues.
4.  **Manual Step:** Copy all files from the folder.
5.  Paste that content into your AI chat (like t3.chat 😉, ChatGPT, Claude, etc.).
6.  Send the prompt and wait for the review.

It saves a surprising amount of time and ensures you don't forget any crucial context!

## Getting Started

1.  **Clone the repo.**
2.  **Configure `CodeReviewAi.Cli/appsettings.json`:**
    *   **`AzureDevOps`**:
        *   `Organization`: Your AzDO org name.
        *   `Project`: Your AzDO project name.
        *   `RepositoryId`: The ID or name of your repo.
        *   `PersonalAccessToken`: Create an AzDO PAT with permissions:
            *   `Code` > `Read`
            *   `Pull Request Threads` > `Read & write` (needed for future auto-commenting)
    *   **`Jira`**:
        *   `BaseUrl`: Your Jira instance URL (e.g., `https://your-co.atlassian.net`).
        *   `User`: Your Jira login email.
        *   `Token`: Create a Jira API Token (from your account settings > security).
    *   **`CodeReview`**:
        *   `OutputDirectory`: Where to save the output folders (default: `CodeReviews`).
        *   `ReviewPrompt`: The default text added to the end of the Markdown file.
        *   `IncludeUnchangedLinesInDiff`: `true` for full-file diffs, `false` (default) for concise patch-style diffs.
3.  **Build:** Build the `CodeReviewAi.Cli` project.
4.  **Install:**
    *   From source: Run `install-dev.bat`
    *   From NuGet: `dotnet tool install --global Hona.CodeReviewAi`
5.  **Run:**
    *   `code-review --pr <YourPrId>`

## What's Next? 🚀

This is just the MVP! The goal is full autonomy:

*   Direct integration with LLMs (OpenAI-compatible APIs, maybe local models like Ollama).
*   Automatically generating review comments.
*   Posting those comments directly back to the Azure DevOps PR thread.
*   Support for other providers (GitHub, GitLab, etc.).

Feel free to contribute or raise issues!

### Template appsettings.json

```json
{
  "AzureDevOps": {
    "Organization": "",
    "Project": "",
    "RepositoryId": "",
    "PersonalAccessToken": ""
  },
  "Jira": {
    "BaseUrl": "https://org.atlassian.net",
    "User": "",
    "Token": ""
  },
  "CodeReview": {
    "OutputDirectory": "CodeReviews",
    "ReviewPrompt": "Please review the above changes for correctness, clarity, and adherence to project standards. Highlight any issues, improvements, or questions below.",
    "IncludeUnchangedLinesInDiff": true
  }
}
```