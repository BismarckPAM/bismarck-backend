FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy csproj files preserving relative paths for project references
COPY identity/Identity.Service.csproj identity/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "identity/Identity.Service.csproj" -v n

FROM restore AS build
# 2. Copy source codes
COPY identity/ identity/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/identity
RUN dotnet build "Identity.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Identity.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Identity.Service.dll"]