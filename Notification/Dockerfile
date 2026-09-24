FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy project files preserving relative paths
COPY notification/Notification.Service.csproj notification/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "notification/Notification.Service.csproj"

FROM restore AS build
# 2. Copy source codes preserving relative paths
COPY notification/ notification/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/notification
RUN dotnet build "Notification.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Notification.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notification.Service.dll"]