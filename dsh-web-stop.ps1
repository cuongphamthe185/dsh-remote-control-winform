#requires -Version 5.1
<#
    Stop the DSH web host and clean up login traces.

    Run manually:          stop-dsh-web.bat
    Via Task Scheduler (works from an agent session / from the phone):
                           stop-dsh-web.bat task
    Keep the old login:    stop-dsh-web.bat keep
    Preview only:          stop-dsh-web.bat dry

    By default, every run:
      1. Stops the process LISTENING on the port (and verifies it is gone).
      2. Removes the cookie-signing secret -> every old token/cookie becomes
         invalid and the next host generates a new secret (rotate). The phone
         must scan the QR / open the link again.
      3. Deletes all QR images, Taildrop link files and leftover URL files.

    Why `task` mode exists: child processes spawned by an agent turn live in
    DSH's kill-on-close Win32 Job, so Windows kills them when the turn ends.
    Task Scheduler runs in a separate process tree, so it survives and can
    stop the host.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Action = '',
    [int]$Port = 8443,
    [int]$DelaySeconds = 0,
    [switch]$ViaTask,
    [switch]$RotateToken,
    [switch]$KeepSessions,
    [switch]$DryRun,
    # Folder for runtime files (QR, link file, URL, logs). Empty = auto:
    # $env:DSH_REMOTE_DIR -> `data\` folder next to this script (portable) -> ~\.dsh\remote.
    [string]$RuntimeDir = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

switch ($Action.ToLowerInvariant()) {
    'revoke' { $RotateToken = $true }
    'rotate' { $RotateToken = $true }
    'keep'   { $KeepSessions = $true }
    'task'   { $ViaTask = $true }
    'remote' { $ViaTask = $true }
    'dry'    { $DryRun = $true }
}

