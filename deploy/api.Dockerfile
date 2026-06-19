# Multi-stage build cho Pos.Api (.NET 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/Pos.Api/Pos.Api.csproj
RUN dotnet publish src/Pos.Api/Pos.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pos.Api.dll"]
