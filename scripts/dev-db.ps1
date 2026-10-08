<#
.SYNOPSIS
  Local development Postgres without Docker or an installer.

.DESCRIPTION
  Uses the portable PostgreSQL binaries in $env:USERPROFILE\kiosk-dev-postgres (see note.md).
  Nothing is registered as a Windows service; the server runs only between `start` and `stop`.

.EXAMPLE
  ./scripts/dev-db.ps1 start     # initialises on first run, then starts on port 5433
  ./scripts/dev-db.ps1 stop
  ./scripts/dev-db.ps1 status
  ./scripts/dev-db.ps1 psql      # interactive shell on the kiosk database
#>
param([ValidateSet('start', 'stop', 'status', 'psql')][string]$Command = 'status')

$ErrorActionPreference = 'Stop'
$root = Join-Path $env:USERPROFILE 'kiosk-dev-postgres'
$bin = Join-Path $root 'pgsql\bin'
$data = Join-Path $root 'data'
$log = Join-Path $root 'postgres.log'
# 5433, not 5432: this PC already runs another Postgres on 5432 that is not ours.
$port = 5433

if (-not (Test-Path (Join-Path $bin 'pg_ctl.exe'))) {
    throw "Portable Postgres not found in $bin. See note.md for how it was downloaded."
}

function Initialize-Cluster {
    if (Test-Path (Join-Path $data 'PG_VERSION')) { return }
    $pw = Join-Path $root 'pw.tmp'
    Set-Content -Path $pw -Value 'kiosk' -NoNewline
    try {
        # Dev-only credentials kiosk/kiosk, matching appsettings.Development.json.
        & "$bin\initdb.exe" -D $data -U kiosk --pwfile=$pw -A scram-sha-256 -E UTF8 --locale=C | Out-Null
    }
    finally { Remove-Item $pw -ErrorAction SilentlyContinue }
    # Listen on localhost only.
    Add-Content (Join-Path $data 'postgresql.conf') "`nlisten_addresses = 'localhost'`nport = $port`n"
}

switch ($Command) {
    'start' {
        Initialize-Cluster
        # Own hidden console: Postgres must not inherit this terminal's console or output pipes. Otherwise piping
        # this script's output hangs the caller, and closing the terminal kills Postgres's worker processes.
        $ctl = Start-Process -FilePath "$bin\pg_ctl.exe" -ArgumentList @('-D', "`"$data`"", '-l', "`"$log`"", '-w', 'start') `
            -WindowStyle Hidden -PassThru
        $ctl.WaitForExit() # waits for pg_ctl only, not for the server it launched
        if ($ctl.ExitCode -ne 0) { throw "Postgres did not start; see $log" }
        $env:PGPASSWORD = 'kiosk'
        $exists = & "$bin\psql.exe" -h localhost -p $port -U kiosk -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'kiosk'"
        if ($LASTEXITCODE -ne 0) { throw "Cannot connect to Postgres on port $port" }
        if ($exists -ne '1') { & "$bin\createdb.exe" -h localhost -p $port -U kiosk kiosk }
        Write-Host "Postgres running on localhost:$port (db: kiosk, user: kiosk, password: kiosk)"
    }
    'stop' { & "$bin\pg_ctl.exe" -D $data -m fast stop }
    'status' { & "$bin\pg_ctl.exe" -D $data status }
    'psql' { $env:PGPASSWORD = 'kiosk'; & "$bin\psql.exe" -h localhost -p $port -U kiosk -d kiosk }
}