function Resolve-RuntimeDir {
    param([string]$Explicit)
    # Order: -RuntimeDir > $env:DSH_REMOTE_DIR > data\ next to the script > ~\.dsh\remote.
    # Keep this in sync with dsh-web-start.ps1 and AppPaths.cs.
    $src = 'default'
    $dir = Join-Path $env:USERPROFILE '.dsh\remote'
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { $dir = $Explicit; $src = '-RuntimeDir' }
    elseif (-not [string]::IsNullOrWhiteSpace($env:DSH_REMOTE_DIR)) { $dir = $env:DSH_REMOTE_DIR; $src = 'DSH_REMOTE_DIR' }
    elseif (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'data') -PathType Container) {
        $dir = Join-Path $PSScriptRoot 'data'; $src = 'portable'
    }
    $dir = [Environment]::ExpandEnvironmentVariables($dir.Trim().Trim('"'))
    if (-not [IO.Path]::IsPathRooted($dir)) { $dir = Join-Path $PSScriptRoot $dir }
    $dir = [IO.Path]::GetFullPath($dir)
    if ($dir.Length -gt 3) { $dir = $dir.TrimEnd('\', '/') }
    # The GUI always passes -RuntimeDir; label it 'default' when it is the default folder.
    if ($dir -ieq [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.dsh\remote'))) { $src = 'default' }
    return @{ Path = $dir; Source = $src }
}

function Get-DshHome {
    # DSH harness home: $env:DSH_HOME if set, otherwise ~\.dsh.
    if (-not [string]::IsNullOrWhiteSpace($env:DSH_HOME)) {
        return [Environment]::ExpandEnvironmentVariables($env:DSH_HOME.Trim().Trim('"'))
    }
    return (Join-Path $env:USERPROFILE '.dsh')
}

$rt      = Resolve-RuntimeDir -Explicit $RuntimeDir
$remote  = $rt.Path
$stopLog = Join-Path $remote 'dsh-web-stop.log'
# The credential file belongs to DSH (not to this tool) -> edit it where DSH reads it.
$creds   = Join-Path (Get-DshHome) '.credentials.yaml'

try { New-Item -ItemType Directory -Force -Path $remote | Out-Null } catch { }

function Note {
    param([string]$Message)
    # Logging is best effort: a logging error must never block stopping the host.
    try {
        "[{0}] {1}" -f (Get-Date -Format 'o'), $Message |
            Add-Content -LiteralPath $stopLog -Encoding UTF8
    } catch { }
}

function Get-PortOwner {
    param([int]$LocalPort)
    foreach ($line in (& netstat.exe -ano 2>$null)) {
        # Match on the foreign address (0.0.0.0:0 / [::]:0), not on the word
        # LISTENING: netstat localizes the state column (e.g. "ABHOEREN" on German Windows).
        if ($line -match ('^\s*TCP\s+\S+:' + $LocalPort + '\s+(?:0\.0\.0\.0:0|\[::\]:0)\s+.+?\s+(\d+)\s*$')) {
            return [int]$Matches[1]
        }
    }
}

if ($ViaTask) {
    # Create a one-off task and run it right away; the task deletes itself at the end.
    $self = $PSCommandPath
    $pwshPath = (Get-Process -Id $PID).Path
    $tr = '"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}" -Port {2} -DelaySeconds 8' -f $pwshPath, $self, $Port
    if ($KeepSessions) { $tr += ' -KeepSessions' }
    # The task runs outside this session and may not see temporary env vars: pass the folder explicitly.
    if ($rt.Source -ne 'default') { $tr += (' -RuntimeDir "{0}"' -f $remote) }
    $when = (Get-Date).AddMinutes(2)
    Write-Host 'Creating scheduled task "DSH-Web-Stop" (runs outside this session''s process tree)...'
    # schtasks needs HH:mm with a colon; some cultures use another time separator.
    $st = $when.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    & schtasks.exe /Create /TN 'DSH-Web-Stop' /SC ONCE /ST $st /RL LIMITED /F /TR $tr | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'schtasks /Create failed' }
    & schtasks.exe /Run /TN 'DSH-Web-Stop' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'schtasks /Run failed' }
    Note "QUEUED via scheduled task (port=$Port, delay=8s)"
    Write-Host 'Stop scheduled in 8 seconds (enough time for the last message to show).' -ForegroundColor Green
    Write-Host "Follow progress: $stopLog"
    return
}

if ($DelaySeconds -gt 0) {
    Write-Host "Waiting $DelaySeconds seconds before stopping..."
    Start-Sleep -Seconds $DelaySeconds
}

Write-Host ''
Write-Host '=================================================' -ForegroundColor DarkGray
if ($KeepSessions) {
    Write-Host ' DSH WEB - STOP + DELETE QR (KEEP LOGIN)' -ForegroundColor Cyan
}
else {
    Write-Host ' DSH WEB - STOP + ROTATE TOKEN + DELETE QR' -ForegroundColor Cyan
}
Write-Host '=================================================' -ForegroundColor DarkGray
Write-Host "Runtime dir : $remote ($($rt.Source))" -ForegroundColor DarkGray
Write-Host "Credentials : $creds" -ForegroundColor DarkGray

# ------------------------------------------------------------ 1. stop the host
$owner = Get-PortOwner -LocalPort $Port

if (-not $owner) {
    Write-Host "No process is LISTENING on port $Port." -ForegroundColor Yellow
    Note "NOOP   no listener on port $Port"
} else {
    $proc = Get-Process -Id $owner -ErrorAction SilentlyContinue
    $name = if ($proc) { $proc.ProcessName } else { '(unknown)' }

    if ($DryRun) {
        Write-Host "DRY RUN: would stop PID $owner ($name) on port $Port." -ForegroundColor Yellow
        Note "DRY    would kill pid=$owner name=$name"
    }
    elseif ($name -notlike 'DeepSeek*') {
        Write-Host "PID $owner ($name) is not DeepSeek Harness - leaving it alone." -ForegroundColor Yellow
        Write-Host ('If you are sure, stop it yourself with: Stop-Process -Id ' + $owner + ' -Force')
        Note "SKIP   pid=$owner name=$name (not DeepSeek)"
    }
    else {
        Write-Host "Stopping PID $owner ($name) ..."
        try {
            Stop-Process -Id $owner -Force -ErrorAction Stop
            Note "KILL   Stop-Process OK pid=$owner"
        }
        catch {
            $out = & taskkill.exe /PID $owner /T /F 2>&1
            Note "KILL   Stop-Process FAILED -> taskkill: $out"
        }
        Start-Sleep -Seconds 2
        $still = Get-PortOwner -LocalPort $Port
        if ($still) {
            Write-Host "WARNING: port $Port is still LISTENING (PID $still)." -ForegroundColor Red
            Note "VERIFY port $Port STILL LISTENING pid=$still"
            exit 1
        }
        Write-Host "Stopped - port $Port is no longer LISTENING (verified)." -ForegroundColor Green
        Note "VERIFY port $Port closed"
    }
}

# ------------------------------------------------ 2. rotate token / revoke cookies
$rotate = -not $KeepSessions
$rotated = $false
if ($rotate) {
    if (-not (Test-Path -LiteralPath $creds)) {
        Write-Host "$creds not found - nothing to rotate." -ForegroundColor DarkGray
    }
    elseif ($DryRun) {
        Write-Host 'DRY RUN: would remove the cookie record (rotate secret), with a temporary backup.' -ForegroundColor Yellow
    }
    else {
        # The backup only exists to restore a failed edit. Delete it on success:
        # keeping it would leave the old secret (and every other DSH credential) on disk.
        $backup = "$creds.bak-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
        Copy-Item -LiteralPath $creds -Destination $backup -Force
        $ok = $false
        try {
            $lines = Get-Content -LiteralPath $creds -Encoding UTF8
            $kept = New-Object System.Collections.Generic.List[string]
            $skipping = $false
            foreach ($line in $lines) {
                if ($line -match '^\s{2}client-connection/browser-session:\s*$') { $skipping = $true; continue }
                if ($skipping -and $line -match '^\s{2}\S') { $skipping = $false }
                if (-not $skipping) { $kept.Add($line) }
            }
            Set-Content -LiteralPath $creds -Value $kept -Encoding UTF8
            $ok = -not (Select-String -LiteralPath $creds -Pattern 'client-connection/browser-session' -Quiet)
        }
        catch {
            Write-Host "Error while editing $creds : $($_.Exception.Message)" -ForegroundColor Red
            $ok = $false
        }

        if (-not $ok) {
            Write-Host 'Could not remove the cookie record - restoring the original file.' -ForegroundColor Red
            Copy-Item -LiteralPath $backup -Destination $creds -Force
            Write-Host "  Backup kept for inspection: $backup" -ForegroundColor Yellow
            Note "ROTATE FAILED, restored from backup=$backup"
            exit 1
        }

        try {
            Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
            $backupNote = 'temporary backup deleted'
        }
        catch {
            $backupNote = "could NOT delete the temporary backup: $backup"
            Write-Host "  $backupNote" -ForegroundColor Yellow
        }
        $rotated = $true
        Write-Host 'Token rotated: the old cookie-signing secret was removed, every old cookie is now invalid.' -ForegroundColor Green
        Write-Host "  ($backupNote)" -ForegroundColor DarkGray
        Note "ROTATE browser-session secret removed ($backupNote)"
    }
} else {
    Write-Host 'Keeping the existing login (-KeepSessions).' -ForegroundColor Yellow
    Note 'ROTATE skipped (KeepSessions)'
}

# ------------------------------------------------- 3. delete QR + URL files
$targets = New-Object System.Collections.Generic.List[string]

# Tool runtime folder: delete every PNG with "qr" in its name.
if (Test-Path -LiteralPath $remote) {
    Get-ChildItem -LiteralPath $remote -File -Filter '*.png' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'qr' } | ForEach-Object { $targets.Add($_.FullName) }
}

