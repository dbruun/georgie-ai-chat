# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ChatAgent.Web/ChatAgent.Web.csproj ChatAgent.Web/
RUN dotnet restore "ChatAgent.Web/ChatAgent.Web.csproj"

# Copy everything else and build
COPY ChatAgent.Web/ ChatAgent.Web/
WORKDIR "/src/ChatAgent.Web"
RUN dotnet build "ChatAgent.Web.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "ChatAgent.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ChatAgent.Web.dll"]
