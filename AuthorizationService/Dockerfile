FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy csproj files matching the "AuthorizationService" folder name
COPY AuthorizationService/AuthorizationService.csproj AuthorizationService/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "AuthorizationService/AuthorizationService.csproj"

# 2. Copy source codes
COPY AuthorizationService/ AuthorizationService/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/AuthorizationService
RUN dotnet publish "AuthorizationService.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuthorizationService.dll"]