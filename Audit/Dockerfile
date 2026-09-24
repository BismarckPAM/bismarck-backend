# Multi-stage build: restore -> build -> publish -> minimal runtime image.
# The runtime image is aspnet (not the full SDK), matching the other services.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 1. Copy project files preserving folder structure for relative project references
COPY Audit/Audit.Service.csproj Audit/
COPY BuildingBlocks/Messaging/Messaging.csproj BuildingBlocks/Messaging/
RUN dotnet restore "Audit/Audit.Service.csproj"

FROM restore AS build
# 2. Copy source codes
COPY Audit/ Audit/
COPY BuildingBlocks/Messaging/ BuildingBlocks/Messaging/

WORKDIR /src/Audit
RUN dotnet build "Audit.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Audit.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Audit.Service.dll"]