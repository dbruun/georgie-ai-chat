using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Data;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ComponentModel;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace ChatAgent;

/// <summary>
/// Example showing how to integrate Azure AI Search as a knowledge source using TextSearchProvider
/// </summary>
public static class ProgramWithSearch
{
    // Optional weather tool to demonstrate agent capabilities
    [Description("Get the weather for a given location.")]
    public static string GetWeather([Description("The location to get the weather for.")] string location)
    {
        Random rand = new();
        string[] conditions = { "sunny", "cloudy", "rainy", "stormy" };
        return $"The weather in {location} is {conditions[rand.Next(0, 4)]} with a high of {rand.Next(10, 30)}°C.";
    }

    public static async Task Main(string[] args)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║  Chat Agent with Azure AI Search Knowledge   ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        // Load configuration
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT");
        var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
        var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME");

        if (string.IsNullOrEmpty(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: OPENAI_API_KEY environment variable not set.");
            Console.ResetColor();
            return;
        }

        if (string.IsNullOrEmpty(searchEndpoint) || string.IsNullOrEmpty(searchApiKey) || string.IsNullOrEmpty(searchIndexName))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Azure Search credentials not found. Running without knowledge base.");
            Console.WriteLine("Set these environment variables to enable Azure AI Search:");
            Console.WriteLine("  AZURE_SEARCH_ENDPOINT");
            Console.WriteLine("  AZURE_SEARCH_API_KEY");
            Console.WriteLine("  AZURE_SEARCH_INDEX_NAME");
            Console.ResetColor();
            Console.WriteLine();
        }

        const string AgentName = "Georgie";
        const string AgentInstructions = "You are a helpful assistant. When answering questions, use information from the knowledge base and cite sources when available.";

        AIAgent agent;

        // Check if Azure Search is configured
        if (!string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(searchApiKey) && !string.IsNullOrEmpty(searchIndexName))
        {
            // Create Azure Search client
            var searchClient = new SearchClient(
                new Uri(searchEndpoint),
                searchIndexName,
                new AzureKeyCredential(searchApiKey)
            );

            // Create search adapter function that queries Azure AI Search
            async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchKnowledgeBase(string query, CancellationToken ct)
            {
                try
                {
                    var searchOptions = new SearchOptions
                    {
                        Size = 5, // Top 5 results
                        Select = { "content", "title", "url" }, // Adjust field names to match your index
                        IncludeTotalCount = true
                    };

                    SearchResults<SearchDocument> response = await searchClient.SearchAsync<SearchDocument>(query, searchOptions, ct);
                    
                    List<TextSearchProvider.TextSearchResult> results = new();
                    
                    await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync().WithCancellation(ct))
                    {
                        results.Add(new TextSearchProvider.TextSearchResult
                        {
                            // Adjust these field names to match your Azure AI Search index schema
                            SourceName = result.Document.TryGetValue("title", out var title) ? title?.ToString() : "Document",
                            SourceLink = result.Document.TryGetValue("url", out var url) ? url?.ToString() : null,
                            Text = result.Document.TryGetValue("content", out var content) ? content?.ToString() ?? string.Empty : string.Empty,
                            RawRepresentation = result.Document
                        });
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Retrieved {results.Count} documents from knowledge base]");
                    Console.ResetColor();

                    return results;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Search error: {ex.Message}");
                    Console.ResetColor();
                    return Enumerable.Empty<TextSearchProvider.TextSearchResult>();
                }
            }

            // Configure TextSearchProvider options
            var textSearchOptions = new TextSearchProviderOptions
            {
                // Run search before every AI invocation to provide context
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
                
                // Include recent messages in search to maintain conversation context
                RecentMessageMemoryLimit = 5,
                
                // Custom prompt to prepend to search results
                ContextPrompt = "## Knowledge Base Information\nUse the following information from the knowledge base to answer the user's question:",
                
                // Instruct the model to cite sources
                CitationsPrompt = "When using information from the knowledge base, cite the source document name and link if available."
            };

            // Create agent with TextSearchProvider as the context provider
            agent = new OpenAIClient(apiKey)
                .GetChatClient("gpt-4o-mini")
                .CreateAIAgent(new ChatClientAgentOptions
                {
                    Name = AgentName,
                    ChatOptions = new ChatOptions 
                    { 
                        Instructions = AgentInstructions,
                        Tools = [AIFunctionFactory.Create(GetWeather)]
                    },
                    AIContextProviderFactory = ctx => new TextSearchProvider(
                        SearchKnowledgeBase,
                        ctx.SerializedState,
                        ctx.JsonSerializerOptions,
                        textSearchOptions
                    )
                });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Azure AI Search knowledge base connected!");
            Console.ResetColor();
        }
        else
        {
            // Create agent without search functionality
            agent = new OpenAIClient(apiKey)
                .GetChatClient("gpt-4o-mini")
                .CreateAIAgent(
                    instructions: AgentInstructions,
                    name: AgentName,
                    tools: [AIFunctionFactory.Create(GetWeather)]
                );
        }

        // Create a new thread for maintaining conversation context
        AgentThread thread = agent.GetNewThread();

        Console.WriteLine($"\nChat with {AgentName}! (Type 'exit' or 'quit' to end the conversation)");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine();

        // Main chat loop
        while (true)
        {
            // Get user input
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("You: ");
            Console.ResetColor();
            
            string? userInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            // Check for exit commands
            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{AgentName}: Goodbye! Have a great day!");
                Console.ResetColor();
                break;
            }

            // Get agent response with streaming
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{AgentName}: ");
            Console.ResetColor();

            try
            {
                await foreach (var update in agent.RunStreamingAsync(userInput, thread))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Console.Write(update.Text);
                    }
                }
                Console.WriteLine();
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }
}
