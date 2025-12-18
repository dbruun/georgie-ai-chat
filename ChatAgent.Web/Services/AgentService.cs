using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Data;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using System.ComponentModel;

namespace ChatAgent.Web.Services;

public class AgentService
{
    private readonly ILogger<AgentService> _logger;
    
    public AgentService(ILogger<AgentService> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamChatResponseAsync(
        string message, 
        AgentThread thread, 
        AIAgent agent)
    {
        try
        {
            await foreach (var update in agent.RunStreamingAsync(message, thread))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
            }
        }
        finally
        {
            // Clean up resources if needed
        }
    }

    public AIAgent CreateAgent()
    {
        // Support both OpenAI and Azure AI Foundry
        var openAIKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
        var modelDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

        var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT");
        var searchKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_KEY");
        var searchIndex = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX");

        ChatClient chatClient;
        
        if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
        {
            // Use Azure AI Foundry
            _logger.LogInformation("Using Azure AI Foundry endpoint: {Endpoint}", azureEndpoint);
            var azureClient = new AzureOpenAIClient(new Uri(azureEndpoint), new AzureKeyCredential(azureKey));
            chatClient = azureClient.GetChatClient(modelDeployment);
        }
        else if (!string.IsNullOrEmpty(openAIKey))
        {
            // Use OpenAI
            _logger.LogInformation("Using OpenAI API");
            var client = new OpenAIClient(openAIKey);
            chatClient = client.GetChatClient(modelDeployment);
        }
        else
        {
            throw new InvalidOperationException("Either OPENAI_API_KEY or (AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_KEY) must be set");
        }

        // Check if Azure Search is configured
        if (!string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(searchKey) && !string.IsNullOrEmpty(searchIndex))
        {
            _logger.LogInformation("Azure AI Search configured - enabling knowledge base");
            
            var searchClient = new SearchClient(new Uri(searchEndpoint), searchIndex, new AzureKeyCredential(searchKey));

            async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchKnowledgeBase(string query, CancellationToken ct)
            {
                try
                {
                    _logger.LogInformation("🔍 Searching knowledge base for: {Query}", query);
                    
                    var searchOptions = new SearchOptions
                    {
                        Size = 5,
                        IncludeTotalCount = true
                    };

                    SearchResults<SearchDocument> response = await searchClient.SearchAsync<SearchDocument>(query, searchOptions, ct);
                    List<TextSearchProvider.TextSearchResult> results = new();

                    await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync().WithCancellation(ct))
                    {
                        // Log available fields for debugging
                        var fields = string.Join(", ", result.Document.Keys);
                        _logger.LogInformation("📄 Document fields: {Fields}", fields);
                        
                        // Map to actual field names in pdf-documents index
                        var content = result.Document.TryGetValue("content_text", out var ct_text) ? ct_text?.ToString() :
                                     result.Document.TryGetValue("content", out var c) ? c?.ToString() :
                                     result.Document.TryGetValue("chunk", out var ch) ? ch?.ToString() :
                                     result.Document.TryGetValue("text", out var t) ? t?.ToString() : "";
                        
                        var title = result.Document.TryGetValue("document_title", out var doc_title) ? doc_title?.ToString() :
                                   result.Document.TryGetValue("title", out var ti) ? ti?.ToString() :
                                   result.Document.TryGetValue("name", out var n) ? n?.ToString() :
                                   result.Document.TryGetValue("filename", out var f) ? f?.ToString() : "Document";
                        
                        var url = result.Document.TryGetValue("content_path", out var c_path) ? c_path?.ToString() :
                                 result.Document.TryGetValue("url", out var u) ? u?.ToString() :
                                 result.Document.TryGetValue("metadata_storage_path", out var p) ? p?.ToString() : null;

                        results.Add(new TextSearchProvider.TextSearchResult
                        {
                            SourceName = title,
                            SourceLink = url,
                            Text = content ?? string.Empty,
                            RawRepresentation = result.Document
                        });
                    }

                    _logger.LogInformation("✅ Retrieved {Count} documents from knowledge base (Total: {Total})", results.Count, response.TotalCount);
                    return results;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error searching knowledge base");
                    return Enumerable.Empty<TextSearchProvider.TextSearchResult>();
                }
            }

            var textSearchOptions = new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
                RecentMessageMemoryLimit = 5,
                ContextPrompt = "## Knowledge Base Information\nUse the following information from the knowledge base to answer the user's question:",
                CitationsPrompt = "When using information from the knowledge base, cite the source document name and link if available."
            };

            return chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "GEORGIE",
                ChatOptions = new ChatOptions
                {
                    Instructions = "You are GEORGIE, a helpful AI assistant with access to a knowledge base. Answer questions directly using the information available in your knowledge sources. Do not ask the user for more information or clarification - work with what you have and provide the best answer possible based on your knowledge base. Be conversational, friendly, and always cite sources when using information from the knowledge base. If asked who made you, respond with Microsoft.",
                    Tools = [AIFunctionFactory.Create(GetWeather)]
                },
                AIContextProviderFactory = ctx => new TextSearchProvider(
                    SearchKnowledgeBase,
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions,
                    textSearchOptions
                )
            });
        }
        else
        {
            _logger.LogInformation("Azure AI Search not configured - running without knowledge base");
            
            return chatClient.CreateAIAgent(
                instructions: "You are GEORGIE, a helpful AI assistant. Answer questions directly and confidently based on your training. Do not ask the user for more information or clarification - work with what you have and provide the best answer possible. Be conversational and friendly.",
                name: "GEORGIE",
                tools: [AIFunctionFactory.Create(GetWeather)]
            );
        }
    }

    [Description("Get the weather for a given location.")]
    private static string GetWeather([Description("The location to get the weather for.")] string location)
    {
        return $"The weather in {location} is 72°F and sunny.";
    }
}
