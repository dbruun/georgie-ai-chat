using ChatAgent.Web.Components;
using ChatAgent.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from appsettings.json and set as environment variables
var config = builder.Configuration;
foreach (var setting in new[] { "OPENAI_API_KEY", "AZURE_SEARCH_ENDPOINT", "AZURE_SEARCH_KEY", 
                                 "AZURE_SEARCH_INDEX", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_KEY", 
                                 "AZURE_OPENAI_DEPLOYMENT" })
{
    var value = config[setting];
    if (!string.IsNullOrEmpty(value))
    {
        Environment.SetEnvironmentVariable(setting, value);
    }
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
