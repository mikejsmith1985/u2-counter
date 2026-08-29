<#
.SYNOPSIS
    Creates the Azure resources this demonstration runs on.

.DESCRIPTION
    Run once. Creating infrastructure and deploying code are separate jobs with
    different failure modes and different frequencies -- deploy.ps1 runs many
    times a day and must not be able to delete a database by being run with a
    typo.

    The shape is deliberate and is most of what this script is for:

      internet ──▶ counter-api      (external ingress, HTTPS)
                        │
                        ▼  private network only
                   counter-mcp      (internal ingress, no public address)
                        │
                        ▼
                   Azure SQL        (the audit trail; no ERP data)

    The MCP server holds the database session and enforces the read-only rules.
    Giving it a public address would mean those rules were the only thing
    standing between the internet and a Universe account, and a rule enforced in
    one place is a rule with one way to be wrong. Internal ingress means the only
    thing that can reach it is the API in the same environment.

    Nothing here is a hosted pipeline. Article VIII: releases run from a local
    script, so what deployed is what was on the machine that deployed it.

.PARAMETER ResourceGroup
    Where everything lives. Deleting this deletes the demonstration.

.PARAMETER Location
    Azure region.

.PARAMETER NamePrefix
    Prefix for every resource name, so two people can each have their own.

.PARAMETER SqlAdminUser
    The Azure SQL administrator login.

.EXAMPLE
    ./deploy/azure/provision.ps1 -ResourceGroup counter-demo -SqlAdminUser counteradmin
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $Location = 'eastus',
    [string] $NamePrefix = 'counter',
    [Parameter(Mandatory)] [string] $SqlAdminUser
)

$ErrorActionPreference = 'Stop'

$registryName = "$($NamePrefix)acr$(Get-Random -Minimum 1000 -Maximum 9999)"
$environmentName = "$NamePrefix-env"
$sqlServerName = "$NamePrefix-sql-$(Get-Random -Minimum 1000 -Maximum 9999)"
$sqlDatabaseName = 'Counter'
$vaultName = "$NamePrefix-kv-$(Get-Random -Minimum 1000 -Maximum 9999)"

function Write-Step {
    param([string] $Message)
    Write-Host "  $Message" -ForegroundColor Cyan
}

function Assert-AzureCli {
    <#
        Confirms the tooling is present and signed in before anything is created.
        Failing halfway leaves resources nobody remembers to remove, and they
        keep costing money.
    #>
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'The Azure CLI was not found. Install it and run "az login".'
    }

    $account = az account show 2>$null | ConvertFrom-Json
    if (-not $account) {
        throw 'Not signed in to Azure. Run "az login" first.'
    }

    Write-Step "Signed in to subscription $($account.name)"
}

Assert-AzureCli

Write-Step 'Registering the Container Apps provider'
az provider register --namespace Microsoft.App --wait | Out-Null
az provider register --namespace Microsoft.OperationalInsights --wait | Out-Null

Write-Step "Creating resource group $ResourceGroup"
az group create --name $ResourceGroup --location $Location | Out-Null

Write-Step "Creating container registry $registryName"
az acr create `
    --resource-group $ResourceGroup `
    --name $registryName `
    --sku Basic `
    --admin-enabled true | Out-Null

Write-Step "Creating Container Apps environment $environmentName"
az containerapp env create `
    --resource-group $ResourceGroup `
    --name $environmentName `
    --location $Location | Out-Null

# -- the audit trail's database ----------------------------------------------
# No ERP data reaches this. It holds who asked what, which is the record the
# shared database login cannot produce on its own.

Write-Step "Creating SQL server $sqlServerName"
Write-Host ''
Write-Host '  The SQL administrator password is read directly by the Azure CLI.' -ForegroundColor Yellow
Write-Host '  It is not stored in this script, echoed, or written to a file.' -ForegroundColor Yellow

az sql server create `
    --resource-group $ResourceGroup `
    --name $sqlServerName `
    --location $Location `
    --admin-user $SqlAdminUser | Out-Null

Write-Step "Creating database $sqlDatabaseName"
az sql db create `
    --resource-group $ResourceGroup `
    --server $sqlServerName `
    --name $sqlDatabaseName `
    --service-objective Basic | Out-Null

# Azure services only. There is no rule admitting an arbitrary address, because
# nothing outside this environment has a reason to reach the audit trail.
Write-Step 'Allowing Azure services to reach the database'
az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $sqlServerName `
    --name AllowAzureServices `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0 | Out-Null

# -- secrets ------------------------------------------------------------------
# The connection string is placed in Key Vault by whoever provisions, and read
# from there by the container app. It does not pass through this script's output,
# its variables, or the shell history.

Write-Step "Creating key vault $vaultName"
az keyvault create `
    --resource-group $ResourceGroup `
    --name $vaultName `
    --location $Location `
    --enable-rbac-authorization false | Out-Null

Write-Host ''
Write-Host '  Store the connection string yourself, so this script never sees it:' -ForegroundColor Yellow
Write-Host "    az keyvault secret set --vault-name $vaultName --name counter-sql --value '<connection string>'" -ForegroundColor Yellow
Write-Host ''

# -- the record of what was made ----------------------------------------------
# Written to a file rather than printed, because deploy.ps1 reads it. Deriving
# these names again there would mean two places that have to agree about a
# random suffix.

$environmentFile = Join-Path $PSScriptRoot 'environment.json'

[pscustomobject]@{
    ResourceGroup   = $ResourceGroup
    Location        = $Location
    Registry        = $registryName
    Environment     = $environmentName
    SqlServer       = $sqlServerName
    SqlDatabase     = $sqlDatabaseName
    KeyVault        = $vaultName
    ApiApp          = "$NamePrefix-api"
    McpApp          = "$NamePrefix-mcp"
    ProvisionedAt   = (Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Depth 3 | Set-Content -Path $environmentFile -Encoding UTF8

Write-Host 'Provisioned.' -ForegroundColor Green
Write-Host "  Names recorded in $environmentFile"
Write-Host '  Next: store the connection string above, then run deploy/azure/deploy.ps1'
