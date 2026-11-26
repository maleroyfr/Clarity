#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-office-gpo-{0}.zip" -f [guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.OfficeGpo.psm1') -Force
Invoke-OfficeGpoDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
