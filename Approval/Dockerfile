# Multi-stage build: restore -> build -> publish -> minimal runtime image.
# The runtime image is aspnet (not the full SDK), matching the other services.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy csproj files preserving relative paths for project references
COPY Approval/Approval.Service.csproj Approval/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "Approval/Approval.Service.csproj"

FROM restore AS build
# 2. Copy source codes
COPY Approval/ Approval/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/Approval
RUN dotnet build "Approval.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Approval.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Approval.Service.dll"]