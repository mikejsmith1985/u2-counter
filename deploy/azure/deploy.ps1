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

    Both apps scale to zero. See the comments at each app for what that costs and
    what was done about it.

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
    if ((git -C $repositoryRoot status --porcelain)) {
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

# Resolved before anything is built, because a missing fork should stop this
# before it spends several minutes building the other image.
$forkRoot = if ($env:U2_MCP_ROOT) { $env:U2_MCP_ROOT } else {
    Join-Path (Split-Path -Parent $repositoryRoot) 'u2-mcp'
}

if (-not (Test-Path $forkRoot)) {
    throw "The hardened fork was not found at $forkRoot. Set U2_MCP_ROOT."
}

Write-Step "Building $apiImage"
az acr build `
    --registry $registry `
    --image "counter-api:$Tag" `
    --file (Join-Path $repositoryRoot 'deploy\api.Dockerfile') `
    $repositoryRoot | Out-Null

# Staged into a directory of its own rather than built from the shared parent.
#
# The parent is somebody's projects folder. Using it as a build context uploads
# every unrelated repository beside this one -- hundreds of megabytes, other
# people's work, and anything private that happens to live there -- to a registry,
# to be discarded on arrival. A .dockerignore cannot fix that without dropping a
# file into a directory this project does not own.
#
# So the context is built here, holding the two things the image copies.
Write-Step 'Staging the MCP build context'

$mcpContext = Join-Path ([System.IO.Path]::GetTempPath()) "counter-mcp-context-$Tag"

if (Test-Path $mcpContext) { Remove-Item $mcpContext -Recurse -Force }
New-Item -ItemType Directory -Path $mcpContext -Force | Out-Null

$excluded = @('.git', '.venv', 'venv', '__pycache__', '.pytest_cache', '.mypy_cache',
              '.ruff_cache', 'node_modules', 'bin', 'obj', 'dist', '.run')

function Copy-Source {
    <#
        Copies a source tree without the directories a build has no use for.
        Robocopy is used for the exclusions; its exit codes below 8 all mean
        success of some kind, which is why the usual failure check is wrong here.
    #>
    param([string] $From, [string] $To)

    $arguments = @($From, $To, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/XD') + $excluded
    & robocopy.exe @arguments | Out-Null

    if ($LASTEXITCODE -ge 8) {
        throw "Could not stage $From (robocopy exit $LASTEXITCODE)."
    }

    $global:LASTEXITCODE = 0
}

Copy-Source $forkRoot (Join-Path $mcpContext 'u2-mcp')
Copy-Source (Join-Path $repositoryRoot 'mvstore') (Join-Path $mcpContext 'counter\mvstore')

$stagedSize = [math]::Round(
    ((Get-ChildItem $mcpContext -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Step "Context staged: $stagedSize MB"

Write-Step "Building $mcpImage"
try {
    az acr build `
        --registry $registry `
        --image "counter-mcp:$Tag" `
        --file (Join-Path $repositoryRoot 'deploy\mcp.Dockerfile') `
        $mcpContext | Out-Null
}
finally {
    Remove-Item $mcpContext -Recurse -Force -ErrorAction SilentlyContinue
}

$registryServer = "$registry.azurecr.io"
$registryPassword = az acr credential show --name $registry --query 'passwords[0].value' --output tsv

function Test-AppExists {
    param([string] $Name)

    $existing = az containerapp show `
        --resource-group $environment.ResourceGroup `
        --name $Name `
        --query 'name' `
        --output tsv 2>$null

    return -not [string]::IsNullOrWhiteSpace($existing)
}

# -- the MCP server, private ---------------------------------------------------
# Internal ingress. This process holds the database session and enforces the
# read-only rules; a public address on it would put those rules between the
# internet and a Universe account, and one rule in one place has one way to be
# wrong.
#
# One replica at most, deliberately. The server holds a single database session,
# and a second replica would hold a second -- the connection multiplication the
# fork was hardened against, reintroduced by the deployment rather than by the
# code.
#
# Zero replicas at rest. It wakes when the API calls it, which adds a few seconds
# to the first request after a quiet period and nothing after that.

Write-Step "Deploying $($environment.McpApp) (internal, scales to zero)"

if (Test-AppExists $environment.McpApp) {
    az containerapp update `
        --resource-group $environment.ResourceGroup `
        --name $environment.McpApp `
        --image $mcpImage `
        --min-replicas 0 `
        --max-replicas 1 | Out-Null
}
else {
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
        --transport http `
        --min-replicas 0 `
        --max-replicas 1 `
        --cpu 0.5 --memory 1.0Gi `
        --env-vars 'U2_DRIVER=demo' 'MVSTORE_DATA_PATH=/srv/data' | Out-Null
}

$mcpEndpoint = "http://$($environment.McpApp)"

# -- the API, public -----------------------------------------------------------
# One replica at most as well, because the audit trail is a SQLite file on an SMB
# share and SQLite's locking is only safe there with a single writer. For a
# demonstration that is no constraint at all; for anything larger the audit trail
# would move to a database server and this cap would go with it.

Write-Step "Deploying $($environment.ApiApp) (public, scales to zero)"

$auditConnection = 'Data Source=/audit/counter.db'

if (Test-AppExists $environment.ApiApp) {
    az containerapp update `
        --resource-group $environment.ResourceGroup `
        --name $environment.ApiApp `
        --image $apiImage `
        --min-replicas 0 `
        --max-replicas 1 | Out-Null
}
else {
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
        --min-replicas 0 `
        --max-replicas 1 `
        --cpu 1.0 --memory 2.0Gi `
        --env-vars "Erp__Endpoint=$mcpEndpoint/" "ConnectionStrings__Counter=$auditConnection" | Out-Null

    # The share is attached by editing the app's YAML, because the CLI has no
    # flag for a volume mount on create. Done once, at creation, so a routine
    # redeploy never has to touch it.
    Write-Step 'Mounting the audit share'

    $yamlPath = Join-Path ([System.IO.Path]::GetTempPath()) "counter-api-$Tag.yaml"

    az containerapp show `
        --resource-group $environment.ResourceGroup `
        --name $environment.ApiApp `
        --output yaml > $yamlPath

    $definition = Get-Content $yamlPath -Raw

    # Appended to the template rather than rewritten, so nothing the CLI put
    # there is lost. The two blocks are the volume and the mount that uses it.
    $definition = $definition -replace '(?m)^(\s*)volumes: null\s*$', @"
`$1volumes:
`$1- name: audit
`$1  storageName: $($environment.StorageLink)
`$1  storageType: AzureFile
"@

    $definition = $definition -replace '(?m)^(\s*)volumeMounts: null\s*$', @"
`$1volumeMounts:
`$1- volumeName: audit
`$1  mountPath: /audit
"@

    Set-Content -Path $yamlPath -Value $definition -Encoding UTF8

    az containerapp update `
        --resource-group $environment.ResourceGroup `
        --name $environment.ApiApp `
        --yaml $yamlPath | Out-Null

    Remove-Item $yamlPath -Force -ErrorAction SilentlyContinue
}

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
Write-Host '  Both apps scale to zero when idle. The first request after a quiet'
Write-Host '  period wakes them, which the screen says plainly rather than'
Write-Host '  appearing to be broken.'
Write-Host ''
Write-Host '  The MCP server has no public address. Nothing outside the'
Write-Host '  environment can reach the database session it holds.'
