Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

function Get-OneDriveValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Add-OneDriveCount([hashtable]$Counts, [string]$Code) {
    if ($Counts.ContainsKey($Code)) { $Counts[$Code]++ } else { $Counts[$Code] = 1 }
}

function ConvertTo-OneDriveUtc($Value) {
    if ($null -eq $Value) { return $null }
    $date = [datetime]$Value
    # The module returns UTC service values; an unspecified kind is kept as UTC rather than shifted by the workstation time zone.
    if ($date.Kind -eq [DateTimeKind]::Unspecified) { $date = [datetime]::SpecifyKind($date, [DateTimeKind]::Utc) }
    return $date.ToUniversalTime().ToString('o')
}

function Get-OneDriveTenantName([string]$AdminUrl) {
    if ($AdminUrl -cmatch '^https://(?<name>[a-z0-9][a-z0-9-]{0,62})-admin\.sharepoint\.com/?$') { return $Matches.name }
    return $null
}

function Assert-OneDriveTenant([string]$Name, [guid]$Tenant) {
    # SharePoint Online Management Shell exposes no tenant ID; the public OpenID configuration of the tenant's initial
    # onmicrosoft.com name binds the admin URL to the requested tenant before any sign-in.
    $issuer = $null
    try {
        $configuration = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$Name.onmicrosoft.com/v2.0/.well-known/openid-configuration" `
            -UseBasicParsing -ErrorAction Stop
        $issuer = [string](Get-OneDriveValue $configuration 'issuer')
    } catch { $issuer = $null }
    $observed = [guid]::Empty
    if ($issuer -notmatch '^https://login\.microsoftonline\.com/(?<id>[0-9a-fA-F-]{36})/v2\.0$' -or
        -not [guid]::TryParse($Matches.id, [ref]$observed) -or $observed -ne $Tenant) {
        throw 'The SharePoint admin URL does not belong to the requested tenant. Use the admin URL of the requested tenant and relaunch.'
    }
}

