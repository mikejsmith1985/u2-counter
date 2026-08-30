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

    # Deploy with the write path switched on.
    #
    # Off by default, and the default is the claim: the deployed application
    # runs the read-only driver, which has no write path at all rather than a
    # disabled one. Passing this deploys the writable driver instead, so the
    # find-review-update flow can be exercised at the public address.
    #
    # A deliberate switch on the command line rather than a setting in a file,
    # because "is this deployment writable?" should be answerable by looking at
    # how it was deployed.
    [switch] $Writable,

    [string] $Tag
)

$ErrorActionPreference = 'Stop'

# The Azure CLI is asked not to print warnings, and this is load-bearing rather
# than tidiness.
#
# PowerShell surfaces a native command's stderr as an error record, and with
# ErrorActionPreference set to Stop a warning on stderr ends the script. The CLI
# emits one whenever a command is altered by an installed extension -- which the
# containerapp extension does on every call -- so a deployment could fail after
# both images had been built and pushed, at the point of actually deploying them,
# because a tool mentioned an extension.
#
# It surfaced intermittently, which is worse than always: several deployments
# passed straight through it before one did not.
$env:AZURE_CORE_ONLY_SHOW_ERRORS = 'true'

# UTF-8, before anything calls the Azure CLI. It does not appear to be enough.
#
# The CLI streams build logs through a library that writes directly to the
# console encoding. On Windows that stays cp1252 and throws the moment a build
# prints a tick, which every successful Docker build does. Setting the console
# encoding, the output encoding, PYTHONIOENCODING and PYTHONUTF8 all failed to
# prevent it, so these are left as the correct settings rather than as a fix.
#
# What actually makes this survivable is Assert-ImageExists below: the build
# carries on in the registry after the CLI dies, so the script waits for the tag
# to appear rather than trusting the exit it never got. Chasing the encoding
# further would be time spent making a cosmetic crash quieter.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'

