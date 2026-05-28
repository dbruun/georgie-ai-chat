# GEORGIE - AI Chat Assistant

A modern AI chat interface built with Blazor Server and Microsoft Agent Framework, deployable to Azure Container Apps.

## Features

- 🤖 Meet GEORGIE - Your conversational AI assistant
- 💬 Real-time streaming chat interface
- 🔍 Azure AI Search integration for RAG (Retrieval Augmented Generation)
- 📚 SharePoint Online knowledge access using the signed-in user's Microsoft 365 permissions
- ☁️ Works with OpenAI or Azure AI Foundry endpoints
- 🎨 Beautiful, responsive UI with modern gradient design
- 🚀 Optimized for Azure Container Apps deployment
- 🔧 Function calling support (weather example included)
- 📱 Mobile-friendly design

## Prerequisites

- .NET 9.0 SDK
- OpenAI API key OR Azure AI Foundry endpoint
- (Optional) Azure AI Search service for knowledge base

## Local Development

1. **Set environment variables:**
   
   **Option A: Using OpenAI**
   ```powershell
   $env:OPENAI_API_KEY="your-openai-key"
   ```
   
   **Option B: Using Azure AI Foundry**
   ```powershell
   $env:AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com"
   $env:AZURE_OPENAI_KEY="your-azure-key"
   $env:AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"  # Your deployment name
   ```
   
   **Optional - for Azure AI Search (RAG)**
   ```powershell
   $env:AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net"
   $env:AZURE_SEARCH_KEY="your-search-key"
   $env:AZURE_SEARCH_INDEX="your-index-name"
   ```

2. **Run the application:**
   ```powershell
   cd ChatAgent.Web
   dotnet run
   ```

3. **Open browser:**
   Navigate to `https://localhost:5001` or `http://localhost:5000`

## Docker Deployment

Build and run with Docker:

```powershell
# Build the image
docker build -t chatagent-web .

# Run the container
docker run -p 8080:8080 `
  -e OPENAI_API_KEY="your-key" `
  -e AZURE_SEARCH_ENDPOINT="your-endpoint" `
  -e AZURE_SEARCH_KEY="your-key" `
  -e AZURE_SEARCH_INDEX="your-index" `
  chatagent-web
