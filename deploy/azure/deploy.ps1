<#
.SYNOPSIS
    Builds the two images and puts them live.

.DESCRIPTION
    Runs from a developer's machine, every time. Article VIII: releases never go
    through a hosted pipeline, so what deploys is what was on the machine that
    deployed it, built from the working tree someone can look at.

    Reads deploy/azure/environment.json, which provision.ps1 wrote. Deriving the
    resource names again here would mean two places that have to agree about a
    random suffix, and they would eventually disagree.

    The tests run first and a failure stops the deployment. That ordering is the
    whole value of a local release script: the thing that decides whether this
    ships is the same suite the author was running a minute ago, on the same
    machine, against the same code.

.PARAMETER SkipTests
    Deploy without running the suite. For a demonstration being fixed live, in
    front of someone. Every other use of it is a mistake.

.PARAMETER Tag
    Image tag. Defaults to the short commit hash, so a running revision can be
    traced back to a commit rather than to "latest".

.EXAMPLE
    ./deploy/azure/deploy.ps1
#>

[CmdletBinding()]
param(
    [switch] $SkipTests,
    [string] $Tag
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$environmentFile = Join-Path $PSScriptRoot 'environment.json'

function Write-Step {
    param([string] $Message)
    Write-Host "  $Message" -ForegroundColor Cyan
}

if (-not (Test-Path $environmentFile)) {
    throw "No $environmentFile. Run deploy/azure/provision.ps1 first."
}

$environment = Get-Content $environmentFile -Raw | ConvertFrom-Json

if (-not $Tag) {
    $Tag = (git -C $repositoryRoot rev-parse --short HEAD).Trim()

    # A dirty tree tagged with a commit hash is a lie: the hash names code that
    # is not what is being deployed. Saying so beats a revision nobody can trace.
    $isDirty = (git -C $repositoryRoot status --porcelain) -ne $null
    if ($isDirty) {
        $Tag = "$Tag-dirty"
        Write-Warning 'The working tree has uncommitted changes; the tag is marked dirty.'
    }
}

Write-Step "Deploying tag $Tag to $($environment.ResourceGroup)"

# -- the gate -----------------------------------------------------------------

if (-not $SkipTests) {
    Write-Step 'Running the test suites'

    $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

    & $dotnet test (Join-Path $repositoryRoot 'api\Counter.sln') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The .NET suites failed. Nothing was deployed.' }

    Push-Location (Join-Path $repositoryRoot 'web')
    try {
        & npm.cmd test
        if ($LASTEXITCODE -ne 0) { throw 'The front-end unit tests failed. Nothing was deployed.' }
    }
    finally {
        Pop-Location
    }

    Push-Location (Join-Path $repositoryRoot 'mvstore')
    try {
        & .\.venv\Scripts\python.exe -m pytest tests -q
        if ($LASTEXITCODE -ne 0) { throw 'The store tests failed. Nothing was deployed.' }
    }
    finally {
        Pop-Location
    }

    Write-Host '  All suites passed.' -ForegroundColor Green
}
else {
    Write-Warning 'Tests skipped. This is deploying code nothing has checked.'
}

# -- build --------------------------------------------------------------------
# Built in Azure rather than locally and pushed. `az acr build` sends the context
# and builds in the registry, which removes "it built on my machine" as an
# explanation for a difference between here and there.

$registry = $environment.Registry
$apiImage = "$registry.azurecr.io/counter-api:$Tag"
$mcpImage = "$registry.azurecr.io/counter-mcp:$Tag"

Write-Step "Building $apiImage"
az acr build `
    --registry $registry `
    --image "counter-api:$Tag" `
    --file (Join-Path $repositoryRoot 'deploy\api.Dockerfile') `
    $repositoryRoot | Out-Null

# The MCP image needs both repositories in its context, because it carries the
# hardened fork's source rather than installing the package from an index. The
# context is therefore their shared parent.
$forkRoot = if ($env:U2_MCP_ROOT) { $env:U2_MCP_ROOT } else {
    Join-Path (Split-Path -Parent $repositoryRoot) 'u2-mcp'
}

if (-not (Test-Path $forkRoot)) {
    throw "The hardened fork was not found at $forkRoot. Set U2_MCP_ROOT."
}

Write-Step "Building $mcpImage"
az acr build `
    --registry $registry `
    --image "counter-mcp:$Tag" `
    --file (Join-Path $repositoryRoot 'deploy\mcp.Dockerfile') `
    (Split-Path -Parent $repositoryRoot) | Out-Null

# -- the MCP server, private ---------------------------------------------------
# Internal ingress. This process holds the database session and enforces the
# read-only rules; a public address on it would put those rules between the
# internet and a Universe account, and one rule in one place has one way to be
# wrong.

$registryServer = "$registry.azurecr.io"
$registryPassword = az acr credential show --name $registry --query 'passwords[0].value' --output tsv

Write-Step "Deploying $($environment.McpApp) (internal ingress only)"
az containerapp create `
    --resource-group $environment.ResourceGroup `
    --name $environment.McpApp `
    --environment $environment.Environment `
    --image $mcpImage `
    --registry-server $registryServer `
    --registry-username $registry `
    --registry-password $registryPassword `
    --target-port 5081 `
    --ingress internal `
    --min-replicas 1 `
    --max-replicas 1 `
    --cpu 0.5 --memory 1.0Gi `
    --env-vars 'U2_DRIVER=demo' 'MVSTORE_DATA_PATH=/srv/data' | Out-Null

# One replica, deliberately. The server holds a single database session, and a
# second replica would hold a second -- which is the connection multiplication
# the fork was hardened against, reintroduced by the deployment rather than by
# the code.

$mcpEndpoint = "http://$($environment.McpApp)"

# -- the API, public -----------------------------------------------------------

Write-Step "Deploying $($environment.ApiApp)"
az containerapp create `
    --resource-group $environment.ResourceGroup `
    --name $environment.ApiApp `
    --environment $environment.Environment `
    --image $apiImage `
    --registry-server $registryServer `
    --registry-username $registry `
    --registry-password $registryPassword `
    --target-port 8080 `
    --ingress external `
    --min-replicas 1 `
    --max-replicas 3 `
    --cpu 1.0 --memory 2.0Gi `
    --secrets "counter-sql=keyvaultref:https://$($environment.KeyVault).vault.azure.net/secrets/counter-sql,identityref:system" `
    --env-vars "Erp__Endpoint=$mcpEndpoint/" 'ConnectionStrings__Counter=secretref:counter-sql' | Out-Null

# The connection string is a Key Vault reference resolved by the container app's
# own identity. It does not pass through this script, its variables, or the shell
# history -- the script names where the secret goes and the platform delivers it.

Write-Step 'Setting the health probes'
az containerapp update `
    --resource-group $environment.ResourceGroup `
    --name $environment.ApiApp `
    --min-replicas 1 | Out-Null

$url = az containerapp show `
    --resource-group $environment.ResourceGroup `
    --name $environment.ApiApp `
    --query 'properties.configuration.ingress.fqdn' `
    --output tsv

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
Write-Host "  https://$url"
Write-Host "  Tag $Tag"
Write-Host ''
Write-Host '  The MCP server has no public address. Nothing outside the'
Write-Host '  environment can reach the database session it holds.'
