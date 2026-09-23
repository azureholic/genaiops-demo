# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json GenAIOps.slnx ./
COPY src/ src/
RUN dotnet restore src/Workers/MetricsAggregator/GenAIOps.Workers.MetricsAggregator.csproj --locked-mode
RUN dotnet publish src/Workers/MetricsAggregator/GenAIOps.Workers.MetricsAggregator.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish ./
COPY EvaluationData/ ./EvaluationData/
COPY GenAIOps.slnx ./
USER $APP_UID
ENTRYPOINT ["dotnet", "GenAIOps.Workers.MetricsAggregator.dll"]
