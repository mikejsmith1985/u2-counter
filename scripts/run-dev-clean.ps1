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
    Accepted and ignored. There was a SQL Server container here and there is not
    one now -- the audit trail is a SQLite file. Kept so that a habit, a note or
    a script passing it does not fail.

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

# The ports this stack occupies, named so a message about one reads as English.
# In one place because they are checked before a run and swept after it, and two
# lists that must agree are one list waiting to disagree.
#
# A list of pairs rather than a hashtable keyed by number, because an ordered
# dictionary indexed with an integer returns the element at that POSITION, not
# the one with that key -- so $ports[5081] asked for the five-thousandth entry,
# found nothing, and every message read "Stopping  on port 5081".
$developmentPorts = @(
    @{ Port = 5081; Name = 'the MCP server' }
    @{ Port = 5080; Name = 'the API' }
    @{ Port = 5173; Name = 'the front end' }
)
$logDirectory = Join-Path $runDirectory 'logs'


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

function Test-ProcessRunsFromRepository {
    <#
        .SYNOPSIS
        Whether a process was launched out of this repository.

        .DESCRIPTION
        Identity by location, which is the only claim available that stays true.
        A process id is reused, a name is shared by every Node and Python on the
        machine, and a start time only says whether something is older than this
        script -- not whose it is.

        Two roots count, because the stack spans two checkouts: this repository
        and the fork the MCP server is installed from. Anything running out of
        either was started by this script.

        Two places carry the answer, and neither alone is enough. The executable
        path settles the API and the MCP server, which run from the build output
        and from the fork's virtual environment. It does not settle the front end:
        Vite runs whichever Node is on PATH, so its executable sits in Program
        Files whoever started it -- but the repository is there in its arguments.

        Anything unreadable is treated as not ours. Refusing to stop a process we
        cannot identify is the mistake that costs nothing.
    #>
    param([Parameter(Mandatory)] $Process)

    $roots = @($repositoryRoot, $forkRoot) |
        Where-Object { $_ } |
        ForEach-Object { (Resolve-Path $_ -ErrorAction SilentlyContinue)?.Path } |
        Where-Object { $_ } |
        ForEach-Object { $_.TrimEnd('\') }

    if ($roots.Count -eq 0) { return $false }

    try {
        $executable = $Process.Path
    }
    catch {
        # Access is denied for processes owned by another user. Not ours, then.
        return $false
    }

    $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($Process.Id)" -ErrorAction SilentlyContinue).CommandLine

    foreach ($root in $roots) {
        if ($executable -and $executable.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }

        if ($commandLine -and $commandLine.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Assert-PortIsFree {
    <#
    .SYNOPSIS
        Refuse to start when a port this script needs is already taken.
    .DESCRIPTION
        Because the alternative is worse than failing. Vite falls back to the
        next free port when its own is busy, so on a machine already running
        another project this script printed one address and the application
        answered on another -- and the person following the printed address got
        somebody else's app.

        Failing here says which port and what holds it, which is a thing somebody
        can act on in seconds.
    #>
    param([int] $Port, [string] $Name)

    $owner = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty OwningProcess

    if (-not $owner) {
        return
    }

    $process = Get-Process -Id $owner -ErrorAction SilentlyContinue
    $description = if ($process) { "$($process.ProcessName), pid $owner" } else { "pid $owner" }

    throw @"
Port $Port is already in use by $description, and $Name needs it.

Stop that process, or free the port, and run this again. It is not stopped
for you: it was running before this script, so it is not this script's to stop.
"@
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
    param(
        [int] $Port,
        [string] $Name
    )

    $owners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    foreach ($processId in $owners) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if (-not $process) {
            continue
        }

        # Ours only if it runs out of this repository.
        #
        # This guard exists because the comment above it used to be wrong. It said
        # the port was ours because this script assigned it, and that holds only
        # while the port was free. Vite falls back to the next port when its own is
        # taken, so on a machine already running another project this script would
        # report 5173, bind 5174, and then stop whatever else was listening on
        # 5173. It did exactly that, to an unrelated dev server.
        #
        # The first repair asked when the process started and spared anything older
        # than this session. That was the wrong question. It spared other people's
        # servers, which was the point, but it also spared OUR OWN from an earlier
        # session -- and those hold the assemblies the build writes, so a deploy
        # then failed on locked files and reported it as a failing test suite.
        #
        # Where a process runs from answers both at once and does not decay. A
        # server started from this repository is ours whenever it started; one
        # started from anywhere else is not ours however recently.
        if (-not (Test-ProcessRunsFromRepository -Process $process)) {
            Write-Step "Leaving $($process.ProcessName) (pid $processId) on port $Port - it is not ours"
            continue
        }

        Write-Step "Stopping $Name on port $Port ($($process.ProcessName), pid $processId)"
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
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
        # may still be held by something it started. Only something that started
        # after we did: anything older was already listening, and is not ours to
        # stop however inconvenient its port is.
        if ($record.Port) {
            Stop-PortHolder -Port ([int] $record.Port) -Name $record.Name
        }
    }

    # And then the ports themselves, recorded or not.
    #
    # The record is not a complete account of what is running. It is written when
    # a service starts and deleted when one stops, so a session that ended badly
    # -- or a machine that restarted the terminal -- leaves services running with
    # nothing tracking them. This ran into exactly that: three orphans held all
    # three ports, "nothing recorded as running" was printed, and the next build
    # failed on assemblies the orphaned API still had open. The error it produced
    # said the test suites had failed, which was untrue and sent the reader
    # looking for a broken test that did not exist.
    #
    # Safe to do unconditionally now only because the test is where a process
    # runs from rather than when it started. Sweeping a port on any weaker basis
    # is how this script once stopped an unrelated project's dev server.
    foreach ($service in $developmentPorts) {
        Stop-PortHolder -Port $service.Port -Name $service.Name
    }

    if (Test-Path $pidFilePath) { Remove-Item $pidFilePath -Force }

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

# The audit trail is a SQLite file, so there is no database server to start.
#
# There was one here: a SQL Server container, started every run, that nothing
# connected to -- the connection string was never set, so the application ran in
# memory while a database sat beside it holding nothing. It was left behind when
# the audit trail moved to SQLite, and removing it is what makes -SkipSql
# unnecessary rather than merely optional.
$auditDatabase = Join-Path $runDirectory 'counter.db'

# The store is not a service of its own either. It is a driver the MCP server
# loads in process, presenting the same objects uopy does, so the server runs the
# code it would run against a real Universe rather than a second path written for
# the demonstration.
# Checked before anything starts, so a busy port is reported rather than worked
# around. A dev server that quietly moves to another port makes every printed
# address wrong.
foreach ($service in $developmentPorts) {
    Assert-PortIsFree -Port $service.Port -Name $service.Name
}

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
    -Environment @{
        'DOTNET_ROOT'                = $dotnetRoot
        # Set here because it was set nowhere, which meant local development
        # never exercised the durable path at all -- and the one place a
        # durability bug would have shown up was the one place it could not.
        'ConnectionStrings__Counter' = "Data Source=$auditDatabase"
    } `
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
Write-Host "  Audit trail $auditDatabase" 
Write-Host ''
Write-Host "  Logs in $logDirectory"
Write-Host '  Stop with: ./scripts/run-dev-clean.ps1 -Stop'
