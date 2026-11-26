#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-azure-{0}.zip" -f [guid]::NewGuid())))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Azure.psm1') -Force
Invoke-AzureDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
