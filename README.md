# Chat Agent - Multi-Turn Conversation

A console-based chat application built with the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview) that demonstrates multi-turn conversations with an LLM.

## Features

- 🤖 Multi-turn conversations with context persistence
- 💬 Interactive chat interface with colored output
- 🛠️ Function calling support (example: weather tool)
- 📡 Streaming responses for real-time feedback
- 🔄 Conversation thread management

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- An API key from one of the supported providers:
  - OpenAI
  - Azure OpenAI
  - GitHub Models (free tier available)

## Installation

1. Clone or navigate to this repository:
   ```powershell
   cd "c:\Repos\Agent Framework Georgie"
   ```

2. Restore the NuGet packages (if not already done):
   ```powershell
   dotnet restore
   ```

## Configuration

### Option 1: OpenAI

Set your OpenAI API key as an environment variable:

**Windows (PowerShell):**
```powershell
$env:OPENAI_API_KEY='your-api-key-here'
```

**Linux/Mac:**
```bash
export OPENAI_API_KEY='your-api-key-here'
```

### Option 2: Azure OpenAI

To use Azure OpenAI instead, modify the `Program.cs` file to use `AzureOpenAIClient`:

```csharp
// Replace the OpenAIClient initialization with:
using Microsoft.Agents.AI.AzureOpenAI;

AIAgent agent = new AzureOpenAIClient(
    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!)
)
.CreateAIAgent(
    AgentInstructions,
    AgentName,
    tools: [AIFunctionFactory.Create(GetWeather)],
    new AzureOpenAIChatCompletionOptions
    {
        DeploymentName = "your-deployment-name"
    }
);
```

Then set the environment variables:
```powershell
$env:AZURE_OPENAI_ENDPOINT='https://your-resource.openai.azure.com/'
$env:AZURE_OPENAI_API_KEY='your-azure-openai-key'
```

### Option 3: GitHub Models (Free Tier)

GitHub Models offers free access to various AI models. Modify the initialization to:

```csharp
AIAgent agent = new OpenAIClient(
    Environment.GetEnvironmentVariable("GITHUB_TOKEN")!,
    new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") }
)
.CreateAIAgent(
    AgentInstructions,
    AgentName,
    tools: [AIFunctionFactory.Create(GetWeather)],
    new OpenAIChatCompletionOptions
    {
        ModelId = "gpt-4o-mini"
    }
);
```

Then set your GitHub token:
```powershell
$env:GITHUB_TOKEN='your-github-token'
```

## Running the Application

Build and run the application:

```powershell
dotnet run
```

You'll see a chat interface where you can interact with the agent:

```
╔═══════════════════════════════════════════════╗
║     Chat Agent - Multi-Turn Conversation     ║
╚═══════════════════════════════════════════════╝

Chat with Georgie! (Type 'exit' or 'quit' to end the conversation)
────────────────────────────────────────────────────────────

You: Hello!
Georgie: Hello! How can I assist you today?

You: What's the weather like in Seattle?
Georgie: The weather in Seattle is rainy with a high of 18°C.

You: exit
Georgie: Goodbye! Have a great day!
```

## How It Works

The application uses the Agent Framework to:

1. **Create an AI Agent** - Initializes an agent with custom instructions
2. **Manage Conversation State** - Uses `AgentThread` to maintain context across multiple turns
3. **Stream Responses** - Displays agent responses in real-time using `RunStreamingAsync`
4. **Function Calling** - Demonstrates tool usage with a weather function

### Key Components

- **AgentThread**: Maintains conversation history between calls
- **RunStreamingAsync**: Provides real-time response streaming
- **AIFunctionFactory**: Enables the agent to call custom functions

## Customization

### Change the Agent's Personality

Modify the `AgentInstructions` constant in `Program.cs`:

```csharp
const string AgentInstructions = "You are a helpful and friendly assistant named Georgie. You engage in natural conversations and can help with various tasks.";
```

### Add More Tools

Add new functions with the `[Description]` attribute:

```csharp
[Description("Calculate the sum of two numbers.")]
public static int Add(
    [Description("The first number")] int a,
    [Description("The second number")] int b
)
{
    return a + b;
}
```

Then include them in the agent creation:

```csharp
tools: [
    AIFunctionFactory.Create(GetWeather),
    AIFunctionFactory.Create(Add)
]
```

### Change the Model

Update the `ModelId` in the options:

```csharp
new OpenAIChatCompletionOptions
{
    ModelId = "gpt-4o"  // or gpt-3.5-turbo, gpt-4, etc.
}
```

## Important Notes

⚠️ **The `--prerelease` flag is required** when installing Agent Framework packages as it's currently in preview.

💡 **Conversation History**: The `AgentThread` object stores the conversation history locally and sends it with each request when using ChatCompletion services.

🔒 **API Keys**: Never commit API keys to source control. Always use environment variables or secure configuration management.

## Learn More

