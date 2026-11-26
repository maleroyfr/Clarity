#requires -Version 5.1
#requires -PSEdition Desktop
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [Parameter(Mandatory, HelpMessage = 'SharePoint admin URL, for example https://contoso-admin.sharepoint.com')][string]$AdminUrl,
    [string]$OutputPath = (Join-Path (Get-Location) ("clarity-onedrive-{0}.zip" -f [guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.OneDrive.psm1') -Force
Invoke-OneDriveDiscovery -ParameterFile $ParameterFile -OutputPath $OutputPath -AdminUrl $AdminUrl
