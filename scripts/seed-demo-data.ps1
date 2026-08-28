<#
.SYNOPSIS
    Generates the demonstration data set and verifies it exercises every rule.

.DESCRIPTION
    Fails if any seed obligation is unmet. A data set that cannot exercise a rule
    proves nothing about that rule, and a test suite running against one would
    pass while telling nobody anything — so an incomplete data set must never
    reach a test run.

.PARAMETER Parts
    How many catalogue items to generate. Default 3000.

.PARAMETER Seed
    Random seed. The same seed always produces the same data, so a reviewer
    investigating a defect can recreate the data that produced it.

.PARAMETER Force
    Replace an existing data set rather than refusing.

.EXAMPLE
    ./scripts/seed-demo-data.ps1
    ./scripts/seed-demo-data.ps1 -Parts 500 -Force
#>

[CmdletBinding()]
param(
    [int] $Parts = 3000,
    [int] $Branches = 12,
    [int] $Customers = 150,
    [int] $Orders = 800,
    [int] $Seed = 20260828,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$storeRoot = Join-Path $repositoryRoot 'mvstore'
$dataDirectory = Join-Path $storeRoot 'data'
$python = Join-Path $storeRoot '.venv/Scripts/python.exe'

if (-not (Test-Path $python)) {
    throw "No virtual environment at $python. Run: uv venv --python 3.12 mvstore/.venv"
}

if ((Test-Path $dataDirectory) -and -not $Force) {
    throw "Data already exists at $dataDirectory. Pass -Force to replace it."
}

if (Test-Path $dataDirectory) {
    Write-Host "Removing existing data set"
    Remove-Item $dataDirectory -Recurse -Force
}

Write-Host "Generating $Parts parts across $Branches branches, $Customers customers, $Orders orders"

# The generator reports unmet obligations on the last line, so the script can
# fail on data that would not exercise the rules.
$generatorScript = @"
import json, sys
from mvstore.seed import generate, verify_obligations
from mvstore.store import MultiValueStore

store = MultiValueStore(r'$dataDirectory')
generate(store, seed=$Seed, parts=$Parts, branches=$Branches,
         customers=$Customers, orders=$Orders)

counts = {name: len(store.keys(name))
          for name in ('PRODUCT', 'INVENTORY', 'BRANCH', 'CUSTOMER', 'PRICING', 'ORDER')}
print(json.dumps({'counts': counts, 'unmet': verify_obligations(store)}))
"@

$output = & $python -c $generatorScript
if ($LASTEXITCODE -ne 0) {
    throw "Generation failed: $output"
}

$result = $output | Select-Object -Last 1 | ConvertFrom-Json

Write-Host ''
Write-Host 'Records written:' -ForegroundColor Green
foreach ($file in $result.counts.PSObject.Properties) {
    Write-Host ("  {0,-12} {1,6}" -f $file.Name, $file.Value)
}

if ($result.unmet.Count -gt 0) {
    Write-Host ''
    Write-Host 'This data set does not exercise:' -ForegroundColor Red
    foreach ($obligation in $result.unmet) {
        Write-Host "  - $obligation"
    }
    throw 'Seed obligations unmet. A rule the data cannot exercise is a rule no test proves.'
}

Write-Host ''
Write-Host 'Every seed obligation met.' -ForegroundColor Green
Write-Host "Data at $dataDirectory"