- [Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview)
- [Multi-Turn Conversation Tutorial](https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/multi-turn-conversation?pivots=programming-language-csharp)
- [Agent Framework GitHub Repository](https://github.com/microsoft/agent-framework)

## Troubleshooting

### "OPENAI_API_KEY environment variable not set"

Make sure you've set the environment variable in your current PowerShell session. The variable is session-specific, so you'll need to set it each time you open a new terminal.

### Rate Limit Errors

If you encounter rate limiting, consider:
- Using a different model tier
- Adding delays between requests
- Upgrading your API plan

### Package Restore Issues

If you encounter issues restoring packages, ensure you're using the `--prerelease` flag:

```powershell
dotnet add package Microsoft.Agents.AI --prerelease
dotnet add package Microsoft.Agents.AI.OpenAI --prerelease
```

## Adding Azure AI Search as a Knowledge Source

### Overview

You can enhance your agent with Retrieval Augmented Generation (RAG) by connecting it to Azure AI Search. This allows the agent to answer questions based on your own documents and data.

### Prerequisites

1. An Azure AI Search service
2. A search index with your documents
3. API credentials for the search service

### Setup Steps

#### 1. Install Azure Search Package

```powershell
dotnet add package Azure.Search.Documents
```

#### 2. Set Environment Variables

```powershell
$env:AZURE_SEARCH_ENDPOINT='https://your-search-service.search.windows.net'
$env:AZURE_SEARCH_API_KEY='your-search-api-key'
$env:AZURE_SEARCH_INDEX_NAME='your-index-name'
```

#### 3. Use the Example with Search

The repository includes `ProgramWithSearch.cs` which demonstrates Azure AI Search integration. To use it:

1. Modify `ChatAgent.csproj` to use `ProgramWithSearch.cs` as the entry point, or
2. Copy the search implementation from `ProgramWithSearch.cs` into your `Program.cs`

### Key Components

#### TextSearchProvider

The `TextSearchProvider` is the key component that enables RAG:

```csharp
var textSearchOptions = new TextSearchProviderOptions
{
    // Run search before every AI invocation
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    
    // Include recent messages in search context
    RecentMessageMemoryLimit = 5,
    
    // Custom prompts for context and citations
    ContextPrompt = "Use the following information...",
    CitationsPrompt = "Cite sources when available."
};
```

#### Search Adapter Function

Create an adapter function that queries Azure AI Search:

```csharp
async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchKnowledgeBase(
    string query, 
    CancellationToken ct)
{
    var searchOptions = new SearchOptions
    {
        Size = 5,
        Select = { "content", "title", "url" }
    };

    SearchResults<SearchDocument> response = 
        await searchClient.SearchAsync<SearchDocument>(query, searchOptions, ct);
    
    // Convert to TextSearchResult format
    // ...
}
```

#### Creating the Agent with Search

```csharp
AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4o-mini")
    .CreateAIAgent(new ChatClientAgentOptions
    {
        Name = AgentName,
        ChatOptions = new ChatOptions { Instructions = AgentInstructions },
        AIContextProviderFactory = ctx => new TextSearchProvider(
            SearchKnowledgeBase,
            ctx.SerializedState,
            ctx.JsonSerializerOptions,
            textSearchOptions
        )
    });
```

### Index Schema Requirements

Your Azure AI Search index should have fields for:
- **content**: The main text content to search
- **title**: Document title or name
- **url**: (Optional) Link to the source document

Adjust the field names in the code to match your index schema:

```csharp
Select = { "content", "title", "url" }, // Your field names here
```

### Search Modes

The TextSearchProvider supports two search behaviors:

1. **BeforeAIInvoke** (Recommended): Automatically searches before each AI call
2. **OnDemandFunctionCalling**: Exposes search as a tool the agent can call when needed

### Advanced Features

#### Vector Search

For semantic search using embeddings, configure your index with vector fields and use hybrid search:

```csharp
var searchOptions = new SearchOptions
{
    Size = 5,
    QueryType = SearchQueryType.Semantic,
    SemanticSearch = new SemanticSearchOptions
    {
        SemanticConfigurationName = "my-semantic-config"
    }
};
```

#### Custom Context Formatting

Customize how search results are formatted:

```csharp
var textSearchOptions = new TextSearchProviderOptions
{
    ContextFormatter = results => 
    {
        // Custom formatting logic
        return string.Join("\n\n", results.Select(r => 
            $"Source: {r.SourceName}\n{r.Text}"));
    }
};
```

#### Filtering Results

Add filters to restrict search results:

```csharp
var searchOptions = new SearchOptions
{
    Filter = "category eq 'documentation' and published eq true",
    Size = 5
};
```

### Example Queries

Once configured, your agent can answer questions based on your knowledge base:

- "What is our return policy?"
- "How do I configure the product?"
- "Tell me about feature X from the documentation"

The agent will automatically search your index and provide answers with source citations.

### Troubleshooting

**Issue**: "Search error: The remote name could not be resolved"
- Check that `AZURE_SEARCH_ENDPOINT` is correctly formatted
- Verify network connectivity to Azure

**Issue**: "403 Forbidden"
- Verify your `AZURE_SEARCH_API_KEY` is correct
- Check that the API key has read permissions on the index

**Issue**: "Index not found"
- Confirm `AZURE_SEARCH_INDEX_NAME` matches an existing index
- Verify the index is in the same search service

**Issue**: Field not found errors
- Update the field names in `Select` to match your index schema
- Check the field names in the `result.Document.TryGetValue()` calls

### Learn More

- [Azure AI Search Documentation](https://learn.microsoft.com/en-us/azure/search/)
- [Agent Framework RAG Samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/GettingStarted/AgentWithRAG)
- [TextSearchProvider API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.textsearchprovider)

## License

This project is provided as an example implementation of the Microsoft Agent Framework.
