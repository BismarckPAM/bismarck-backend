FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY gateway/Gateway.Service.csproj gateway/
RUN dotnet restore "gateway/Gateway.Service.csproj"

FROM restore AS build
COPY gateway/ gateway/
WORKDIR /src/gateway
RUN dotnet build "Gateway.Service.csproj" --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Gateway.Service.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Service.dll"]