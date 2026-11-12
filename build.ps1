param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe not found (need .NET Framework 4.x)' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$references = @('System.dll', 'System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Runtime.Serialization.dll', 'Microsoft.CSharp.dll')
$frameworkDir = Split-Path $csc -Parent

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/codepage:65001',
    ('/out:' + (Join-Path $OutDir 'AudioEndpointSvc.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),
    ('/lib:' + $frameworkDir)
)
foreach ($reference in $references) {
    $arguments += ('/reference:' + $reference)
}
$arguments += $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

Write-Host ('Built ' + (Join-Path $OutDir 'AudioEndpointSvc.exe'))
