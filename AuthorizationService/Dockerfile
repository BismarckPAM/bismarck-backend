FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy csproj files preserving relative paths for project references
COPY authorization/AuthorizationService.csproj authorization/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "authorization/AuthorizationService.csproj"

# 2. Copy source codes
COPY authorization/ authorization/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/authorization
RUN dotnet publish "AuthorizationService.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuthorizationService.dll"]