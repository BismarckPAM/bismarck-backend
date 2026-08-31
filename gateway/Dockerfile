# Multi-stage build: restore -> build -> publish -> minimal runtime image.
# The runtime image is aspnet (not the full SDK), matching the other services.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Gateway.Service.csproj .
RUN dotnet restore "Gateway.Service.csproj"

FROM restore AS build
COPY . .
RUN dotnet build "Gateway.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Gateway.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Service.dll"]
