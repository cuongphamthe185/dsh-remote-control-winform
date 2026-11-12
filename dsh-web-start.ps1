#requires -Version 5.1
<#
    Start the DSH web host so this PC can be controlled from a phone (over Tailscale).

    Run:    start-dsh-web.bat
    Stop:   stop-dsh-web.bat

    This script:
      1. Checks the port and the Tailscale IP.
      2. Starts `dsh web` hidden (the host is independent: closing this window
         does not kill it).
      3. Waits for the token URL and saves it to the runtime folder (default
         %USERPROFILE%\.dsh\remote, outside OneDrive; see -RuntimeDir).
      4. Generates an offline QR image (segno), copies the URL to the clipboard
         and opens the QR image (or sends the link over Taildrop with -Taildrop).
      5. Optional -Taildrop <device>: sends an HTML file with a clickable link
         over Taildrop (e.g. -Taildrop your-phone) instead of opening the QR on
         the PC. If sending fails, it falls back to showing the QR on the PC.

    Every run writes a QR file with a NEW name (timestamped) so image viewers do
    not show a cached old image, and so a second run (host already running) can
    still open the QR.
#>
[CmdletBinding()]
param(
    # Tailnet IP of this PC. 'auto' = read from `tailscale ip -4`.
    [string]$ListenHost = 'auto',
    [int]$Port = 8443,
    # Explicit DSH paths; empty = auto-detect.
    [string]$DshExe = '',
    [string]$DshCli = '',
    # DSH profile used for the web host.
    [string]$Profile = 'web',
    # Optional, when running behind `tailscale serve` (HTTPS + MagicDNS):
    #   -PublicUrl https://machine.tailxxxx.ts.net/ -TrustedHost machine.tailxxxx.ts.net
    [string]$PublicUrl = '',
    [string[]]$TrustedHost = @(),
    [switch]$NoQr,
    [switch]$NoOpenQr,
    # Send the pairing link over Taildrop to this device (e.g. your-phone). Empty = show the QR on the PC.
    [string]$Taildrop = '',
    # Skip the 1-day overlay (for diagnosing startup problems).
    [switch]$NoCookiePatch,
    # Only print the command that would run; do not start the host.
    [switch]$DryRun,
    # Folder for runtime files (QR, link file, URL, logs). Empty = auto:
    # $env:DSH_REMOTE_DIR -> `data\` folder next to this script (portable) -> ~\.dsh\remote.
    [string]$RuntimeDir = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# $exe/$cli are resolved after the functions are declared (see Resolve-DshPaths).
$exe = ''
$cli = ''

function Resolve-RuntimeDir {
    param([string]$Explicit)
    # Order: -RuntimeDir > $env:DSH_REMOTE_DIR > data\ next to the script > ~\.dsh\remote.
    # Keep this in sync with dsh-web-stop.ps1 and AppPaths.cs.
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

function Test-CloudSyncedPath {
    param([string]$Path)
    # Runtime files contain the token: warn when they would land in a cloud-synced folder.
    foreach ($root in @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
        if ($root -and $Path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return ($Path -match '(?i)\\(OneDrive|Dropbox|Google Drive|iCloudDrive)( - [^\\]+)?\\')
}

# Runtime files contain the token -> kept outside OneDrive by default (~\.dsh\remote).
$rt      = Resolve-RuntimeDir -Explicit $RuntimeDir
$remote  = $rt.Path
$stopHint = "click 'Stop DSH' in DSH Remote or run stop-dsh-web.bat"
try { New-Item -ItemType Directory -Force -Path $remote | Out-Null } catch { }
$logFile = Join-Path $remote 'dsh-web.log'
$errFile = Join-Path $remote 'dsh-web.err.log'
$urlFile = Join-Path $remote 'dsh-web.url.txt'
# Caps the web login cookie at 1 day (upstream default is 30).
$patchFile = Join-Path $PSScriptRoot 'connection-1day.patch.yml'

$script:QrError = $null

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

function ConvertTo-ProcessArgument {
    param([string]$Value)
    # Start-Process joins -ArgumentList with spaces WITHOUT quoting (PS 5.1 and 7),
    # so a path such as "C:\Users\John Doe\..." would be split. Quote it here using
    # the standard Windows argv rules.
    if ($Value -eq '') { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Test-LocalAddress {
    param([string]$Address)
    foreach ($nic in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        foreach ($u in $nic.GetIPProperties().UnicastAddresses) {
            if ($u.Address.ToString() -eq $Address) { return $true }
        }
    }
    return $false
}

function New-QrImage {
    param([string]$Url)
    # Returns the image path, or $null on failure (reason in $script:QrError).
    $script:QrError = $null
    $py = $null
    foreach ($cand in @('python', 'py')) {
        $c = Get-Command $cand -ErrorAction SilentlyContinue
        if ($c) { $py = $c.Source; break }
    }
    if (-not $py) { $script:QrError = 'python not found'; return $null }

    # New name every run: avoids image viewers caching the old file content.
    $out = Join-Path $remote ('dsh-web-qr-' + (Get-Date).ToString('yyyyMMdd-HHmmssfff') + '.png')

    # Check the folder is writable first, to report the real error (instead of blaming segno).
    try {
        $probe = Join-Path $remote ('.qr-write-' + [guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($probe, 'x')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    }
    catch {
        $script:QrError = "cannot write to $remote : $($_.Exception.Message)"
        return $null
    }

    # Remove QR images from previous runs (best effort - open files cannot be deleted).
    Get-ChildItem -LiteralPath $remote -File -Filter 'dsh-web-qr*.png' -ErrorAction SilentlyContinue |
        ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { } }

    $code = "import sys, segno; segno.make(sys.argv[1], error='m').save(sys.argv[2], scale=10, border=4)"
    $pyOut = & $py -c $code $Url $out 2>&1
    $rc = $LASTEXITCODE
    if ($rc -ne 0 -or -not (Test-Path -LiteralPath $out)) {
        $detail = $pyOut | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | Select-Object -Last 1
        if ($detail) { $script:QrError = $detail.ToString().Trim() }
        elseif ($rc -ne 0) { $script:QrError = "python/segno failed (exit $rc)" }
        else { $script:QrError = 'PNG file was not created' }
        return $null
    }
    return $out
}

function Show-QrImage {
    param([string]$Path)
    # Returns $true if the image was opened with the default app.
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return $false }
    try { Start-Process -FilePath $Path -ErrorAction Stop; return $true } catch { }
    try { Invoke-Item -LiteralPath $Path -ErrorAction Stop; return $true } catch { }
    return $false
}

function Get-TailscaleExe {
    $cmd = Get-Command tailscale.exe -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $base) { continue }
        $p = Join-Path $base 'Tailscale\tailscale.exe'
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

function Resolve-TailscaleIp {
    $ts = Get-TailscaleExe
    if (-not $ts) { throw 'tailscale.exe not found. Install Tailscale or add it to PATH.' }

    try {
        $ips = & $ts ip -4 2>$null
        foreach ($ip in @($ips)) {
            $ip = "$ip".Trim()
            if ($ip -match '^\d+\.\d+\.\d+\.\d+$') { return $ip }
        }
    }
    catch { }

    try {
        $raw = & $ts status --json 2>$null | Out-String
        if ($raw) {
            $obj = $raw | ConvertFrom-Json
            foreach ($ip in @($obj.Self.TailscaleIPs)) {
                $ip = "$ip".Trim()
                if ($ip -match '^\d+\.\d+\.\d+\.\d+$') { return $ip }
            }
        }
    }
    catch { }

    throw 'Tailscale is not running or has no IPv4 address. Start Tailscale and try again.'
}

function Resolve-DshPaths {
    param([string]$ExeHint = '', [string]$CliHint = '')

    $exeCandidates = New-Object System.Collections.Generic.List[string]
    if ($ExeHint) { $exeCandidates.Add($ExeHint) }
    if ($env:LOCALAPPDATA) {
        $exeCandidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\DeepSeek Harness\DeepSeek Harness.exe'))
    }
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($base) { $exeCandidates.Add((Join-Path $base 'DeepSeek Harness\DeepSeek Harness.exe')) }
    }

    # Registry App Paths (if present).
    foreach ($key in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\DeepSeek Harness.exe',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\DeepSeek Harness.exe'
    )) {
        try {
            $v = Get-ItemPropertyValue -LiteralPath $key -Name '(default)' -ErrorAction Stop
            if ($v) { $exeCandidates.Add((("$v").Trim('"'))) }
        }
        catch { }
    }

    # dsh.cmd on PATH -> derive the install root.
    $dshCmd = Get-Command dsh.cmd -ErrorAction SilentlyContinue
    $pathCli = $null
    if ($dshCmd -and $dshCmd.Source) {
        $pathCli = $dshCmd.Source
        $root = $pathCli
        for ($i = 0; $i -lt 5; $i++) { $root = Split-Path -Parent $root }
        if ($root) { $exeCandidates.Add((Join-Path $root 'DeepSeek Harness.exe')) }
    }

    $exe = ''
    foreach ($c in $exeCandidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { $exe = $c; break }
    }

    $cli = ''
    if ($CliHint -and (Test-Path -LiteralPath $CliHint)) {
        $cli = $CliHint
    }
    elseif ($exe) {
        $candidate = Join-Path (Split-Path -Parent $exe) 'resources\runtime\cli\bin\dsh.cmd'
        if (Test-Path -LiteralPath $candidate) { $cli = $candidate }
    }
    if (-not $cli -and $pathCli -and (Test-Path -LiteralPath $pathCli)) { $cli = $pathCli }

    if (-not $cli) {
        throw 'DSH CLI not found. Install DeepSeek Harness or pass -DshCli <path to dsh.cmd>.'
    }

    return @{ Exe = $exe; Cli = $cli }
}

function New-PairLinkFile {
    param([string]$Url)
    # Keep only the newest link file; older ones hold old tokens and are not needed.
    Get-ChildItem -LiteralPath $remote -File -Filter 'dsh-web-pair-*' -ErrorAction SilentlyContinue |
        ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { } }

    $path = Join-Path $remote ('dsh-web-pair-' + (Get-Date).ToString('yyyyMMdd-HHmmssfff') + '.html')
    $enc = [System.Net.WebUtility]::HtmlEncode($Url)
    $html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>DSH - open session</title>
<style>
  body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 24px; line-height: 1.5; color: #111; }
  h1 { font-size: 20px; margin: 0 0 8px; }
  .btn { display: inline-block; background: #111; color: #fff; text-decoration: none; padding: 14px 22px; border-radius: 10px; font-weight: 600; }
  .hint { color: #555; font-size: 14px; margin-top: 24px; }
  .url { font-family: ui-monospace, Consolas, monospace; font-size: 12px; word-break: break-all; background: #f3f3f3; padding: 10px; border-radius: 8px; }
</style>
</head>
<body>
<h1>DSH on your PC</h1>
<p>Tap the button below to open and pair the session. The link only works while the host is running.</p>
<p><a class="btn" href="$enc">Open DSH</a></p>
<p class="hint">If the button does not work, copy this URL into your browser:</p>
<p class="url">$enc</p>
</body>
</html>
"@
    [IO.File]::WriteAllText($path, $html, (New-Object System.Text.UTF8Encoding -ArgumentList $false))
    return $path
}

function Send-PairLink {
    param([string]$Url, [string]$Device)
    if ([string]::IsNullOrWhiteSpace($Device)) { return $false }
    $ts = Get-TailscaleExe
    if (-not $ts) {
        Write-Host 'Taildrop     : tailscale.exe not found' -ForegroundColor Yellow
        return $false
    }

    $target = $Device.Trim()
    if (-not $target.EndsWith(':')) { $target = $target + ':' }
    $file = New-PairLinkFile -Url $Url

    $out = Join-Path $remote 'taildrop.out.log'
    $err = Join-Path $remote 'taildrop.err.log'
    foreach ($f in @($out, $err)) {
        try { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } } catch { }
    }

    $argLine = 'file cp "{0}" "{1}"' -f $file, $target
    try {
        $proc = Start-Process -FilePath $ts -ArgumentList $argLine -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $proc.WaitForExit(30000)) {
            try { $proc.Kill() } catch { }
            Write-Host 'Taildrop     : still not sent after 30 seconds.' -ForegroundColor Yellow
            return $false
        }
        if ($proc.ExitCode -ne 0) {
            $msg = ''
            if (Test-Path -LiteralPath $err) {
                $m = Get-Content -LiteralPath $err -Raw
                if ($m) { $msg = $m.Trim() }
            }
            if (-not $msg -and (Test-Path -LiteralPath $out)) {
                $m = Get-Content -LiteralPath $out -Raw
                if ($m) { $msg = $m.Trim() }
            }
            Write-Host ("Taildrop     : failed (exit {0}) {1}" -f $proc.ExitCode, $msg) -ForegroundColor Yellow
            return $false
        }
        Write-Host "Taildrop     : link sent to $target" -ForegroundColor Green
        Write-Host "Link file    : $file"
        return $true
    }
    catch {
        Write-Host "Taildrop     : error - $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host '               If this is Access denied: run the .bat from Explorer, not inside a DSH agent session.' -ForegroundColor DarkGray
        return $false
    }
}

function Show-Pairing {
    param([string]$Url, [string]$Device)
    if (-not [string]::IsNullOrWhiteSpace($Device)) {
        if (Send-PairLink -Url $Url -Device $Device) { return }
        Write-Host 'Taildrop failed -> falling back to the QR on the PC.' -ForegroundColor Yellow
    }
    Write-QrStep -Url $Url
}

function Write-QrStep {
    param([string]$Url)
    # Generate + open the QR and print the result. Shared by the first run and re-runs.
    if ($NoQr) { return }
    $img = New-QrImage -Url $Url
    if (-not $img) {
        Write-Host "QR           : NOT created ($($script:QrError))" -ForegroundColor Yellow
        return
    }
    if ($NoOpenQr) {
        Write-Host "QR           : $img  (-NoOpenQr: open this file yourself)"
        return
    }
    if (Show-QrImage -Path $img) {
        Write-Host "QR           : $img  (opened)" -ForegroundColor Green
    }
    else {
        Write-Host "QR           : $img  (could NOT open it automatically - open this file manually)" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '=================================================' -ForegroundColor DarkGray
Write-Host ' DSH WEB - START' -ForegroundColor Cyan
Write-Host '=================================================' -ForegroundColor DarkGray
Write-Host "Runtime dir  : $remote ($($rt.Source))" -ForegroundColor DarkGray
if (Test-CloudSyncedPath -Path $remote) {
    Write-Host 'WARNING: the runtime folder is inside a cloud-synced folder - URL/QR files containing the token will be uploaded.' -ForegroundColor Red
}

$dsh = Resolve-DshPaths -ExeHint $DshExe -CliHint $DshCli
$exe = $dsh.Exe
$cli = $dsh.Cli

if (-not $ListenHost -or $ListenHost -eq 'auto') {
    $ListenHost = Resolve-TailscaleIp
    Write-Host "Tailscale IP : $ListenHost" -ForegroundColor DarkGray
}

if (-not (Test-Path -LiteralPath $cli)) { throw "DSH CLI not found: $cli" }

# --- Already running: print the URL again + reopen the QR, do not start another instance ---
$owner = Get-PortOwner -LocalPort $Port
if ($owner) {
    $p = Get-Process -Id $owner -ErrorAction SilentlyContinue
    Write-Host "Port $Port is already being served (PID $owner, $($p.ProcessName))." -ForegroundColor Yellow

    if (Test-Path -LiteralPath $urlFile) {
        $u = (Get-Content -LiteralPath $urlFile -Raw).Trim()
        if (-not $u) { $u = $null }
    }
    else { $u = $null }

    if (-not $u) {
        Write-Host "Cannot read the saved URL ($urlFile)." -ForegroundColor Yellow
        Write-Host 'The host may have been started by another process, or the URL file was deleted.' -ForegroundColor Yellow
        Write-Host "For a new token + QR: $stopHint, then start again." -ForegroundColor Yellow
        return
    }

    Write-Host "Token URL    : $u" -ForegroundColor White
    Write-Host ("Reopen URL   : " + ($u -split '\?')[0])
    try { Set-Clipboard -Value $u } catch { }
    Write-Host '(token URL copied to the clipboard)'
    Show-Pairing -Url $u -Device $Taildrop
    Write-Host ''
    Write-Host 'The host was already running, so NO new token was created.' -ForegroundColor DarkGray
    Write-Host "For a new token (and a new QR): $stopHint, then start again." -ForegroundColor DarkGray
    return
}

# --- The IP must exist on this machine, otherwise `dsh web` cannot bind ---
if (-not (Test-LocalAddress -Address $ListenHost)) {
    Write-Host "IP $ListenHost was not found on this machine." -ForegroundColor Red
    Write-Host '  - Turn on Tailscale on the PC, sign in with the same account, then run again.'
    Write-Host '  - Or use it on this PC only:  start-dsh-web.bat -ListenHost 127.0.0.1'
    throw "Invalid bind address: $ListenHost"
}

# --- Start the host, detached from this window ---
foreach ($f in @($logFile, $errFile)) {
    try { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } } catch { }
}
if ($exe) { $env:DSH_DESKTOP_NODE_EXECUTABLE = $exe }

$argList = @('--profile', $Profile)
# Actual cookie lifetime, used by the instructions below.
$cookieDays = 30
if ($NoCookiePatch) {
    Write-Host 'Skipping the 1-day overlay (-NoCookiePatch) -> cookie uses the default 30 days.' -ForegroundColor Yellow
}
elseif (Test-Path -LiteralPath $patchFile) {
    $argList += @('--patch', $patchFile)
    $cookieDays = 1
}
else {
    Write-Host "$patchFile not found -> cookie will use the default 30 days." -ForegroundColor Yellow
}
$argList += @('--host', $ListenHost, '--port', "$Port", '--no-open')
if ($PublicUrl) { $argList += @('--public-url', $PublicUrl) }
foreach ($t in $TrustedHost) { $argList += @('--trusted-host', $t) }
$argLine = ($argList | ForEach-Object { ConvertTo-ProcessArgument -Value "$_" }) -join ' '

if ($DryRun) {
    Write-Host ''
    Write-Host 'DRY RUN - command that would run:' -ForegroundColor Yellow
    Write-Host ("  " + $cli)
    Write-Host ("  " + $argLine)
    return
}

$proc = Start-Process -FilePath $cli -ArgumentList $argLine `
    -RedirectStandardOutput $logFile -RedirectStandardError $errFile `
    -WindowStyle Hidden -PassThru

Write-Host "Starting host (PID $($proc.Id)) on ${ListenHost}:$Port ..." -ForegroundColor DarkGray

# --- Wait for the token URL (up to 120s) ---
$url = $null
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path -LiteralPath $logFile) {
        $m = Select-String -LiteralPath $logFile -Pattern 'dsh web:\s+(https?://\S+)' -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($m) { $url = $m.Matches[0].Groups[1].Value; break }
    }
    if ($proc.HasExited) { break }
}

if (-not $url) {
    Write-Host ''
    Write-Host 'Could NOT get the URL - the host failed to start.' -ForegroundColor Red
    Write-Host "Error log: $errFile"
    Get-Content -LiteralPath $errFile -ErrorAction SilentlyContinue | Select-Object -Last 25
    Get-Content -LiteralPath $logFile -ErrorAction SilentlyContinue | Select-Object -Last 25
    exit 1
}

try { Set-Content -LiteralPath $urlFile -Value $url -Encoding UTF8 -NoNewline } catch { }
try { Set-Clipboard -Value $url } catch { }
$plainUrl = ($url -split '\?')[0]

Write-Host ''
Write-Host '============== DSH WEB IS RUNNING ==============' -ForegroundColor Green
Write-Host "Token URL    : $url" -ForegroundColor White
Write-Host "Reopen URL   : $plainUrl   (use after the phone has paired)"
Show-Pairing -Url $url -Device $Taildrop
Write-Host "Log          : $logFile"
Write-Host "Errors       : $errFile"
if ($cookieDays -eq 1) {
    Write-Host 'Cookie       : 1 day (--patch connection-1day.patch.yml)' -ForegroundColor Magenta
}
else {
    Write-Host "Cookie       : $cookieDays days (upstream default, NO 1-day patch)" -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'On the phone:' -ForegroundColor Cyan
Write-Host '  1. Turn on Tailscale and sign in with the same account (tailnet).'
if ($Taildrop) {
    Write-Host "  2. Open the Taildrop notification -> HTML file -> tap 'Open DSH'."
    Write-Host '     (No notification? Open the Tailscale/Files app, Tailscale section.)'
}
else {
    Write-Host '  2. Open Camera / Google Lens and scan the QR image shown on the PC.'
}
Write-Host "  3. The link opens with ?token=... -> the server sets a $cookieDays-day cookie."
Write-Host "  4. Within those $cookieDays day(s), just open the reopen URL (no token/QR needed)."
Write-Host ''
Write-Host 'SECURITY:' -ForegroundColor Yellow
Write-Host '  - The token URL grants control of this PC. Do not screenshot it or paste it into chat.'
Write-Host "  - QR / link / URL files are in $remote (keep it out of cloud-synced folders)."
Write-Host '  - The link file sent over Taildrop also contains the token: delete it on the phone after pairing.'
if ($cookieDays -eq 1) {
    Write-Host '  - The login cookie lives only 1 DAY (upstream default is 30 days).'
}
else {
    Write-Host "  - The login cookie lives $cookieDays DAYS (no 1-day patch)." -ForegroundColor Yellow
}
Write-Host "  - On stop ($stopHint): all QR/link files are deleted and the token is rotated (old cookies become invalid)."
Write-Host '================================================' -ForegroundColor Green
Write-Host ''
Write-Host "Closing this window does NOT stop the server. To stop it: $stopHint." -ForegroundColor DarkGray
