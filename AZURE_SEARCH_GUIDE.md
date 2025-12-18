# Azure AI Search Integration - Quick Start Guide

## Overview

This guide shows you how to add Azure AI Search as a knowledge source to your chat agent, enabling Retrieval Augmented Generation (RAG).

## What You'll Need

- Azure AI Search service
- A populated search index
- Search service API key

## Quick Setup (5 minutes)

### 1. Install the Package

```powershell
dotnet add package Azure.Search.Documents
```

### 2. Set Environment Variables

```powershell
$env:AZURE_SEARCH_ENDPOINT='https://your-service.search.windows.net'
$env:AZURE_SEARCH_API_KEY='your-api-key'
$env:AZURE_SEARCH_INDEX_NAME='your-index'
```

### 3. Update Your Code

See `ProgramWithSearch.cs` for a complete working example, or follow the steps below.

## Implementation Steps

### Step 1: Add Using Statements

```csharp
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
```

### Step 2: Create Search Client

```csharp
var searchClient = new SearchClient(
    new Uri(searchEndpoint),
    searchIndexName,
    new AzureKeyCredential(searchApiKey)
);
```

### Step 3: Create Search Adapter Function

```csharp
async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchKnowledgeBase(
    string query, 
    CancellationToken ct)
{
    var searchOptions = new SearchOptions
    {
        Size = 5,
        Select = { "content", "title", "url" } // Adjust to your schema
    };

    SearchResults<SearchDocument> response = 
        await searchClient.SearchAsync<SearchDocument>(query, searchOptions, ct);
    
    List<TextSearchProvider.TextSearchResult> results = new();
    
    await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
    {
        results.Add(new TextSearchProvider.TextSearchResult
        {
            SourceName = result.Document["title"]?.ToString() ?? "Document",
            SourceLink = result.Document["url"]?.ToString(),
            Text = result.Document["content"]?.ToString() ?? string.Empty
        });
    }
    
    return results;
}
```

### Step 4: Configure TextSearchProvider

```csharp
var textSearchOptions = new TextSearchProviderOptions
{
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    RecentMessageMemoryLimit = 5,
    ContextPrompt = "Use the following information from the knowledge base:",
    CitationsPrompt = "Cite sources when available."
};
```

### Step 5: Create Agent with Search

```csharp
AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4o-mini")
    .CreateAIAgent(new ChatClientAgentOptions
    {
        Name = "Georgie",
        ChatOptions = new ChatOptions 
        { 
            Instructions = "You are a helpful assistant. Use the knowledge base to answer questions."
        },
        AIContextProviderFactory = ctx => new TextSearchProvider(
            SearchKnowledgeBase,
            ctx.SerializedState,
            ctx.JsonSerializerOptions,
            textSearchOptions
        )
    });
```

## Index Schema

Your Azure AI Search index needs these fields (adjust names as needed):

| Field | Type | Purpose |
|-------|------|---------|
| content | Edm.String | Main searchable text |
| title | Edm.String | Document name/title |
| url | Edm.String | (Optional) Source link |

## Testing Your Setup

1. Run the application
2. Ask a question related to your indexed documents
3. The agent should retrieve relevant information and cite sources

Example:
```
You: What is the product warranty?
Georgie: According to the Product Warranty document, all products come with...
```

## Common Field Names

Different indexes use different field names. Update the `Select` clause to match yours:

```csharp
// Common variations:
Select = { "content", "title", "url" }           // Default
Select = { "text", "name", "link" }              // Alternative
Select = { "body", "heading", "source_url" }     // Another variant
Select = { "chunk", "document_title", "path" }   // Chunked documents
```

## TextSearchProvider Options

```csharp
new TextSearchProviderOptions
{
    // When to search
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    
    // How many recent messages to include in search
    RecentMessageMemoryLimit = 5,
    
    // Custom prompt prepended to search results
    ContextPrompt = "## Knowledge Base\n...",
    
    // Instructions for citations
    CitationsPrompt = "Cite your sources.",
    
    // Custom function tool name (for OnDemand mode)
    FunctionToolName = "search_knowledge_base",
    FunctionToolDescription = "Search the knowledge base"
}
```

## Search Behaviors

### BeforeAIInvoke (Recommended)
Automatically searches before each AI call.

```csharp
SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
```

**Pros:**
- Automatic, no extra configuration
- Always has context
- Consistent behavior

**Cons:**
- Searches even when not needed
- Uses more tokens

### OnDemandFunctionCalling
Agent decides when to search using function calling.

```csharp
SearchTime = TextSearchProviderOptions.TextSearchBehavior.OnDemandFunctionCalling
```

**Pros:**
- More efficient
- Agent controls when to search
- Lower token usage

**Cons:**
- Requires function calling support
- Agent must know when to search

## Advanced: Semantic Search

For better results, use semantic search:

```csharp
var searchOptions = new SearchOptions
{
    QueryType = SearchQueryType.Semantic,
    SemanticSearch = new SemanticSearchOptions
    {
        SemanticConfigurationName = "default" // Your semantic config name
    },
    Size = 5
};
```

## Advanced: Hybrid Search (Vector + Keyword)

Combine vector and keyword search:

```csharp
var searchOptions = new SearchOptions
{
    VectorSearch = new VectorSearchOptions
    {
        Queries =
        {
            new VectorizedQuery(embeddingVector)
            {
                KNearestNeighborsCount = 5,
                Fields = { "contentVector" }
            }
        }
    },
    Size = 5
};
```

## Advanced: Filtering

Restrict search results:

```csharp
var searchOptions = new SearchOptions
{
    Filter = "category eq 'documentation'",
    Size = 5
};
```

## Troubleshooting

### No Results Returned
- Check index has data: Use Azure Portal to verify
- Verify field names match your index schema
- Try simpler search queries

### Search Error: 403
- Verify API key is correct
- Check key has read permissions
- Try regenerating the key

### Wrong Information Returned
- Increase `Size` to get more results
- Use semantic search for better relevance
- Improve your index data quality

### Agent Not Citing Sources
- Ensure `CitationsPrompt` is set
- Verify `SourceName` and `SourceLink` are populated
- Update agent instructions to emphasize citations

## Performance Tips

1. **Optimize Search Results Size**: Start with 3-5 results, adjust based on needs
2. **Use Semantic Search**: Better relevance with semantic configurations
3. **Enable Caching**: Cache search results when appropriate
4. **Efficient Field Selection**: Only select fields you need

## Next Steps

- Explore vector search for semantic similarity
- Add filters for multi-tenant scenarios
- Implement custom context formatting
- Set up semantic configurations in your index

## Resources

- [Full Example Code](./ProgramWithSearch.cs)
- [Azure AI Search Docs](https://learn.microsoft.com/azure/search/)
- [Agent Framework RAG Samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/GettingStarted/AgentWithRAG)
