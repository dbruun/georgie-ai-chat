using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Data;
using Microsoft.Extensions.AI;
using OpenAI;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

// ============================================================================
// MINIMAL EXAMPLE: Adding Azure AI Search to Your Agent
// ============================================================================

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT")!;
var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY")!;
var indexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME")!;

// 1. Create Azure Search client
var searchClient = new SearchClient(
    new Uri(searchEndpoint),
    indexName,
    new AzureKeyCredential(searchApiKey)
);

// 2. Create search function
async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(string query, CancellationToken ct)
{
    var options = new SearchOptions { Size = 5, Select = { "content", "title", "url" } };
    var response = await searchClient.SearchAsync<SearchDocument>(query, options, ct);
    
    var results = new List<TextSearchProvider.TextSearchResult>();
    await foreach (var result in response.Value.GetResultsAsync())
    {
        results.Add(new()
        {
            SourceName = result.Document["title"]?.ToString() ?? "Document",
            SourceLink = result.Document["url"]?.ToString(),
            Text = result.Document["content"]?.ToString() ?? ""
        });
    }
    return results;
}

// 3. Configure search options
var searchOptions = new TextSearchProviderOptions
{
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    RecentMessageMemoryLimit = 5
};

// 4. Create agent with search
var agent = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4o-mini")
    .CreateAIAgent(new ChatClientAgentOptions
    {
        Name = "SearchAgent",
        ChatOptions = new() { Instructions = "Use the knowledge base to answer questions." },
        AIContextProviderFactory = ctx => new TextSearchProvider(
            SearchAsync,
            ctx.SerializedState,
            ctx.JsonSerializerOptions,
            searchOptions
        )
    });

// 5. Use the agent
var thread = agent.GetNewThread();
Console.WriteLine(await agent.RunAsync("What information do you have?", thread));
