[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('RegisterCandidate', 'Promote', 'Rollback')]
    [string] $Action,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string] $ApiUrl,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $RegistryId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Actor,

    [string] $Version,
    [string] $AgentId
)

$ErrorActionPreference = 'Stop'
$baseUrl = $ApiUrl.TrimEnd('/')
$headers = @{
    'Content-Type' = 'application/json'
}

if ($Action -eq 'RegisterCandidate') {
    if ([string]::IsNullOrWhiteSpace($Version) -or [string]::IsNullOrWhiteSpace($AgentId)) {
        throw 'RegisterCandidate requires both -Version and -AgentId.'
    }
    $uri = "$baseUrl/api/candidates/$([Uri]::EscapeDataString($Version))"
    $body = @{ registryId = $RegistryId; actor = $Actor; agentId = $AgentId } | ConvertTo-Json
}
else {
    $snapshot = Invoke-RestMethod `
        -Method Get `
        -Uri "$baseUrl/api/versions?registryId=$([Uri]::EscapeDataString($RegistryId))"
    if ([string]::IsNullOrWhiteSpace($snapshot.eTag)) {
        throw "Registry '$RegistryId' did not return an ETag."
    }

    $headers['If-Match'] = $snapshot.eTag
    $headers['Idempotency-Key'] = "$($Action.ToLowerInvariant())-$RegistryId-$Version"
    $body = @{ registryId = $RegistryId; actor = $Actor } | ConvertTo-Json
    if ($Action -eq 'Promote') {
        if ([string]::IsNullOrWhiteSpace($Version)) {
            throw 'Promote requires -Version.'
        }
        $uri = "$baseUrl/api/promote/$([Uri]::EscapeDataString($Version))"
    }
    else {
        $uri = "$baseUrl/api/rollback"
    }
}

$result = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $body
if (($Action -eq 'Promote' -or $Action -eq 'Rollback') -and
    -not [string]::IsNullOrWhiteSpace($Version) -and
    $result.production.promptVersion -ne $Version) {
    throw "$Action completed with production version '$($result.production.promptVersion)' instead of expected '$Version'."
}
$result | ConvertTo-Json -Depth 20