# PYTHONUTF8 as well, because PYTHONIOENCODING alone did not hold: colorama
# wraps the CLI's stdout and writes through the console code page, which stays
# cp1252 and throws on the tick every successful build prints. This forces
# Python's UTF-8 mode for the whole interpreter rather than only its streams.
$env:PYTHONUTF8 = '1'

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
    # The development services stop first, and this is not tidiness. They hold
    # the assemblies the build writes, so leaving them running fails the build
    # and reports it as "the .NET suites failed" -- sending whoever reads that
    # looking for a broken test that does not exist.
    #
    # The suites start their own MCP server and database anyway, so nothing
    # here needs what is being stopped.
    Write-Step 'Stopping the development services'
    & (Join-Path $repositoryRoot 'scripts/run-dev-clean.ps1') -Stop | Out-Null

    Write-Step 'Running the test suites'

    $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

    # Built first, and separately, so a build failure is reported as one.
    #
    # "dotnet test" builds before it runs, and reports either outcome with the
    # same exit code -- so a locked assembly came back as "the .NET suites
    # failed", which is a false statement that sends the reader looking for a
    # broken test. The comment further up predicted this exact confusion and the
    # message still made it. Two commands, two messages.
    & $dotnet build (Join-Path $repositoryRoot 'api\Counter.sln') --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw 'The .NET build failed - no test has run yet. If it names a locked file, something is still using the assemblies: run scripts/run-dev-clean.ps1 -Stop. Nothing was deployed.'
    }

    & $dotnet test (Join-Path $repositoryRoot 'api\Counter.sln') --nologo -v q --no-build
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

    # Cypress last, because it needs the application running and the three suites
    # above do not. It was missing from this gate entirely, which was the wrong
    # suite to leave out: four of the nine defects this project has found came
    # from it, and the requirements it covers -- keyboard operation, the failure
    # states, accessibility -- have no other check.
    Write-Step 'Starting the application for the browser suite'
    & (Join-Path $repositoryRoot 'scripts/run-dev-clean.ps1') | Out-Null

    try {
        # Ready before the browser opens. A cold catalogue makes the first search
        # return nothing, which fails as a broken search rather than a slow start.
        $deadline = (Get-Date).AddMinutes(3)
        $isReady = $false

        while (-not $isReady -and (Get-Date) -lt $deadline) {
            try {
                $health = Invoke-RestMethod 'http://127.0.0.1:5080/health' -TimeoutSec 3
                $isReady = $health.isReady
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $isReady) {
            throw 'The application never became ready. The browser suite was not run.'
        }

        Push-Location (Join-Path $repositoryRoot 'web')
        try {
            & npm.cmd run cypress
            if ($LASTEXITCODE -ne 0) { throw 'The browser suite failed. Nothing was deployed.' }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        & (Join-Path $repositoryRoot 'scripts/run-dev-clean.ps1') -Stop | Out-Null
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

$excluded = @('.git', '.venv', 'venv', '__pycache__', '.pytest_cache', '.mypy_cache',
              '.ruff_cache', 'node_modules', 'bin', 'obj', 'dist', '.run', 'coverage',
              'videos', 'screenshots', 'downloads')

function New-BuildContext {
    <#
        Copies a source tree into a temporary directory, without the parts a
        build has no use for, and reports how large the result is.

        This exists because .dockerignore is not honoured here. The file is in
        the context root and correct, and `az acr build` uploaded 137MB of bin
        and obj directories anyway -- with .git, which is 1MB, excluded by the
        CLI's own default rules and nothing else excluded at all.

        Rather than keep guessing at pattern syntax against a builder that does
        not report what it matched, the context is built here. What is sent is
        then exactly what a reader of this function can see is sent.

        Robocopy is used for the exclusions; its exit codes below 8 all mean
        success of some kind, which is why the usual failure check is wrong.
    #>
    param([hashtable] $Trees, [string] $Name)

    $context = Join-Path ([System.IO.Path]::GetTempPath()) "counter-$Name-context-$Tag"

    if (Test-Path $context) { Remove-Item $context -Recurse -Force }
    New-Item -ItemType Directory -Path $context -Force | Out-Null

    foreach ($destination in $Trees.Keys) {
        $target = if ($destination -eq '.') { $context } else { Join-Path $context $destination }
        $arguments = @($Trees[$destination], $target, '/E', '/NFL', '/NDL',
                       '/NJH', '/NJS', '/NP', '/XD') + $excluded

        & robocopy.exe @arguments | Out-Null

        if ($LASTEXITCODE -ge 8) {
            throw "Could not stage $($Trees[$destination]) (robocopy exit $LASTEXITCODE)."
        }

        $global:LASTEXITCODE = 0
    }

    $size = [math]::Round(
        ((Get-ChildItem $context -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Step "$Name context staged: $size MB"

    return $context
}

function Assert-ImageExists {
    <#
        Confirms the tag this deploy is about to use is really in the registry.

        Waits, because the build may still be running: the CLI can lose its log
        stream and return while the build continues server-side, and a tag that
        is not there yet is not the same as one that will never be there.
    #>
    param([string] $Repository)

    $deadline = (Get-Date).AddMinutes(20)

    while ((Get-Date) -lt $deadline) {
        $tags = az acr repository show-tags `
            --name $registry `
            --repository $Repository `
            --output tsv 2>$null

        if ($tags -contains $Tag) {
            Write-Step "$Repository`:$Tag is in the registry"
            return
        }

        Start-Sleep -Seconds 15
    }

    throw "$Repository`:$Tag never appeared in $registry. Check: az acr task list-runs --registry $registry"
}

Write-Step 'Staging the API build context'
$apiContext = New-BuildContext -Name 'api' -Trees @{ '.' = $repositoryRoot }

Write-Step "Building $apiImage"
try {
    az acr build `
        --registry $registry `
        --image "counter-api:$Tag" `
        --file (Join-Path $repositoryRoot 'deploy\api.Dockerfile') `
        $apiContext | Out-Null
}
finally {
    Remove-Item $apiContext -Recurse -Force -ErrorAction SilentlyContinue
}

# Checked, because a failing `az` does not stop PowerShell on its own.
# ErrorActionPreference governs cmdlets; a native command that exits non-zero
# simply returns, and the script would go on to deploy an image that was never
# built -- reporting success while the app fails to pull.
Assert-ImageExists -Repository 'counter-api'

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
$mcpContext = New-BuildContext -Name 'mcp' -Trees @{
    'u2-mcp'         = $forkRoot
    'counter\mvstore' = (Join-Path $repositoryRoot 'mvstore')
}

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

Assert-ImageExists -Repository 'counter-mcp'

# The server validates its connection settings at startup whichever driver is
# loaded, so it refuses to start without a password even though the demonstration
# driver never authenticates against anything. The image deliberately does not
# bake one in -- a password in an image layer is a password in a registry -- so
# the deployment supplies it.
#
# It is passed as a container-app secret rather than a plain environment variable
# because that is how the real one would arrive, and a demonstration that handles
# its placeholder differently from the real thing has not demonstrated the
# handling. Nothing authenticates against this value: there is no Universe behind
# it.
$u2Password = if ($env:U2_PASSWORD) { $env:U2_PASSWORD } else {
    'demo-no-database-behind-this'
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

# -- accepting the exposure risk, deliberately --------------------------------
#
# The fork refuses to serve unauthenticated MCP on a reachable interface. That
# refusal is the first fix made to it, and it blocked this deployment -- which is
# the check working rather than a problem with it.
#
# The container must bind 0.0.0.0 or the platform cannot route to it; loopback is
# not an option here. Authentication would need an identity provider this
# demonstration does not have. So the risk is accepted, and the reason it is
# acceptable is the network rather than the server:
#
#   - the app has internal ingress only, so it has no public address at all
#   - nothing outside this Container Apps environment can reach it
#   - the only thing inside the environment is the API in front of it
#   - the driver is the demonstration store; there is no Universe behind it
#
# That is a boundary, not an authentication, and the distinction matters: anything
# that gained a foothold inside the environment would reach this server freely. A
# deployment against a real database would set U2_AUTH_ENABLED and put an identity
# provider in front, and the fork supports that -- it is simply not exercised here.
#
# Written down rather than quietly set, because a security control switched off
# without a recorded reason is one nobody can review.

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

# Which driver the MCP server loads, and whether it may act.
#
# Three settings rather than one, because they are three independent gates and
# each answers a different question: which driver is loaded, whether that driver
# may act, and whether the MCP server permits writes at all. All three have to
# agree, and each defaults to no.
#
# That was not a design on paper -- deploying with the first two produced
# "Write operations disabled in read-only mode" from the server itself, because
# U2_READ_ONLY defaults to true in this fork. The guard worked before anybody
# tested it, which is the point of defaults that fail closed.
if ($Writable) {
    Write-Host '  WRITABLE: this deployment can change ERP records' -ForegroundColor Yellow
    $mcpDriver = 'U2_DRIVER=mvstore.writable_driver'
    $mcpWritable = 'MVSTORE_WRITABLE=true'
    $mcpReadOnly = 'U2_READ_ONLY=false'
    $apiWritable = 'Erp__Writable=true'
}
else {
    $mcpDriver = 'U2_DRIVER=demo'
    $mcpWritable = 'MVSTORE_WRITABLE=false'
    $mcpReadOnly = 'U2_READ_ONLY=true'
    $apiWritable = 'Erp__Writable=false'
}

Write-Step "Deploying $($environment.McpApp) (internal, scales to zero)"

if (Test-AppExists $environment.McpApp) {
    # The secret and its reference are set on the update path too, not only on
    # create. An app created before this existed has neither, and a redeploy that
    # only swapped the image left the container refusing to start for a missing
    # password -- while the deploy reported success, because the image really had
    # been deployed.
    az containerapp secret set `
        --resource-group $environment.ResourceGroup `
        --name $environment.McpApp `
        --secrets "u2-password=$u2Password" | Out-Null

    az containerapp update `
        --resource-group $environment.ResourceGroup `
        --name $environment.McpApp `
        --image $mcpImage `
        --min-replicas 0 `
        --max-replicas 1 `
        --set-env-vars $mcpDriver $mcpWritable $mcpReadOnly 'MVSTORE_DATA_PATH=/srv/data' `
                       'U2_PASSWORD=secretref:u2-password' `
                       'U2_ALLOW_UNAUTHENTICATED_NETWORK_ACCESS=true' | Out-Null
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
        --secrets "u2-password=$u2Password" `
        --env-vars $mcpDriver $mcpWritable $mcpReadOnly 'MVSTORE_DATA_PATH=/srv/data' `
                   'U2_PASSWORD=secretref:u2-password' `
                   'U2_ALLOW_UNAUTHENTICATED_NETWORK_ACCESS=true' | Out-Null
}

$mcpEndpoint = "http://$($environment.McpApp)"

# The assistant's key, if this machine has one injected.
#
# Read from the environment and handed straight to Azure as a secret, so the
# value is never written into this script, into a file, or into the app's
# environment variables where `az containerapp show` would print it back.
#
# Absent is a supported state and the deployment says so rather than failing: the
# application runs without an assistant, and the panel that would offer one
# simply does not render.
$assistantKey = $env:SMITHBROS_CLAUDE_API_KEY

if ($assistantKey) {
    Write-Host '  an assistant key was found and will be deployed as a secret' -ForegroundColor DarkGray
    $assistantSetting = 'ANTHROPIC_API_KEY=secretref:anthropic-api-key'
}
else {
    Write-Host '  no assistant key in the environment; deploying without one' -ForegroundColor Yellow
    $assistantSetting = ''
}

# -- the API, public -----------------------------------------------------------
# One replica at most as well, because the audit trail is a SQLite file on an SMB
# share and SQLite's locking is only safe there with a single writer. For a
# demonstration that is no constraint at all; for anything larger the audit trail
# would move to a database server and this cap would go with it.

Write-Step "Deploying $($environment.ApiApp) (public, scales to zero)"

$auditConnection = 'Data Source=/audit/counter.db'

# Set before the environment variable that references it, because a secretref
# naming a secret the app does not have is accepted and then fails at start-up.
if ($assistantKey -and (Test-AppExists $environment.ApiApp)) {
    az containerapp secret set `
        --resource-group $environment.ResourceGroup `
        --name $environment.ApiApp `
        --secrets "anthropic-api-key=$assistantKey" | Out-Null
}

if (Test-AppExists $environment.ApiApp) {
    az containerapp update `
        --resource-group $environment.ResourceGroup `
        --name $environment.ApiApp `
        --image $apiImage `
        --min-replicas 0 `
        --max-replicas 1 `
        --set-env-vars "Erp__Endpoint=$mcpEndpoint/" `
                       "ConnectionStrings__Counter=$auditConnection" `
                       $assistantSetting $apiWritable | Out-Null
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
        --secrets "anthropic-api-key=$assistantKey" `
        --env-vars "Erp__Endpoint=$mcpEndpoint/" "ConnectionStrings__Counter=$auditConnection" `
                   $assistantSetting $apiWritable | Out-Null

}


$url = az containerapp show `
    --resource-group $environment.ResourceGroup `
    --name $environment.ApiApp `
    --query 'properties.configuration.ingress.fqdn' `
    --output tsv

# -- the audit share, checked on every deployment ------------------------------
#
# This used to run only when the app was created, with a comment saying a routine
# redeploy never had to touch it. That was wrong in a way that cost a day: it made
# the mount options unchangeable. Adding `nobrl` to this script changed nothing,
# reported success, and left the audit trail broken -- because the patch looked
# for `volumes: null` and there was no longer a null to replace.
#
# A setting that can only be applied once is a setting nobody can fix. So this
# runs every time, compares what is deployed against what is wanted, and only
# writes when they differ -- which keeps the revision count down without making
# the configuration write-once.
function Set-AuditShare {
    param(
        [Parameter(Mandatory)] $Environment,
        [Parameter(Mandatory)] [string] $Tag
    )

    # `nobrl` is load-bearing rather than tuning. SQLite coordinates writers with
    # byte-range locks and the SMB server behind an Azure Files share does not
    # honour them, so the audit schema could not be created at all: CREATE TABLE
    # __EFMigrationsLock waited out the command timeout and failed with "database
    # is locked". Every write afterwards failed on a table that did not exist.
    #
    # The uid and gid are load-bearing too, for a plainer reason: the share mounts
    # as root and the .NET images run as uid 1654, so without them the application
    # cannot create its own database file.
    $wanted = 'uid=1654,gid=1654,dir_mode=0755,file_mode=0644,nobrl'

    $current = az containerapp show `
        --resource-group $Environment.ResourceGroup `
        --name $Environment.ApiApp `
        --query "properties.template.volumes[?name=='audit'].mountOptions" `
        --output tsv

    if ($current -eq $wanted) {
        Write-Host "  the audit share is mounted as it should be" -ForegroundColor DarkGray
        return
    }

    Write-Step 'Mounting the audit share'

    if ($current) {
        Write-Host "  changing: $current" -ForegroundColor Yellow
        Write-Host "        to: $wanted" -ForegroundColor Yellow
    }

    $yamlPath = Join-Path ([System.IO.Path]::GetTempPath()) "counter-api-$Tag.yaml"

    az containerapp show `
        --resource-group $Environment.ResourceGroup `
        --name $Environment.ApiApp `
        --output yaml > $yamlPath

    $definition = Get-Content $yamlPath -Raw

    # Two cases, because the app may or may not already have the blocks. On a
    # freshly created app they are null and the whole block is inserted; on an
    # existing one only the options line needs to change. Handling just the first
    # is what made this write-once.
    if ($definition -match '(?m)^\s*volumes: null\s*$') {
        $definition = $definition -replace '(?m)^(\s*)volumes: null\s*$', @"
`$1volumes:
`$1- name: audit
`$1  storageName: $($Environment.StorageLink)
`$1  storageType: AzureFile
`$1  mountOptions: $wanted
"@
    }
    else {
        # The dash matters. The CLI renders the volume as a list item, so the
        # line is "    - mountOptions: ...", and a pattern anchored on whitespace
        # alone matches nothing -- silently, replacing no text and reporting
        # success. That is the same shape of failure as the one this function
        # exists to fix, which is why the result is read back below.
        $definition = $definition -replace '(?m)^(\s*(?:- )?)mountOptions: .*$', "`$1mountOptions: $wanted"
    }

    if ($definition -match '(?m)^\s*volumeMounts: null\s*$') {
        $definition = $definition -replace '(?m)^(\s*)volumeMounts: null\s*$', @"
`$1volumeMounts:
`$1- volumeName: audit
`$1  mountPath: /audit
"@
    }

    Set-Content -Path $yamlPath -Value $definition -Encoding UTF8

    az containerapp update `
        --resource-group $Environment.ResourceGroup `
        --name $Environment.ApiApp `
        --yaml $yamlPath | Out-Null

    Remove-Item $yamlPath -Force -ErrorAction SilentlyContinue

    # Read it back. A configuration change that reports success and did nothing is
    # the defect this function exists because of.
    $applied = az containerapp show `
        --resource-group $Environment.ResourceGroup `
        --name $Environment.ApiApp `
        --query "properties.template.volumes[?name=='audit'].mountOptions" `
        --output tsv

    if ($applied -ne $wanted) {
        throw "The audit share still mounts as '$applied' rather than '$wanted'."
    }

    Write-Host "  mounted with $wanted" -ForegroundColor Green
}

# -- prove the deployment, rather than announcing it ---------------------------
#
# Everything above this line checks that Azure accepted what it was given. That
# is not the same as the application working, and the difference is not
# theoretical: a broken audit trail survived seven deployments of this script.
# Each one reported success, because each one was told the update had been
# accepted and asked nothing further.
#
# The application could not create its schema -- SQLite cannot take a write lock
# on the Azure Files share -- so every audit write failed on a table that did not
# exist. The pre-deploy gate did check /health, but on the local build, where the
# database sits on a local disk and works. The one environment where it was
# broken was the one nothing asked.
#
# So this asks the deployed application, and treats a wrong answer as a failed
# deployment.
function Assert-DeploymentAnswers {
    param(
        [Parameter(Mandatory)] [string] $Url
    )

    Write-Host ''
    Write-Host 'Checking the deployed application...' -ForegroundColor Cyan

    # Generous, and deliberately so: this is the first request after a
    # deployment, so it is waking a container from zero. Measured cold starts
    # here run to about a minute.
    $deadline = (Get-Date).AddSeconds(180)
    $health = $null

    # Both conditions in the same loop, and this is not fussiness. A deployment
    # switches revisions, so for a few seconds the address still answers from the
    # old one -- which reports its own state, not the new one's. Checking
    # readiness first and durability afterwards read one answer from each and
    # failed a deployment that had in fact worked.
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod "https://$Url/health" -TimeoutSec 60
            if ($health.isReady -and $health.isAuditDurable) { break }
        }
        catch {
            # A refusal here is the container still starting, which is expected
            # and is why this loop exists rather than a single request.
            $health = $null
        }

        Start-Sleep -Seconds 5
    }

    if ($null -eq $health -or -not $health.isReady) {
        throw "The deployed application did not become ready within three minutes."
    }

    Write-Host "  ready, $($health.catalogueCount) parts searchable" -ForegroundColor Green

    # The check that would have caught the defect above. `isAuditDurable` reports
    # whether anything is actually being stored, so a false here means the
    # application is answering questions and recording none of them.
    if (-not $health.isAuditDurable) {
        throw @"
The deployed application reports that its audit trail is not durable.

    $($health.detail)

Every request is being answered and none is being recorded. Check the
container log for the reason the schema could not be opened.
"@
    }

    Write-Host '  the audit trail is durable' -ForegroundColor Green

    # One real search, because a catalogue count proves the catalogue was read
    # and not that a question can be answered from it.
    #
    # Retried, because the request immediately after a deployment can meet a
    # replica that is still starting and come back as a gateway timeout from the
    # platform rather than an answer from the application. That is the cold start
    # this deployment is designed around, not a fault -- so it is waited out
    # rather than reported. An answer that arrives and is wrong still fails.
    $found = $null

    foreach ($attempt in 1..4) {
        try {
            $found = Invoke-RestMethod "https://$Url/api/v1/parts?q=breaker" -TimeoutSec 90
            break
        }
        catch {
            if ($attempt -eq 4) {
                throw "The deployed application could not answer a search: $($_.Exception.Message)"
            }

            Write-Host "  the search timed out, waking (attempt $attempt)" -ForegroundColor DarkGray
            Start-Sleep -Seconds 10
        }
    }

    if ($found.results.Count -eq 0) {
        throw "The deployed application returned no results for a search that should match."
    }

    Write-Host "  a search answered with $($found.results.Count) parts" -ForegroundColor Green
}

Set-AuditShare -Environment $environment -Tag $Tag

Assert-DeploymentAnswers -Url $url

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

