#requires -Version 5.1
<#
    Build DSH Remote (WinForms, .NET Framework 4.8) with the in-box C# compiler.
    No Visual Studio / dotnet SDK required.

    Output: ..\DSH Remote.exe  (or -OutFile <path>)

    If the target exe is currently running it cannot be replaced; the build then
    lands next to it as "DSH Remote.new.exe" and says so.
#>
[CmdletBinding()]
param(
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw 'csc.exe not found. .NET Framework 4.x is required.'
}

$here = $PSScriptRoot
$root = Split-Path -Parent $here

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $out = Join-Path $root 'DSH Remote.exe'
}
elseif ([System.IO.Path]::IsPathRooted($OutFile)) {
    $out = $OutFile
}
else {
    $out = Join-Path $root $OutFile
}
$out = [System.IO.Path]::GetFullPath($out)
$outDir = Split-Path -Parent $out
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$manifest = Join-Path $here 'app.manifest'
$outTemp = Join-Path $outDir 'DSHRemote.build.exe'

$sources = @(Get-ChildItem -LiteralPath $here -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No .cs files found.' }

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll'
)

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/win32manifest:' + $manifest),
    ('/out:' + $outTemp)
) + $refs + $sources

$compilerOutput = & $csc $cscArgs 2>&1
if ($compilerOutput) { $compilerOutput | Write-Host }
if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $outTemp) { Remove-Item -LiteralPath $outTemp -Force -ErrorAction SilentlyContinue }
    throw "csc failed with exit code $LASTEXITCODE"
}

$final = $out
$replaceable = $true

if (Test-Path -LiteralPath $final) {
    try {
        Remove-Item -LiteralPath $final -Force -ErrorAction Stop
    }
    catch {
        $replaceable = $false
    }
}

if (-not $replaceable) {
    $altName = [System.IO.Path]::GetFileNameWithoutExtension($final) + '.new.exe'
    $alt = Join-Path $outDir $altName
    if (Test-Path -LiteralPath $alt) { Remove-Item -LiteralPath $alt -Force }
    Move-Item -LiteralPath $outTemp -Destination $alt -Force

    $item = Get-Item -LiteralPath $alt
    Write-Host ("Built: {0} ({1:N0} bytes)" -f $item.FullName, $item.Length) -ForegroundColor Green
    Write-Host ''
    Write-Host 'DSH Remote is running, so the old exe could not be replaced.' -ForegroundColor Yellow
    Write-Host 'Close the DSH Remote window (the host keeps running), then run this script again' -ForegroundColor Yellow
    Write-Host 'to get ..\DSH Remote.exe.' -ForegroundColor Yellow
    return
}

Move-Item -LiteralPath $outTemp -Destination $final -Force

$item = Get-Item -LiteralPath $final
Write-Host ("Built: {0} ({1:N0} bytes)" -f $item.FullName, $item.Length) -ForegroundColor Green
