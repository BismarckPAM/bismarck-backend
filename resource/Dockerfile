FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY *.csproj ./
# Enable enhanced HTTP retries and avoid parallel download congestion
ENV NUGET_ENABLE_ENHANCED_HTTP_RETRY=true
RUN dotnet restore "Resource.Service.csproj" --disable-parallel 

FROM restore AS build
COPY . .
RUN dotnet build --no-restore --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resource.Service.dll"]