```

## Azure Container Apps Deployment

### Using Azure CLI

1. **Create a resource group:**
   ```bash
   az group create --name rg-chatagent --location eastus
   ```

2. **Create Container Apps environment:**
   ```bash
   az containerapp env create \
     --name chatagent-env \
     --resource-group rg-chatagent \
     --location eastus
   ```

3. **Build and push image to Azure Container Registry:**
   ```bash
   az acr create --name yourregistry --resource-group rg-chatagent --sku Basic
   az acr build --registry yourregistry --image chatagent-web:latest .
   ```

4. **Deploy to Container Apps:**
   ```bash
   az containerapp create \
     --name chatagent-web \
     --resource-group rg-chatagent \
     --environment chatagent-env \
     --image yourregistry.azurecr.io/chatagent-web:latest \
     --target-port 8080 \
     --ingress external \
     --secrets \
       openai-key="your-openai-key" \
       search-key="your-search-key" \
     --env-vars \
       OPENAI_API_KEY=secretref:openai-key \
       AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net" \
       AZURE_SEARCH_KEY=secretref:search-key \
       AZURE_SEARCH_INDEX="your-index-name" \
     --cpu 0.5 --memory 1Gi
   ```

### Using Azure Portal

1. Go to Azure Portal → Container Apps
2. Create new Container App
3. Select your container registry and image
4. Set target port to **8080**
5. Enable **external ingress**
6. Add environment variables:
   - `OPENAI_API_KEY`
   - `AZURE_SEARCH_ENDPOINT`
   - `AZURE_SEARCH_KEY`
   - `AZURE_SEARCH_INDEX`
7. Set CPU: 0.5 cores, Memory: 1 GB
8. Deploy

## Architecture

```
┌─────────────────┐
│   Blazor UI     │  Interactive Server-Side Rendering
│   (Home.razor)  │  Real-time chat interface
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ AgentService    │  Manages AI Agent lifecycle
│                 │  Streams responses
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────┐
│  Microsoft Agent Framework      │
│  ┌─────────────┐ ┌────────────┐│
│  │ OpenAI GPT  │ │Azure Search││
│  │  (LLM)      │ │   (RAG)    ││
│  └─────────────┘ └────────────┘│
└─────────────────────────────────┘
```

## Configuration

For the full SharePoint and Microsoft 365 setup flow, see [docs/sharepoint-knowledge-setup.md](docs/sharepoint-knowledge-setup.md).

### Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `OPENAI_API_KEY` | Yes | Your OpenAI API key |
| `AZURE_SEARCH_ENDPOINT` | No | Azure AI Search endpoint URL |
| `AZURE_SEARCH_KEY` | No | Azure AI Search admin key |
| `AZURE_SEARCH_INDEX` | No | Name of your search index |

### SharePoint Knowledge Configuration

Add this to `appsettings.json` or user secrets:

```json
{
   "AzureAd": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "your-tenant-id",
      "ClientId": "your-app-client-id",
      "ClientSecret": "your-app-client-secret",
      "CallbackPath": "/signin-oidc"
   },
   "SharePointKnowledge": {
      "Enabled": true,
      "Path": "https://contoso.sharepoint.com/sites/Georgie",
      "ResultCount": 5,
      "GraphScopes": [
         "User.Read",
         "Files.Read.All",
         "Sites.Read.All"
      ]
   }
}
```

GEORGIE queries Microsoft Graph search with the signed-in user's delegated token, so SharePoint results are security-trimmed to content that user can already access.

If the customer uses Okta for workforce sign-in, true SharePoint pass-through still requires Microsoft 365 tokens issued by Microsoft Entra ID. The practical pattern is to federate Entra to Okta so users still authenticate with Okta, while this app signs in against Entra and can obtain delegated Microsoft Graph tokens for SharePoint.

For a more complete explanation of the auth model, delegated Graph permissions, and Azure-side configuration, see [docs/sharepoint-knowledge-setup.md](docs/sharepoint-knowledge-setup.md).

### Azure AI Search Schema

Your search index should have these fields:
- `content` (string) - The main text content
- `title` (string) - Document title
- `url` (string) - Source URL (optional)

## Project Structure

```
ChatAgent.Web/
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor      # Main layout wrapper
│   ├── Pages/
│   │   └── Home.razor            # Chat interface component
│   ├── App.razor                 # Root component
│   ├── Routes.razor              # Routing configuration
│   └── _Imports.razor            # Global using statements
├── Services/
│   └── AgentService.cs           # AI Agent management service
├── wwwroot/
│   └── app.css                   # Styles
├── Program.cs                    # Application entry point
├── appsettings.json              # Configuration
└── ChatAgent.Web.csproj          # Project file
```

## Customization

### Change AI Model

Edit `AgentService.cs`:
```csharp
var chatClient = client.GetChatClient("gpt-4"); // Change model here
```

### Add Custom Functions

In `AgentService.cs`, add to the `Tools` collection:
```csharp
Tools = { 
    AIFunctionFactory.Create(GetWeather),
    AIFunctionFactory.Create(YourCustomFunction)
}
```

### Modify UI Theme

Edit `wwwroot/app.css` and update CSS variables:
```css
:root {
    --primary-color: #your-color;
    --user-message-bg: #your-color;
}
```

## Troubleshooting

### Chat not working
- Verify `OPENAI_API_KEY` is set correctly
- Check browser console for JavaScript errors
- Ensure ports 5000/5001 (dev) or 8080 (prod) are available

### Azure AI Search not connected
- Verify all search environment variables are set
- Check search index exists and has data
- Ensure search key has query permissions

### Container startup fails
- Check container logs: `docker logs <container-id>`
- Verify all required environment variables are passed
- Ensure port 8080 is exposed

## License

This project uses preview packages from Microsoft Agent Framework. Check package licenses for details.
