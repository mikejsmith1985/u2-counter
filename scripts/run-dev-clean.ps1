<#
.SYNOPSIS
    Starts, or stops, every service this feature needs for local development.

.DESCRIPTION
    Records the process id of everything it starts in .run/pids.json, and stops
    only those ids.

    Article II of the constitution forbids stopping processes by name pattern,
    and the reason is not theoretical: this agent may itself be running inside a
    process whose name matches a pattern like "dotnet" or "node", and a wildcard
    stop would end the session that issued it. Recording ids at start time is the
    only way to know which processes are ours.

    A recorded id is checked against its recorded start time before being
    stopped, because process ids are reused by the operating system. Stopping a
    id that has been recycled would end an unrelated program.

.PARAMETER Stop
    Stop the services recorded in .run/pids.json and exit.

.PARAMETER SkipSql
    Do not start the SQL Server container. Useful when one is already running.

.EXAMPLE
    ./scripts/run-dev-clean.ps1
    ./scripts/run-dev-clean.ps1 -Stop
#>

[CmdletBinding()]
param(
    [switch] $Stop,
    [switch] $SkipSql
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $repositoryRoot '.run'
$pidFilePath = Join-Path $runDirectory 'pids.json'
$logDirectory = Join-Path $runDirectory 'logs'

$sqlContainerName = 'counter-sql'

function Write-Step {
    param([string] $Message)
    Write-Host "  $Message"
}

function Get-RecordedProcesses {
    <#
        Reads the recorded processes, returning an empty list when the file is
        absent or unreadable. A missing file means nothing was started, which is
        not an error.
    #>
    if (-not (Test-Path $pidFilePath)) { return @() }

    try {
        $content = Get-Content $pidFilePath -Raw
        if ([string]::IsNullOrWhiteSpace($content)) { return @() }
        return @(ConvertFrom-Json $content)
    }
    catch {
        Write-Warning "Could not read $pidFilePath ($($_.Exception.Message)). Treating it as empty."
        return @()
    }
}

function Save-RecordedProcesses {
    param([object[]] $Records)

    if (-not (Test-Path $runDirectory)) {
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
    }
    ConvertTo-Json @($Records) -Depth 4 | Set-Content -Path $pidFilePath -Encoding UTF8
}

function Test-ProcessIsOurs {
    <#
        Confirms a recorded id still belongs to the process we started, by
        comparing its start time against the one recorded. Operating systems
        reuse process ids, so an id alone is not identity.
    #>
    param([int] $ProcessId, [string] $RecordedStartTime)

    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
    }
    catch {
        return $false      # Already gone.
    }

    if ([string]::IsNullOrWhiteSpace($RecordedStartTime)) { return $false }

    $recorded = [datetime]::Parse($RecordedStartTime).ToUniversalTime()
    $actual = $process.StartTime.ToUniversalTime()

    # A second of tolerance: the recorded value is written just after start.
    return ([math]::Abs(($actual - $recorded).TotalSeconds) -lt 1)
}

function Stop-RecordedProcesses {
    $records = Get-RecordedProcesses
    if ($records.Count -eq 0) {
        Write-Step 'Nothing recorded as running.'
    }

    foreach ($record in $records) {
        if (Test-ProcessIsOurs -ProcessId $record.ProcessId -RecordedStartTime $record.StartedAt) {
            Write-Step "Stopping $($record.Name) (pid $($record.ProcessId))"
            Stop-Process -Id $record.ProcessId -Force -ErrorAction SilentlyContinue
        }
        else {
            Write-Step "Skipping $($record.Name) (pid $($record.ProcessId)) - no longer ours"
        }
    }

    if (Test-Path $pidFilePath) { Remove-Item $pidFilePath -Force }

    # The SQL container is addressed by its own name, which docker guarantees is
    # unique. This is not a process-name pattern.
    if (docker ps --quiet --filter "name=^${sqlContainerName}$") {
        Write-Step "Stopping container $sqlContainerName"
        docker stop $sqlContainerName | Out-Null
    }

    Write-Host 'Stopped.' -ForegroundColor Green
}

function Start-Service {
    <#
        Starts one service, records its id and start time, and returns the record.
    #>
    param(
        [string] $Name,
        [string] $FilePath,
        [string[]] $ArgumentList,
        [string] $WorkingDirectory
    )

    $standardOut = Join-Path $logDirectory "$Name.out.log"
    $standardError = Join-Path $logDirectory "$Name.err.log"

    $process = Start-Process -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $standardOut `
        -RedirectStandardError $standardError `
        -PassThru `
        -WindowStyle Hidden

    Write-Step "Started $Name (pid $($process.Id))"

    return [pscustomobject]@{
        Name        = $Name
        ProcessId   = $process.Id
        StartedAt   = $process.StartTime.ToUniversalTime().ToString('o')
        LogPath     = $standardOut
    }
}

# -- entry point --------------------------------------------------------------

if ($Stop) {
    Stop-RecordedProcesses
    return
}

# Always clear a previous run first, so two sets of services never compete for
# the same ports.
Stop-RecordedProcesses

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

$started = @()

if (-not $SkipSql) {
    Write-Step 'Starting SQL Server container'
    docker run --detach --rm `
        --name $sqlContainerName `
        --env 'ACCEPT_EULA=Y' `
        --env "MSSQL_SA_PASSWORD=$env:COUNTER_SQL_PASSWORD" `
        --publish '1433:1433' `
        'mcr.microsoft.com/mssql/server:2022-latest' | Out-Null
}

$started += Start-Service -Name 'mvstore' `
    -FilePath 'python' `
    -ArgumentList @('-m', 'mvstore.server', '--port', '5082') `
    -WorkingDirectory (Join-Path $repositoryRoot 'mvstore')

$started += Start-Service -Name 'mcp' `
    -FilePath 'u2-mcp' `
    -ArgumentList @('--streamable-http', '--host', '127.0.0.1', '--port', '5081') `
    -WorkingDirectory $repositoryRoot

$started += Start-Service -Name 'api' `
    -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'src/Counter.Api', '--urls', 'http://127.0.0.1:5080') `
    -WorkingDirectory (Join-Path $repositoryRoot 'api')

$started += Start-Service -Name 'web' `
    -FilePath 'npm' `
    -ArgumentList @('run', 'dev') `
    -WorkingDirectory (Join-Path $repositoryRoot 'web')

Save-RecordedProcesses -Records $started

Write-Host ''
Write-Host 'Running:' -ForegroundColor Green
Write-Host '  Front end   http://127.0.0.1:5173'
Write-Host '  API         http://127.0.0.1:5080'
Write-Host '  MCP server  http://127.0.0.1:5081  (loopback only)'
Write-Host '  Store       http://127.0.0.1:5082  (loopback only)'
Write-Host ''
Write-Host "  Logs in $logDirectory"
Write-Host '  Stop with: ./scripts/run-dev-clean.ps1 -Stop'
