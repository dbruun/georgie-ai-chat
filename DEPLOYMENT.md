# Azure Container Apps Deployment Guide

This guide walks you through deploying the GEORGIE chat application to Azure Container Apps (ACA).

## Prerequisites

- Azure CLI installed (`az --version` to check)
- Docker installed (for local testing)
- Azure subscription
- Azure AI Foundry endpoint and API key
- (Optional) Azure AI Search service for RAG

## Understanding Secrets in Container Apps

Container Apps use **secrets** to securely store sensitive values like API keys. 

### How it works:
1. **Define secrets** when creating the app: `--secrets azure-openai-key="your-key-value"`
2. **Reference secrets** in environment variables: `AZURE_OPENAI_KEY=secretref:azure-openai-key`
3. Secrets are encrypted at rest and only accessible to your app

### Best practices:
- ✅ Use secrets for API keys, connection strings, passwords
- ✅ Use environment variables for non-sensitive config (endpoints, deployment names)
- ✅ Rotate secrets regularly using `az containerapp secret set`
- ❌ Never commit secrets to source control

## Quick Deploy (First Time - 10 minutes)

```bash
# 1. Login to Azure
az login

# 2. Set variables
RESOURCE_GROUP="rg-chatagent"
LOCATION="eastus"
ACR_NAME="chatagentacr$RANDOM"
APP_NAME="chatagent-web"
ENV_NAME="chatagent-env"

# 3. Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# 4. Create Azure Container Registry
az acr create --name $ACR_NAME --resource-group $RESOURCE_GROUP --sku Basic --admin-enabled true

# 5. Build and push image
az acr build --registry $ACR_NAME --image chatagent-web:latest --file Dockerfile .

# 6. Create Container Apps environment
az containerapp env create \
  --name $ENV_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION

# 7. Get ACR credentials
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# 8. Deploy Container App
az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENV_NAME \
  --image $ACR_NAME.azurecr.io/chatagent-web:latest \
  --target-port 8080 \
  --ingress external \
  --registry-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --secrets \
    azure-openai-key="your-azure-openai-api-key" \
    azure-search-key="your-azure-search-admin-key" \
  --env-vars \
    AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/" \
    AZURE_OPENAI_KEY=secretref:azure-openai-key \
    AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini" \
    AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net" \
    AZURE_SEARCH_KEY=secretref:azure-search-key \
    AZURE_SEARCH_INDEX="your-index-name" \
  --cpu 0.5 --memory 1Gi \
  --min-replicas 1 --max-replicas 3

# 9. Get the app URL
az containerapp show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.configuration.ingress.fqdn -o tsv
```

## Full Deployment with Azure AI Foundry + Azure AI Search

Deploy GEORGIE with all features enabled:

### Bash (Linux/Mac):
```bash
az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENV_NAME \
  --image $ACR_NAME.azurecr.io/chatagent-web:latest \
  --target-port 8080 \
  --ingress external \
  --registry-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --secrets \
    azure-openai-key="your-azure-openai-api-key" \
    azure-search-key="your-azure-search-admin-key" \
  --env-vars \
    AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/" \
    AZURE_OPENAI_KEY=secretref:azure-openai-key \
    AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini" \
    AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net" \
    AZURE_SEARCH_KEY=secretref:azure-search-key \
    AZURE_SEARCH_INDEX="your-index-name" \
  --cpu 0.5 --memory 1Gi \
  --min-replicas 1 --max-replicas 3
```

