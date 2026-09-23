# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props NuGet.config global.json GenAIOps.slnx ./
COPY src/ src/
RUN dotnet restore src/Api/GenAIOps.Api.csproj --locked-mode
RUN dotnet publish src/Api/GenAIOps.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/publish ./
COPY Prompts/ ./Prompts/
COPY EvaluationData/ ./EvaluationData/
COPY Schemas/ ./Schemas/
COPY GenAIOps.slnx ./
USER $APP_UID
ENTRYPOINT ["dotnet", "GenAIOps.Api.dll"]
