#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-exchange-{0}.zip" -f [guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Exchange.psm1') -Force
Invoke-ExchangeDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