### PowerShell (Windows):
```powershell
az containerapp create `
  --name $APP_NAME `
  --resource-group $RESOURCE_GROUP `
  --environment $ENV_NAME `
  --image "$ACR_NAME.azurecr.io/chatagent-web:latest" `
  --target-port 8080 `
  --ingress external `
  --registry-server "$ACR_NAME.azurecr.io" `
  --registry-username $ACR_USERNAME `
  --registry-password $ACR_PASSWORD `
  --secrets `
    azure-openai-key="your-azure-openai-api-key" `
    azure-search-key="your-azure-search-admin-key" `
  --env-vars `
    AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/" `
    AZURE_OPENAI_KEY=secretref:azure-openai-key `
    AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini" `
    AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net" `
    AZURE_SEARCH_KEY=secretref:azure-search-key `
    AZURE_SEARCH_INDEX="your-index-name" `
  --cpu 0.5 --memory 1Gi `
  --min-replicas 1 --max-replicas 3
```

**Note:** Your Azure AI Search index must have these fields:
- `content_text` - Main document content
- `document_title` - Document title  
- `content_path` - Source path/URL

## Update Deployment

When you make code changes:

```bash
# Rebuild and push
az acr build --registry $ACR_NAME --image chatagent-web:latest --file Dockerfile .

# Update container app (triggers new revision)
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image $ACR_NAME.azurecr.io/chatagent-web:latest
```

## Scaling Configuration

### Manual Scaling
```bash
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --min-replicas 2 \
  --max-replicas 10
```

### Auto-scaling Rules
```bash
# Scale based on HTTP requests
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --scale-rule-name http-rule \
  --scale-rule-type http \
  --scale-rule-http-concurrency 50
```

## Monitoring

### View logs
```bash
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --follow
```

### View metrics
```bash
# Go to Azure Portal → Container Apps → Your App → Metrics
# Monitor: CPU, Memory, HTTP requests, Response time
```

## Environment Variables Management

### Add a secret
```bash
az containerapp secret set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --secrets "new-secret=value"
```

### Update environment variable
```bash
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars "NEW_VAR=value"
```

### List current configuration
```bash
az containerapp show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.template
```

## Custom Domain & HTTPS

```bash
# Add custom domain
az containerapp hostname add \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname chat.yourdomain.com

# Certificate is automatically managed by Azure
```

## Cost Optimization

### Scale to Zero (when idle)
```bash
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --min-replicas 0
```

**Note:** Blazor Server apps maintain WebSocket connections, so scaling to zero may not be ideal for production.

### Use Consumption Plan
- ACA charges based on vCPU and memory per second
- Typical costs: ~$0.000024/vCPU-second, ~$0.0000025/GB-second
- Example: 0.5 vCPU, 1GB, running 24/7 ≈ $15-20/month

## Troubleshooting

### App not starting
```bash
# Check logs
az containerapp logs show --name $APP_NAME --resource-group $RESOURCE_GROUP --tail 100

# Common issues:
# - Missing OPENAI_API_KEY
# - Port mismatch (ensure 8080)
# - Image pull errors (check ACR credentials)
```

### High latency
```bash
# Check replica count
az containerapp replica list --name $APP_NAME --resource-group $RESOURCE_GROUP

# Increase resources
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --cpu 1.0 --memory 2Gi
```

### Connection issues
```bash
# Verify ingress is external
az containerapp show --name $APP_NAME --resource-group $RESOURCE_GROUP \
  --query properties.configuration.ingress.external
```

## CI/CD with GitHub Actions

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to ACA

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    
    - name: Login to Azure
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}
    
    - name: Build and push
      run: |
        az acr build --registry ${{ secrets.ACR_NAME }} \
          --image chatagent-web:${{ github.sha }} \
          --file Dockerfile .
    
    - name: Deploy to ACA
      run: |
        az containerapp update \
          --name ${{ secrets.APP_NAME }} \
          --resource-group ${{ secrets.RESOURCE_GROUP }} \
          --image ${{ secrets.ACR_NAME }}.azurecr.io/chatagent-web:${{ github.sha }}
```

## Cleanup

```bash
# Delete everything
az group delete --name $RESOURCE_GROUP --yes --no-wait
```

## Additional Resources

- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Pricing Calculator](https://azure.microsoft.com/pricing/calculator/)
- [ACA Samples](https://github.com/Azure-Samples/container-apps-samples)
