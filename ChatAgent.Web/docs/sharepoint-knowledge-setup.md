# SharePoint Knowledge Setup

This document explains how GEORGIE currently uses SharePoint Online as a knowledge source, what must be configured in Azure and Microsoft 365, and how this works when the customer uses Okta for workforce sign-in.

## How It Works

GEORGIE supports two knowledge paths:

- Azure AI Search, using the app's configured search key.
- SharePoint Online, using Microsoft Graph with the signed-in user's delegated permissions.

The SharePoint flow is security-trimmed. GEORGIE only receives SharePoint results that the current signed-in user is already allowed to access.

The implementation is wired here:

- `Program.cs`: enables OIDC sign-in and downstream token acquisition through `Microsoft.Identity.Web`.
- `Components/Layout/MainLayout.razor`: shows the sign-in/sign-out entry points.
- `Components/Pages/Home.razor`: creates the agent with the current authenticated user.
- `Services/AgentService.cs`: acquires a delegated Microsoft Graph token and calls `POST https://graph.microsoft.com/v1.0/search/query`.

## Important Auth Distinction

`DefaultAzureCredential` is not used for SharePoint pass-through.

That is intentional.

`DefaultAzureCredential` authenticates the app process to Azure as the app or as a local developer identity. It does not represent the browser user who signed in to the web application.

For SharePoint permission trimming, GEORGIE must call Microsoft Graph as the signed-in user. That requires delegated Microsoft identity tokens, which are acquired through the OpenID Connect sign-in flow configured in this app.

## Current User Experience

If Microsoft identity is not configured:

- The app still runs.
- GEORGIE can still use OpenAI or Azure OpenAI.
- SharePoint knowledge is not available.

If Microsoft identity is configured:

- The app shows a sign-in link.
- Users are not automatically redirected to sign in on page load.
- After sign-in, GEORGIE can query SharePoint using the user's Microsoft 365 access.

## Azure and Microsoft 365 Configuration

### 1. Create an App Registration in Microsoft Entra ID

Create a web app registration for GEORGIE.

Configure these values:

- Application type: Web
- Supported account type: match your tenant requirements
- Redirect URI for local development: `https://localhost:5001/signin-oidc`
- Redirect URI for deployed environments: `https://<your-app-host>/signin-oidc`

Create either:

- a client secret, or
- a certificate

This app currently expects a client secret in configuration.

### 2. Add Microsoft Graph Delegated Permissions

Add these delegated permissions to the app registration:

- `User.Read`
- `Files.Read.All`
- `Sites.Read.All`

Grant admin consent if required by tenant policy.

These scopes are the ones currently requested by the application.

### 3. Confirm SharePoint Access

Users must already have access to the SharePoint sites, libraries, and files they are expected to query.

GEORGIE does not bypass SharePoint permissions. It only surfaces what Microsoft Graph returns for that user.

### 4. Configure the Application

Populate the following settings in `appsettings.json`, user secrets, or secure environment configuration:

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

Field notes:

- `AzureAd:TenantId`: the Microsoft Entra tenant that issues tokens for Microsoft 365 access.
- `AzureAd:ClientId`: the app registration client ID.
- `AzureAd:ClientSecret`: the app registration secret. Do not keep real secrets in source-controlled files.
- `SharePointKnowledge:Enabled`: turns on the SharePoint knowledge path.
- `SharePointKnowledge:Path`: optional path scoping for SharePoint search.
- `SharePointKnowledge:ResultCount`: number of Graph search hits to pull into GEORGIE's context.
- `SharePointKnowledge:GraphScopes`: delegated scopes requested for the downstream Graph call.

## Okta Customers

If the customer uses Okta for workforce sign-in, the key point is this:

Okta can be the interactive sign-in experience, but SharePoint Online still requires Microsoft-issued delegated tokens for Microsoft Graph.

That means the practical enterprise pattern is:

- Microsoft 365 and Microsoft Entra remain the identity system for Graph and SharePoint access.
- Entra is federated with Okta, or Okta is otherwise integrated as the upstream identity provider.
- Users sign in through Okta.
- Microsoft Entra issues the tokens used by this app to call Microsoft Graph.

What does not work for this scenario:

- taking a pure Okta access token and passing it directly to SharePoint Online or Microsoft Graph

If the customer wants SharePoint permission trimming, Microsoft identity still has to be in the token issuance path.

## Local Development Recommendations

- Use user secrets instead of storing `AzureAd:ClientSecret` in `appsettings.json`.
- Keep a local development redirect URI registered in Entra.
- Test sign-in in a fresh private browser window if you change auth settings.

## Deployment Recommendations

- Store client secrets in Azure Key Vault or the hosting platform's secure secret store.
- Register the production redirect URI before testing sign-in.
- Ensure HTTPS is enabled for the deployed app.
- If deploying behind a reverse proxy, confirm forwarded headers and external host configuration are correct so redirect URIs match what users actually hit.

## Troubleshooting

### The app never shows a sign-in option

Check that both of these are configured:

- `AzureAd:ClientId`
- `AzureAd:TenantId`

Without those values, auth is not enabled in the current app startup.

### Sign-in succeeds but SharePoint results are empty

Check these in order:

- `SharePointKnowledge:Enabled` is `true`
- the user actually has access to the target SharePoint content
- admin consent has been granted for delegated Graph permissions if your tenant requires it
- `SharePointKnowledge:Path` is not too restrictive

### Users can chat but are never prompted automatically

That is the current behavior by design.

The app currently offers sign-in but does not force sign-in on page load. If you want sign-in to be required whenever SharePoint knowledge is enabled, that can be added as a follow-up change.

### Why not use `DefaultAzureCredential`?

Because the requirement is user-scoped SharePoint access.

`DefaultAzureCredential` would authenticate as the app or local developer identity, not as the current browser user.

## Operational Summary

Use this mental model:

- Azure OpenAI and Azure AI Search can be authenticated as the app.
- SharePoint knowledge must be authenticated as the user.
- Okta can front the sign-in experience, but Microsoft Entra must still issue the delegated Microsoft Graph token.