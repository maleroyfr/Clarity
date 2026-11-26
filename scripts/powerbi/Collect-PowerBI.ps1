#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-powerbi-{0}.zip" -f [guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.PowerBI.psm1') -Force
Invoke-PowerBIDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
