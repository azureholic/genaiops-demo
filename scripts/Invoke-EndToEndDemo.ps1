[CmdletBinding()]
param(
    [switch] $Json,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Demo\GenAIOps.Demo.csproj'
$arguments = @(
    'run',
    '--project', $project,
    '--configuration', 'Release',
    '--no-restore'
)
if ($NoBuild) {
    $arguments += '--no-build'
}
if ($Json) {
    $arguments += '--'
    $arguments += '--json'
}

if (-not $Json) {
    Write-Host 'DATA SOURCE: deterministic in-memory demo fixture; live operational state is not read or written.'
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "End-to-end demo failed with exit code $LASTEXITCODE."
}
