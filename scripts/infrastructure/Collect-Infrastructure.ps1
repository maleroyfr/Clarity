#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-infrastructure-{0}.zip" -f [guid]::NewGuid())))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Infrastructure.psm1') -Force
Invoke-InfrastructureDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
