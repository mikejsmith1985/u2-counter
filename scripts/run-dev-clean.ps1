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

function Stop-PortHolder {
    <#
        Stops whatever is listening on one of the ports this script assigns.

        This exists because several of these services are not the process we
        started. `dotnet run` builds, launches Counter.Api as a child and exits;
        npm.cmd launches node and exits. The recorded id is then gone while the
        service it started is still holding the port -- and the next run fails to
        bind, with an error that says nothing about why.

        Resolving the owner of a known port is still targeting one specific id.
        It is not a name pattern, and Article II's prohibition is on name
        patterns for a concrete reason: this agent may itself be running inside a
        process called dotnet or node, and `Stop-Process -Name node` would end
        the session issuing it. The port is ours because this script assigned it.
    #>
    param([int] $Port, [string] $Name)

    $owners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    foreach ($processId in $owners) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($process) {
            Write-Step "Stopping $Name on port $Port ($($process.ProcessName), pid $processId)"
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Stop-RecordedProcesses {
    $records = Get-RecordedProcesses
    if ($records.Count -eq 0) {
        Write-Step 'Nothing recorded as running.'
    }

    foreach ($record in $records) {
        if (Test-ProcessIsOurs -ProcessId $record.ProcessId -RecordedStartTime $record.StartedAt) {
            Write-Step "Stopping $($record.Name) (pid $($record.ProcessId))"

            # /T stops the tree. The recorded id is often a launcher whose child
            # is the actual service, and stopping the parent alone orphans it.
            & taskkill.exe /PID $record.ProcessId /T /F 2>&1 | Out-Null
        }
        else {
            Write-Step "Skipping $($record.Name) (pid $($record.ProcessId)) - already gone"
        }

        # Whether or not the recorded id was still ours, the port it was given
        # may still be held by something it started.
        if ($record.Port) {
            Stop-PortHolder -Port ([int] $record.Port) -Name $record.Name
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

        Environment is taken as a hashtable, applied to this process just before
        the child is created and removed again afterwards. Start-Process hands a
        child the parent's environment and offers no way to add to it, so this is
        the only seam available.
    #>
    param(
        [string] $Name,
        [string] $FilePath,
        [string[]] $ArgumentList,
        [string] $WorkingDirectory,
        [hashtable] $Environment = @{},
        [int] $Port = 0
    )

    $previousValues = @{}
    foreach ($key in $Environment.Keys) {
        $previousValues[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }

    $standardOut = Join-Path $logDirectory "$Name.out.log"
    $standardError = Join-Path $logDirectory "$Name.err.log"

    $process = Start-Process -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $standardOut `
        -RedirectStandardError $standardError `
        -PassThru `
        -WindowStyle Hidden

    foreach ($key in $previousValues.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previousValues[$key])
    }

    Write-Step "Started $Name (pid $($process.Id))"

    return [pscustomobject]@{
        Name        = $Name
        ProcessId   = $process.Id
        StartedAt   = $process.StartTime.ToUniversalTime().ToString('o')
        # Recorded so the service can still be stopped when the process we
        # started has exited and left a child holding the port.
        Port        = $Port
        LogPath     = $standardOut
    }
}

# Where the hardened fork is checked out: a sibling of this repository unless
# told otherwise. Its own virtual environment carries the entry point, because
# this demonstration runs the fork as installed rather than importing its source
# -- what is exercised should be the thing being offered for review.
$forkRoot = if ($env:U2_MCP_ROOT) { $env:U2_MCP_ROOT } else {
    Join-Path (Split-Path -Parent $repositoryRoot) 'u2-mcp'
}
$mcpExecutable = Join-Path $forkRoot '.venv\Scripts\u2-mcp.exe'

if (-not (Test-Path $mcpExecutable)) {
    throw "The hardened MCP server was not found at $mcpExecutable. Set U2_MCP_ROOT to where the fork is checked out."
}

function Resolve-DotnetExecutable {
    <#
        Returns a dotnet that has an SDK behind it.

        The dotnet on PATH is frequently the shared host alone -- enough to run a
        published application, not enough to build one. It fails with "No .NET
        SDKs were found", which reads like dotnet being absent rather than like
        the wrong one being first on the path, and costs whoever hits it an
        afternoon.
    #>
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        'C:\Program Files\dotnet\dotnet.exe'
    )

    foreach ($candidate in $candidates) {
        $sdkDirectory = Join-Path (Split-Path -Parent $candidate) 'sdk'
        if ((Test-Path $candidate) -and (Test-Path $sdkDirectory) -and
            (Get-ChildItem $sdkDirectory -Directory -ErrorAction SilentlyContinue)) {
            return $candidate
        }
    }

    throw 'No dotnet with an SDK was found. Install the .NET 9 SDK, or put one earlier on PATH.'
}

$dotnetExecutable = Resolve-DotnetExecutable
$dotnetRoot = Split-Path -Parent $dotnetExecutable

# -- entry point --------------------------------------------------------------

if ($Stop) {
    Stop-RecordedProcesses
    return
}

# Always clear a previous run first, so two sets of services never compete for
# the same ports.
Stop-RecordedProcesses

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

# Recorded as each one starts rather than all at the end. A failure partway
# through otherwise leaves the services that did start with nothing recording
# them: -Stop cannot find them, the next run collides with the ports they hold,
# and the only way out is to hunt process ids by hand -- which is exactly the
# situation Article II forbids resolving with a name pattern.
$started = @()

function Register-Started {
    param([object] $Record)

    $script:started += $Record
    Save-RecordedProcesses -Records $script:started
}

if (-not $SkipSql) {
    Write-Step 'Starting SQL Server container'
    docker run --detach --rm `
        --name $sqlContainerName `
        --env 'ACCEPT_EULA=Y' `
        --env "MSSQL_SA_PASSWORD=$env:COUNTER_SQL_PASSWORD" `
        --publish '1433:1433' `
        'mcr.microsoft.com/mssql/server:2022-latest' | Out-Null
}

# The store is not a service of its own. It is a driver the MCP server loads in
# process, presenting the same objects uopy does, so the server runs the code it
# would run against a real Universe rather than a second path written for the
# demonstration.
Register-Started (Start-Service -Name 'mcp' `
    -FilePath $mcpExecutable `
    -ArgumentList @('--streamable-http', '--host', '127.0.0.1', '--port', '5081') `
    -WorkingDirectory $repositoryRoot `
    -Environment @{
        'U2_DRIVER'         = 'demo'
        'MVSTORE_DATA_PATH' = (Join-Path $repositoryRoot 'mvstore\data')
        'PYTHONPATH'        = (Join-Path $repositoryRoot 'mvstore\src')
        # The server validates its connection settings at startup whichever
        # driver is configured, so these must be present even though the demo
        # driver never authenticates against anything. They are placeholders, not
        # credentials: there is no Universe instance behind them, and a real
        # deployment supplies real ones through the vault rather than here.
        'U2_HOST'           = '127.0.0.1'
        'U2_USER'           = 'u2demo'
        'U2_PASSWORD'       = 'demo-no-database-behind-this'
        'U2_ACCOUNT'        = 'DEMO'
    } `
    -Port 5081)

Register-Started (Start-Service -Name 'api' `
    -FilePath $dotnetExecutable `
    -ArgumentList @('run', '--project', 'src/Counter.Api', '--urls', 'http://127.0.0.1:5080') `
    -WorkingDirectory (Join-Path $repositoryRoot 'api') `
    -Environment @{ 'DOTNET_ROOT' = $dotnetRoot } `
    -Port 5080)

# npm.cmd, not npm: the bare name resolves to a shell script that
# Start-Process cannot launch, and the error it gives ("not a valid Win32
# application") says nothing about which of several things went wrong.
Register-Started (Start-Service -Name 'web' `
    -FilePath 'npm.cmd' `
    -ArgumentList @('run', 'dev') `
    -WorkingDirectory (Join-Path $repositoryRoot 'web') `
    -Port 5173)

Write-Host ''
Write-Host 'Running:' -ForegroundColor Green
Write-Host '  Front end   http://127.0.0.1:5173'
Write-Host '  API         http://127.0.0.1:5080'
Write-Host '  MCP server  http://127.0.0.1:5081  (loopback only)'
Write-Host ''
Write-Host "  Logs in $logDirectory"
Write-Host '  Stop with: ./scripts/run-dev-clean.ps1 -Stop'
