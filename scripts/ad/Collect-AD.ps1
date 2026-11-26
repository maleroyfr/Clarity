#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-ad-{0}.zip" -f [guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.AD.psm1') -Force
Invoke-AdDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath
