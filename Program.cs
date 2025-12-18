using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ComponentModel;

namespace ChatAgent;

public static class Program
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
        Console.WriteLine("║     Chat Agent - Multi-Turn Conversation     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        // Load configuration
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: OPENAI_API_KEY environment variable not set.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Please set your OpenAI API key:");
            Console.WriteLine("  Windows (PowerShell): $env:OPENAI_API_KEY='your-api-key'");
            Console.WriteLine("  Linux/Mac: export OPENAI_API_KEY='your-api-key'");
            Console.WriteLine();
            Console.WriteLine("Alternatively, you can use Azure OpenAI or GitHub Models.");
            Console.WriteLine("See README.md for more details.");
            return;
        }

        // Create the AI agent
        const string AgentName = "Georgie";
        const string AgentInstructions = "You are a helpful and friendly assistant named Georgie. You engage in natural conversations and can help with various tasks.";

        AIAgent agent = new OpenAIClient(apiKey)
            .GetChatClient("gpt-4o-mini")
            .CreateAIAgent(
                instructions: AgentInstructions,
                name: AgentName,
                tools: [AIFunctionFactory.Create(GetWeather)]
            );

        // Create a new thread for maintaining conversation context
        AgentThread thread = agent.GetNewThread();

        Console.WriteLine($"Chat with {AgentName}! (Type 'exit' or 'quit' to end the conversation)");
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
