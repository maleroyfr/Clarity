#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-sharepoint-{0}.zip" -f [guid]::NewGuid())),
    [string]$ClientId
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ClientId)) {
    # Asked at launch and kept only in memory; never place it in the parameter file.
    $ClientId = Read-Host -Prompt 'Client ID of the Entra app registration authorized for PnP.PowerShell'
}
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.SharePoint.psm1') -Force
Invoke-SharePointDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath -ClientId $ClientId.Trim()
