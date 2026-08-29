<#
.SYNOPSIS
    Creates the Azure resources this demonstration runs on.

.DESCRIPTION
    Run once. Creating infrastructure and deploying code are separate jobs with
    different failure modes and different frequencies -- deploy.ps1 runs many
    times a day and must not be able to delete a database by being run with a
    typo.

    The shape is deliberate and is most of what this script is for:

      internet ──▶ counter-api      external ingress, scales to zero
                        │
                        ▼  private network only
                   counter-mcp      internal ingress, scales to zero, one replica
                        │
                        ▼
                   Azure Files      the audit trail, as a SQLite file

    Two decisions worth knowing about.

    **The MCP server has no public address.** It holds the database session and
    enforces the read-only rules. A public address would put those rules between
    the internet and a Universe account, and a rule enforced in one place has
    exactly one way to be wrong.

    **There is no database server.** Both apps scale to zero when nobody is using
    them, and a database server is the one component that could not scale down
    with them -- it would sit there costing money to hold a few thousand audit
    rows nobody is reading. The audit trail is a SQLite file on an Azure Files
    share, which costs pennies while idle and is still there when the container
    comes back.

    Nothing here is a hosted pipeline. Article VIII: releases run from a local
    script, so what deployed is what was on the machine that deployed it.

.PARAMETER ResourceGroup
    Where everything lives. Deleting this deletes the demonstration.

.PARAMETER Location
    Azure region.

.PARAMETER NamePrefix
    Prefix for every resource name, so two people can each have their own.

.EXAMPLE
    ./deploy/azure/provision.ps1 -ResourceGroup counter-demo
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $Location = 'eastus',
    [string] $NamePrefix = 'counter'
)

$ErrorActionPreference = 'Stop'

$suffix = Get-Random -Minimum 10000 -Maximum 99999
$registryName = "$($NamePrefix)acr$suffix"
$environmentName = "$NamePrefix-env"
$storageAccountName = "$($NamePrefix)st$suffix"
$shareName = 'audit'
$storageLinkName = 'audit-share'

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

Write-Step 'Registering the providers this needs'
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

# -- where the audit trail lives ----------------------------------------------
# A file share rather than a database server, because this deployment scales to
# zero and a server cannot. No ERP data goes here: what is stored is the record
# of who asked what, which is the record the shared database login cannot
# produce on its own.

Write-Step "Creating storage account $storageAccountName"
az storage account create `
    --resource-group $ResourceGroup `
    --name $storageAccountName `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --min-tls-version TLS1_2 `
    --allow-blob-public-access false | Out-Null

$storageKey = az storage account keys list `
    --resource-group $ResourceGroup `
    --account-name $storageAccountName `
    --query '[0].value' `
    --output tsv

Write-Step "Creating file share $shareName"
az storage share-rm create `
    --resource-group $ResourceGroup `
    --storage-account $storageAccountName `
    --name $shareName `
    --quota 1 | Out-Null

# The environment holds the share definition; the app mounts it by this name.
Write-Step "Linking the share to the environment as $storageLinkName"
az containerapp env storage set `
    --resource-group $ResourceGroup `
    --name $environmentName `
    --storage-name $storageLinkName `
    --azure-file-account-name $storageAccountName `
    --azure-file-account-key $storageKey `
    --azure-file-share-name $shareName `
    --access-mode ReadWrite | Out-Null

# -- the record of what was made ----------------------------------------------
# Written to a file rather than printed, because deploy.ps1 reads it. Deriving
# these names again there would mean two places that have to agree about a
# random suffix.

$environmentFile = Join-Path $PSScriptRoot 'environment.json'

[pscustomobject]@{
    ResourceGroup  = $ResourceGroup
    Location       = $Location
    Registry       = $registryName
    Environment    = $environmentName
    StorageAccount = $storageAccountName
    ShareName      = $shareName
    StorageLink    = $storageLinkName
    ApiApp         = "$NamePrefix-api"
    McpApp         = "$NamePrefix-mcp"
    ProvisionedAt  = (Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Depth 3 | Set-Content -Path $environmentFile -Encoding UTF8

Write-Host ''
Write-Host 'Provisioned.' -ForegroundColor Green
Write-Host "  Names recorded in $environmentFile"
Write-Host '  No database server, and nothing running yet: both apps scale to'
Write-Host '  zero, so this costs almost nothing until someone opens the link.'
Write-Host ''
Write-Host '  Next: ./deploy/azure/deploy.ps1'
