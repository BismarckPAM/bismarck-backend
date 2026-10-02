# Multi-stage build: restore -> build -> publish -> minimal runtime image.
# Same pattern as Audit/Approval. The runtime image is aspnet (not the full SDK).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy project files preserving folder structure for the relative
#    BuildingBlocks/Messaging project reference.
COPY Analytics/Analytics.Service.csproj Analytics/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "Analytics/Analytics.Service.csproj"

FROM restore AS build
# 2. Copy source codes
COPY Analytics/ Analytics/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/Analytics
RUN dotnet build "Analytics.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Analytics.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Analytics.Service.dll"]
