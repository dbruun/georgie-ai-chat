using ChatAgent.Web.Components;
using ChatAgent.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);
var hasMicrosoftIdentity = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"]);

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

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddScoped<AgentService>();

if (hasMicrosoftIdentity)
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi([
            "User.Read",
            "Files.Read.All",
            "Sites.Read.All"
        ])
        .AddInMemoryTokenCaches();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
if (hasMicrosoftIdentity)
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.UseAntiforgery();

if (hasMicrosoftIdentity)
{
    app.MapGet("/account/login", async (HttpContext context, string? returnUrl) =>
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
        };

        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
    });

    app.MapGet("/account/logout", async (HttpContext context) =>
    {
        var properties = new AuthenticationProperties { RedirectUri = "/" };

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
    });
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
