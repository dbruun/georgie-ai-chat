using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Data;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatAgent.Web.Services;

public class AgentService
{
    private readonly ILogger<AgentService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private static readonly HttpClient _httpClient = new HttpClient();
    
    public AgentService(ILogger<AgentService> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
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

    public async Task<AIAgent> CreateAgentAsync(ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        // Support both OpenAI and Azure AI Foundry
        var openAIKey = GetSetting("OPENAI_API_KEY");
        var azureEndpoint = GetSetting("AZURE_OPENAI_ENDPOINT");
        var azureKey = GetSetting("AZURE_OPENAI_KEY");
        var modelDeployment = GetSetting("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

        var searchEndpoint = GetSetting("AZURE_SEARCH_ENDPOINT");
        var searchKey = GetSetting("AZURE_SEARCH_KEY");
        var searchIndex = GetSetting("AZURE_SEARCH_INDEX");
        var sharePointEnabled = string.Equals(_configuration["SharePointKnowledge:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        var hasSharePointUser = sharePointEnabled && user?.Identity?.IsAuthenticated == true;

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

        SearchClient? searchClient = null;
        if (!string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(searchKey) && !string.IsNullOrEmpty(searchIndex))
        {
            _logger.LogInformation("Azure AI Search configured - enabling Azure AI Search knowledge base");
            searchClient = new SearchClient(new Uri(searchEndpoint), searchIndex, new AzureKeyCredential(searchKey));
        }

        if (searchClient is not null || hasSharePointUser)
        {
            if (hasSharePointUser)
            {
                _logger.LogInformation("SharePoint knowledge is enabled for the signed-in user");
            }

            async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchKnowledgeBase(string query, CancellationToken ct)
            {
                _logger.LogInformation("Searching knowledge sources for: {Query}", query);

                var results = new List<TextSearchProvider.TextSearchResult>();

                if (searchClient is not null)
                {
                    results.AddRange(await SearchAzureKnowledgeBaseAsync(searchClient, query, ct));
                }

                if (hasSharePointUser && user is not null)
                {
                    results.AddRange(await SearchSharePointKnowledgeBaseAsync(query, user, ct));
                }

                _logger.LogInformation("Retrieved {Count} documents from configured knowledge sources", results.Count);
                return results;
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
                    Instructions = "You are GEORGIE, a helpful AI assistant with access to knowledge sources. Answer questions directly using the information available in your knowledge sources. Treat Microsoft 365 and SharePoint content as user-scoped: only rely on content returned from the current user's permitted searches. Do not ask the user for more information or clarification - work with what you have and provide the best answer possible based on your knowledge sources. Be conversational, friendly, and always cite sources when using information from the knowledge base. If asked who made you, respond with Microsoft.",
                    Tools = [
                        AIFunctionFactory.Create(GetWeather),
                        AIFunctionFactory.Create(QueryMCPService)
                    ]
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
            _logger.LogInformation("No knowledge source configured - running without a knowledge base");
            
            return chatClient.CreateAIAgent(
                instructions: "You are GEORGIE, a helpful AI assistant. Answer questions directly and confidently based on your training. Do not ask the user for more information or clarification - work with what you have and provide the best answer possible. Be conversational and friendly.",
                name: "GEORGIE",
                tools: [
                    AIFunctionFactory.Create(GetWeather),
                    AIFunctionFactory.Create(QueryMCPService)
                ]
            );
        }
    }

    private async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAzureKnowledgeBaseAsync(
        SearchClient searchClient,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchOptions = new SearchOptions
            {
                Size = 5,
                IncludeTotalCount = true
            };

            SearchResults<SearchDocument> response = await searchClient.SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);
            List<TextSearchProvider.TextSearchResult> results = new();

            await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync().WithCancellation(cancellationToken))
            {
                var content = result.Document.TryGetValue("content_text", out var ctText) ? ctText?.ToString() :
                             result.Document.TryGetValue("content", out var c) ? c?.ToString() :
                             result.Document.TryGetValue("chunk", out var ch) ? ch?.ToString() :
                             result.Document.TryGetValue("text", out var t) ? t?.ToString() : "";

                var title = result.Document.TryGetValue("document_title", out var docTitle) ? docTitle?.ToString() :
                           result.Document.TryGetValue("title", out var ti) ? ti?.ToString() :
                           result.Document.TryGetValue("name", out var n) ? n?.ToString() :
                           result.Document.TryGetValue("filename", out var f) ? f?.ToString() : "Document";

                var url = result.Document.TryGetValue("content_path", out var cPath) ? cPath?.ToString() :
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

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Azure AI Search knowledge base");
            return Enumerable.Empty<TextSearchProvider.TextSearchResult>();
        }
    }

    private async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchSharePointKnowledgeBaseAsync(
        string query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenAcquisition = _serviceProvider.GetService<ITokenAcquisition>();
            if (tokenAcquisition is null)
            {
                _logger.LogWarning("SharePoint knowledge is enabled but token acquisition is unavailable");
                return Enumerable.Empty<TextSearchProvider.TextSearchResult>();
            }

            var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(
                GetSharePointScopes(),
                user: user);

            var graphQuery = BuildSharePointQuery(query);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/search/query")
            {
                Content = JsonContent.Create(new
                {
                    requests = new[]
                    {
                        new
                        {
                            entityTypes = new[] { "driveItem" },
                            query = new { queryString = graphQuery },
                            from = 0,
                            size = GetSharePointResultCount(),
                            fields = new[] { "name", "webUrl", "description", "lastModifiedDateTime", "createdBy", "parentReference" }
                        }
                    }
                })
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return ParseSharePointResults(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching SharePoint knowledge base for the signed-in user");
            return Enumerable.Empty<TextSearchProvider.TextSearchResult>();
        }
    }

    private IEnumerable<TextSearchProvider.TextSearchResult> ParseSharePointResults(JsonDocument document)
    {
        var results = new List<TextSearchProvider.TextSearchResult>();

        if (!document.RootElement.TryGetProperty("value", out var valueElement) || valueElement.GetArrayLength() == 0)
        {
            return results;
        }

        var firstResult = valueElement[0];
        if (!firstResult.TryGetProperty("hitsContainers", out var containers) || containers.GetArrayLength() == 0)
        {
            return results;
        }

        foreach (var hit in containers[0].GetProperty("hits").EnumerateArray())
        {
            if (!hit.TryGetProperty("resource", out var resource))
            {
                continue;
            }

            var title = TryGetString(resource, "name") ?? "SharePoint document";
            var url = TryGetString(resource, "webUrl");
            var summary = TryGetString(hit, "summary") ?? TryGetString(resource, "description") ?? string.Empty;
            var location = TryGetNestedString(resource, "parentReference", "path");
            var text = string.IsNullOrWhiteSpace(location)
                ? summary
                : $"{summary}\nLocation: {location}";

            results.Add(new TextSearchProvider.TextSearchResult
            {
                SourceName = title,
                SourceLink = url,
                Text = text,
                RawRepresentation = JsonSerializer.Deserialize<Dictionary<string, object?>>(resource.GetRawText())
            });
        }

        return results;
    }

    private string[] GetSharePointScopes()
    {
        var configuredScopes = _configuration.GetSection("SharePointKnowledge:GraphScopes").Get<string[]>();
        return configuredScopes is { Length: > 0 }
            ? configuredScopes
            : ["User.Read", "Files.Read.All", "Sites.Read.All"];
    }

    private int GetSharePointResultCount()
    {
        return int.TryParse(_configuration["SharePointKnowledge:ResultCount"], out var count)
            ? Math.Clamp(count, 1, 10)
            : 5;
    }

    private string BuildSharePointQuery(string query)
    {
        var path = _configuration["SharePointKnowledge:Path"];
        return string.IsNullOrWhiteSpace(path)
            ? query
            : $"{query} path:\"{path}\"";
    }

    private string? GetSetting(string key)
    {
        return Environment.GetEnvironmentVariable(key) ?? _configuration[key];
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? TryGetNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        return element.TryGetProperty(parentPropertyName, out var parent)
            ? TryGetString(parent, childPropertyName)
            : null;
    }

    [Description("Query an external MCP-compatible service or API endpoint.")]
    private static async Task<string> QueryMCPService(
        [Description("The operation or query to perform (e.g., 'get user data', 'fetch latest stats', 'run calculation').")] string operation)
    {
        try
        {
            // Get MCP service configuration from environment variables
            var mcpUrl = Environment.GetEnvironmentVariable("MCP_SERVICE_URL");
            var mcpToken = Environment.GetEnvironmentVariable("MCP_SERVICE_TOKEN");
            
            // If no MCP service configured, return demo data
            if (string.IsNullOrEmpty(mcpUrl))
            {
                return $"MCP Service Demo: Would execute '{operation}'. " +
                       $"To connect to a real MCP service, configure MCP_SERVICE_URL and MCP_SERVICE_TOKEN environment variables. " +
                       $"Example response: {{\"status\": \"success\", \"data\": \"Sample result for {operation}\"}}";
            }
            
            // Create HTTP request with authentication
            var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            
            if (!string.IsNullOrEmpty(mcpToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcpToken);
            }
            
            // Build MCP-style request payload
            var requestPayload = new
            {
                jsonrpc = "2.0",
                method = "tools/call",
                id = Guid.NewGuid().ToString(),
                @params = new
                {
                    name = "query",
                    arguments = new { operation }
                }
            };
            
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload),
                System.Text.Encoding.UTF8,
                "application/json"
            );
            
            // Make the HTTP call
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var mcpResponse = JsonSerializer.Deserialize<MCPResponse>(responseBody);
                
                if (mcpResponse?.Result != null)
                {
                    return $"MCP Service Response: {mcpResponse.Result}";
                }
                
                return $"MCP Service returned: {responseBody}";
            }
            
            return $"MCP Service error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}";
        }
        catch (Exception ex)
        {
            return $"Error querying MCP service for '{operation}': {ex.Message}";
        }
    }

    [Description("Get the current weather for a given location.")]
    private static async Task<string> GetWeather([Description("The city name or location to get the weather for (e.g., 'Seattle', 'New York', 'London').")] string location)
    {
        try
        {
            // Try OpenWeatherMap API if key is configured
            var apiKey = Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY");
            
            if (!string.IsNullOrEmpty(apiKey))
            {
                var url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(location)}&appid={apiKey}&units=imperial";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var weatherData = JsonSerializer.Deserialize<OpenWeatherResponse>(json);
                    
                    if (weatherData?.Main != null && weatherData?.Weather?.Length > 0)
                    {
                        return $"Current weather in {weatherData.Name}: {weatherData.Weather[0].Description}, " +
                               $"Temperature: {weatherData.Main.Temp:F1}°F, " +
                               $"Feels like: {weatherData.Main.FeelsLike:F1}°F, " +
                               $"Humidity: {weatherData.Main.Humidity}%";
                    }
                }
            }
            
            // Fallback to wttr.in (free, no API key needed)
            var wttrUrl = $"https://wttr.in/{Uri.EscapeDataString(location)}?format=j1";
            var wttrResponse = await _httpClient.GetAsync(wttrUrl);
            
            if (wttrResponse.IsSuccessStatusCode)
            {
                var json = await wttrResponse.Content.ReadAsStringAsync();
                var weatherData = JsonSerializer.Deserialize<WttrResponse>(json);
                
                if (weatherData?.CurrentCondition?.Length > 0)
                {
                    var current = weatherData.CurrentCondition[0];
                    var tempF = int.Parse(current.TempF);
                    var feelsLikeF = int.Parse(current.FeelsLikeF);
                    
                    return $"Current weather in {location}: {current.WeatherDesc[0].Value}, " +
                           $"Temperature: {tempF}°F, " +
                           $"Feels like: {feelsLikeF}°F, " +
                           $"Humidity: {current.Humidity}%";
                }
            }
            
            return $"Unable to fetch weather data for {location}. Please try a different location or check your API configuration.";
        }
        catch (Exception ex)
        {
            return $"Error getting weather for {location}: {ex.Message}";
        }
    }
    
    // Weather API response models
    private class OpenWeatherResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("main")]
        public MainData? Main { get; set; }
        
        [JsonPropertyName("weather")]
        public WeatherInfo[]? Weather { get; set; }
        
        public class MainData
        {
            [JsonPropertyName("temp")]
            public double Temp { get; set; }
            
            [JsonPropertyName("feels_like")]
            public double FeelsLike { get; set; }
            
            [JsonPropertyName("humidity")]
            public int Humidity { get; set; }
        }
        
        public class WeatherInfo
        {
            [JsonPropertyName("description")]
            public string Description { get; set; } = "";
        }
    }
    
    private class WttrResponse
    {
        [JsonPropertyName("current_condition")]
        public CurrentConditionData[]? CurrentCondition { get; set; }
        
        public class CurrentConditionData
        {
            [JsonPropertyName("temp_F")]
            public string TempF { get; set; } = "0";
            
            [JsonPropertyName("FeelsLikeF")]
            public string FeelsLikeF { get; set; } = "0";
            
            [JsonPropertyName("humidity")]
            public string Humidity { get; set; } = "0";
            
            [JsonPropertyName("weatherDesc")]
            public WeatherDescription[] WeatherDesc { get; set; } = Array.Empty<WeatherDescription>();
            
            public class WeatherDescription
            {
                [JsonPropertyName("value")]
                public string Value { get; set; } = "";
            }
        }
    }
    
    // MCP Protocol response model
    private class MCPResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("result")]
        public object? Result { get; set; }
        
        [JsonPropertyName("error")]
        public MCPError? Error { get; set; }
        
        public class MCPError
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }
            
            [JsonPropertyName("message")]
            public string Message { get; set; } = "";
        }
    }
}