# The HTML file sent over Taildrop also contains the token URL -> delete it too.
if (Test-Path -LiteralPath $remote) {
    Get-ChildItem -LiteralPath $remote -File -Filter 'dsh-web-pair-*' -ErrorAction SilentlyContinue |
        ForEach-Object { $targets.Add($_.FullName) }
}

# In the script/app folder: only touch files this tool generates.
# (Never walk up into the workspace, so bundling with the exe stays safe.)
$qrDirs = @($PSScriptRoot)
foreach ($dir in $qrDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    Get-ChildItem -LiteralPath $dir -File -Filter 'dsh-web-qr*.png' -ErrorAction SilentlyContinue |
        ForEach-Object { $targets.Add($_.FullName) }
}

foreach ($extra in @('dsh-web.url.txt', 'dsh-web-qr.png')) {
    $p = Join-Path $remote $extra
    if (Test-Path -LiteralPath $p) { $targets.Add($p) }
}

$unique = $targets | Sort-Object -Unique
if (-not $unique) {
    Write-Host 'No QR / URL files left to delete.' -ForegroundColor DarkGray
}
else {
    foreach ($t in $unique) {
        if ($DryRun) {
            Write-Host "DRY RUN: would delete $t" -ForegroundColor Yellow
        }
        else {
            try {
                Remove-Item -LiteralPath $t -Force -ErrorAction Stop
                Write-Host "Deleted: $t" -ForegroundColor DarkGray
                Note "QR     removed $t"
            }
            catch {
                Write-Host "Could not delete: $t" -ForegroundColor Yellow
                Note "QR     remove FAILED $t : $($_.Exception.Message)"
            }
        }
    }
}

# Clean up the self-created task (when run through the scheduler).
try {
    & schtasks.exe /Query /TN 'DSH-Web-Stop' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { & schtasks.exe /Delete /TN 'DSH-Web-Stop' /F 2>$null | Out-Null }
} catch { }

Write-Host ''
if ($DryRun) {
    Write-Host 'Done (DRY RUN - nothing was changed).' -ForegroundColor DarkGray
}
elseif ($KeepSessions) {
    Write-Host "Done. To start again: click 'Start DSH' in DSH Remote or run start-dsh-web.bat." -ForegroundColor DarkGray
    Write-Host 'The old login was kept: the phone can reopen the URL directly (while the cookie is valid).' -ForegroundColor DarkGray
}
else {
    Write-Host "Done. To start again: click 'Start DSH' in DSH Remote or run start-dsh-web.bat." -ForegroundColor DarkGray
    if ($rotated) {
        Write-Host 'Token rotated: the phone must scan the new QR / open the new pairing link.' -ForegroundColor DarkGray
    }
    else {
        Write-Host 'No token was rotated (no DSH credential file / cookie record found).' -ForegroundColor DarkGray
    }
}
