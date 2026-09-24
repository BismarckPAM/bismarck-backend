FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy csproj files from repository root
COPY Notification/Notification.Service.csproj Notification/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "Notification/Notification.Service.csproj"

FROM restore AS build
# 2. Copy source codes
COPY Notification/ Notification/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/Notification
RUN dotnet build "Notification.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Notification.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notification.Service.dll"]