function Write-OneDrivePersonalSites($Package, $Context, [hashtable]$Counts) {
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $personalHost = "$($Context.TenantName)-my.sharepoint.com"
    $sites = @(Get-SPOSite -IncludePersonalSite $true -Limit All -Filter "Url -like '-my.sharepoint.com/personal/'" -ErrorAction Stop)
    foreach ($site in $sites) {
        $url = ConvertTo-DiscoverySharePointUrl ([string](Get-OneDriveValue $site 'Url'))
        if ($null -eq $url -or ([Uri]$url).Host -cne $personalHost -or -not ([Uri]$url).AbsolutePath.StartsWith('/personal/', [StringComparison]::OrdinalIgnoreCase) -or
            $url.Length -gt 500) {
            Add-OneDriveCount $Counts 'SiteIdentifierUnavailable'
            continue
        }
        if (-not $seen.Add($url)) { Add-OneDriveCount $Counts 'DuplicateSiteCollection'; continue }
        $attributes = @{ url = $url }
        $owner = [string](Get-OneDriveValue $site 'Owner')
        $ownerKey = $null
        if ([string]::IsNullOrWhiteSpace($owner)) { Add-OneDriveCount $Counts 'OwnerUnresolved' }
        elseif ($owner -notmatch '^[^@\s|]+@[^@\s|]+\.[^@\s|]+$' -or $owner.Length -gt 500) { Add-OneDriveCount $Counts 'OwnerMasked' }
        else {
            $attributes.ownerPrincipalName = $owner
            $ownerKey = @{ type = 'user'; namespace = 'upn'; externalId = $owner }
        }
        $values = @{
            storageUsedMegabytes = Get-OneDriveValue $site 'StorageUsageCurrent'
            storageQuotaMegabytes = Get-OneDriveValue $site 'StorageQuota'
            lastContentModifiedDate = Get-OneDriveValue $site 'LastContentModifiedDate'
            lockState = Get-OneDriveValue $site 'LockState'
            status = Get-OneDriveValue $site 'Status'
        }
        $missing = $false
        foreach ($name in $values.Keys) {
            $value = $values[$name]
            if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) { $missing = $true; continue }
            $attributes[$name] = switch ($name) {
                { $_ -like 'storage*' } { [long]$value; break }
                'lastContentModifiedDate' { ConvertTo-OneDriveUtc $value; break }
                default { [string]$value }
            }
        }
        if ($missing) { Add-OneDriveCount $Counts 'PersonalSiteAttributesMissing' }
        $key = @{ type = 'oneDrive'; namespace = 'sharepoint.siteUrl'; externalId = $url }
        Write-DiscoveryObject -Package $Package -Type $key.type -Namespace $key.namespace -ExternalId $key.externalId -Attributes $attributes `
            -AlternativeKeys @(@{ type = 'oneDrive'; namespace = 'url'; externalId = $url })
        if ($null -ne $ownerKey) { Write-DiscoveryRelationship -Package $Package -RelationType ownerOf -From $ownerKey -To $key }
    }
}

$script:OneDriveWarnings = @{
    SiteIdentifierUnavailable = 'personal sites had no readable personal site URL of this tenant and were not published.'
    DuplicateSiteCollection = 'duplicate personal site URLs were returned; only the first was published.'
    OwnerUnresolved = 'personal sites had no owner; no account link was published.'
    OwnerMasked = 'personal sites had an owner that is not a user principal name (masked or unresolvable); no account link was guessed.'
    PersonalSiteAttributesMissing = 'personal sites had values that were not returned and were left unobserved.'
}

function Invoke-OneDriveDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath, [Parameter(Mandatory)][string]$AdminUrl)
    if ($env:OS -ne 'Windows_NT') { throw 'Run the OneDrive script in Windows PowerShell 5.1 on Windows.' }
    Assert-DiscoveryPrerequisites -MinimumPowerShell '5.1' -Modules @('Microsoft.Online.SharePoint.PowerShell')
    $request = Read-DiscoveryParameters -Path $ParameterFile
    $tenant = [guid]::Empty
    $scopeTenant = [guid]::Empty
    if ($request.environmentKind -ne 'M365Tenant' -or $request.scope.kind -ne 'tenant' -or $request.producerVersion -ne '1.0.0' -or
        -not [guid]::TryParse([string]$request.expectedEnvironmentExternalId, [ref]$tenant) -or $tenant -eq [guid]::Empty -or
        -not [guid]::TryParse([string]$request.scope.value, [ref]$scopeTenant) -or $scopeTenant -ne $tenant) {
        throw 'Use a OneDrive 1.0.0 request whose tenant scope is the requested tenant.'
    }
    $definitions = @{
        'onedrive.personalSites' = @{
            Writer = 'Write-OneDrivePersonalSites'; Relations = @('ownerOf')
            Fields = @('url', 'ownerPrincipalName', 'storageUsedMegabytes', 'storageQuotaMegabytes', 'lastContentModifiedDate', 'lockState', 'status')
        }
    }
    if (@($request.sections | Where-Object { -not $definitions.ContainsKey($_) }).Count -gt 0 -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) { throw 'Unknown or duplicate OneDrive sections.' }
    $name = Get-OneDriveTenantName $AdminUrl.Trim().ToLowerInvariant()
    if ($null -eq $name) { throw 'Enter the SharePoint admin URL in the form https://<tenant>-admin.sharepoint.com.' }
    Assert-OneDriveTenant $name $tenant
    Import-Module Microsoft.Online.SharePoint.PowerShell -DisableNameChecking -ErrorAction Stop
    # Interactive sign-in of a SharePoint administrator; no credential is passed or written anywhere.
    Connect-SPOService -Url "https://$name-admin.sharepoint.com" -ErrorAction Stop
    try {
        $context = @{ TenantName = $name }
        $version = @(Get-Module Microsoft.Online.SharePoint.PowerShell)[0].Version.ToString()
        $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.onedrive' -ProducerVersion '1.0.0' -EnvironmentExternalId $tenant.ToString() `
            -ModuleVersions @{ 'Microsoft.Online.SharePoint.PowerShell' = $version; PowerShell = $PSVersionTable.PSVersion.ToString() } `
            -SourceId "$name-admin.sharepoint.com"
        foreach ($capability in $request.sections) {
            $definition = $definitions[$capability]
            Start-DiscoverySection -Package $package -CapabilityId $capability -Scope $request.scope `
                -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
            $counts = @{}
            try {
                & $definition.Writer $package $context $counts
                $issues = @($counts.Keys | Sort-Object | ForEach-Object { @{ code = $_; message = "$($counts[$_]) $($script:OneDriveWarnings[$_])" } })
                $status = if ($issues.Count -gt 0) { 'Partial' } else { 'Complete' }
                Complete-DiscoverySection -Package $package -Status $status -Warnings $issues
            } catch {
                Complete-DiscoverySection -Package $package -Status Failed -Errors @(@{
                        code = 'SpoReadFailed'; message = 'Personal sites were not readable. A SharePoint administrator must run this script, then relaunch.'
                    })
            }
        }
        Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
    } finally {
        try { Disconnect-SPOService -ErrorAction Stop } catch { Write-Verbose 'No SharePoint Online connection to close.' }
    }
}

Export-ModuleMember -Function Invoke-OneDriveDiscovery
