FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AuthorizationService.csproj", "./"]
RUN dotnet restore "AuthorizationService.csproj"
COPY . .
RUN dotnet build "AuthorizationService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AuthorizationService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AuthorizationService.dll"]
