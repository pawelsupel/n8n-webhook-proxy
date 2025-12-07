FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY n8n-webhook-proxy.sln .
COPY src/WebhookProxy/WebhookProxy.csproj src/WebhookProxy/
COPY src/WebhookProxy.Tests/WebhookProxy.Tests.csproj src/WebhookProxy.Tests/

RUN dotnet restore

COPY . .
RUN dotnet publish src/WebhookProxy/WebhookProxy.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "WebhookProxy.dll"]
