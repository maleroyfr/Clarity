Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

function Get-SharePointValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function ConvertTo-SharePointUtc($Value) {
    if ($null -eq $Value) { return $null }
    $date = [datetime]$Value
    # CSOM serializes UTC; an unspecified kind is kept as UTC rather than shifted by the workstation time zone.
    if ($date.Kind -eq [DateTimeKind]::Unspecified) { $date = [datetime]::SpecifyKind($date, [DateTimeKind]::Utc) }
    return $date.ToUniversalTime().ToString('o')
}

function ConvertTo-SharePointText($Value) {
    if ($null -eq $Value) { return $null }
    return [string]$Value
}

function Add-SharePointCount([hashtable]$Counts, [string]$Code) {
    if ($Counts.ContainsKey($Code)) { $Counts[$Code]++ } else { $Counts[$Code] = 1 }
}

function Get-SharePointPrincipal($Member, [string]$SiteId) {
    $login = [string](Get-SharePointValue $Member 'LoginName')
    if ([string](Get-SharePointValue $Member 'PrincipalType') -eq 'SharePointGroup') {
        $id = Get-SharePointValue $Member 'Id'
        if ($null -eq $id) { return $null }
        return @{ type = 'sharepointGroup'; namespace = 'sharepoint.siteGroup'; externalId = "$SiteId/$([int]$id)" }
    }
    if ($login -match '^i:0#\.f\|membership\|(?<upn>[^|@\s]+@[^|@\s.][^|@\s]*\.[^|@\s.]+)$' -and $Matches.upn.Length -le 500) {
        return @{ type = 'user'; namespace = 'upn'; externalId = $Matches.upn }
    }
    # Entra security groups and Microsoft 365 group members only; the "_o" owners claim is not the group itself.
    if ($login -match '^(c:0t\.c\|tenant|c:0o\.c\|federateddirectoryclaimprovider)\|(?<id>[0-9a-fA-F-]{36})$') {
        $guid = [guid]::Empty
        if ([guid]::TryParse($Matches.id, [ref]$guid) -and $guid -ne [guid]::Empty) {
            return @{ type = 'group'; namespace = 'microsoft.graph'; externalId = $guid.ToString() }
        }
    }
    return $null
}

function Write-SharePointSiteDetails($Package, $Context, [hashtable]$Counts) {
    $web = Get-PnPWeb -Includes Title, WebTemplate, Configuration, Language, LastItemUserModifiedDate -ErrorAction Stop
    $template = Get-SharePointValue $web 'WebTemplate'
    $language = Get-SharePointValue $web 'Language'
    $attributes = @{
        title = ConvertTo-SharePointText (Get-SharePointValue $web 'Title')
        url = $Context.Url
        webTemplate = if ($null -eq $template) { $null } else { '{0}#{1}' -f $template, (Get-SharePointValue $web 'Configuration') }
        language = if ($null -eq $language) { $null } else { [long]$language }
        lastItemUserModifiedDate = ConvertTo-SharePointUtc (Get-SharePointValue $web 'LastItemUserModifiedDate')
    }
    try {
        $storage = Get-SharePointValue (Get-SharePointValue (Get-PnPSite -Includes Usage -ErrorAction Stop) 'Usage') 'Storage'
        if ($null -eq $storage) { throw 'Usage was not returned.' }
        $attributes.storageUsedBytes = [long]$storage
    } catch { Add-SharePointCount $Counts 'SiteUsageUnavailable' }
    Write-DiscoveryObject -Package $Package -Type site -Namespace 'sharepoint.siteId' -ExternalId $Context.SiteId `
        -Attributes $attributes -AlternativeKeys @(@{ type = 'site'; namespace = 'url'; externalId = $Context.Url })
}

function Write-SharePointLibraries($Package, $Context, [hashtable]$Counts) {
    $site = @{ type = 'site'; namespace = 'sharepoint.siteId'; externalId = $Context.SiteId }
    $root = 'https://' + ([Uri]$Context.Url).Host
    foreach ($list in @(Get-PnPList -Includes RootFolder, HasUniqueRoleAssignments -ErrorAction Stop)) {
        if ([int](Get-SharePointValue $list 'BaseTemplate') -ne 101 -or [bool](Get-SharePointValue $list 'Hidden')) { continue }
        $id = [guid]::Empty
        if (-not [guid]::TryParse([string](Get-SharePointValue $list 'Id'), [ref]$id) -or $id -eq [guid]::Empty) {
            Add-SharePointCount $Counts 'LibraryIdentifierUnavailable'
            continue
        }
        $folder = [string](Get-SharePointValue (Get-SharePointValue $list 'RootFolder') 'ServerRelativeUrl')
        $attributes = @{
            title = ConvertTo-SharePointText (Get-SharePointValue $list 'Title')
            url = if ($folder.StartsWith('/')) { $root + $folder } else { $null }
            created = ConvertTo-SharePointUtc (Get-SharePointValue $list 'Created')
            lastItemUserModifiedDate = ConvertTo-SharePointUtc (Get-SharePointValue $list 'LastItemUserModifiedDate')
        }
        foreach ($name in @('ItemCount', 'EnableVersioning', 'HasUniqueRoleAssignments')) {
            $value = Get-SharePointValue $list $name
            $field = $name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)
            if ($null -eq $value) { Add-SharePointCount $Counts 'LibraryAttributesMissing' }
            elseif ($name -eq 'ItemCount') { $attributes[$field] = [long]$value }
            else { $attributes[$field] = [bool]$value }
        }
        $key = @{ type = 'documentLibrary'; namespace = 'sharepoint.listId'; externalId = $id.ToString() }
        Write-DiscoveryObject -Package $Package -Type $key.type -Namespace $key.namespace -ExternalId $key.externalId -Attributes $attributes
        Write-DiscoveryRelationship -Package $Package -RelationType partOf -From $key -To $site
    }
}

function Write-SharePointPermissions($Package, $Context, [hashtable]$Counts) {
    $site = @{ type = 'site'; namespace = 'sharepoint.siteId'; externalId = $Context.SiteId }
    $principals = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $memberships = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $assignments = @(Get-PnPProperty -ClientObject (Get-PnPWeb -ErrorAction Stop) -Property RoleAssignments -ErrorAction Stop)
    foreach ($assignment in $assignments) {
        [void](Get-PnPProperty -ClientObject $assignment -Property Member, RoleDefinitionBindings -ErrorAction Stop)
        $member = Get-SharePointValue $assignment 'Member'
        $principal = Get-SharePointPrincipal $member $Context.SiteId
        if ($null -eq $principal) { Add-SharePointCount $Counts 'PrincipalUnresolved'; continue }
        if (-not $principals.Add("$($principal.type)|$($principal.namespace)|$($principal.externalId)")) { continue }
        $roles = [string[]]@(@(Get-SharePointValue $assignment 'RoleDefinitionBindings') |
            ForEach-Object { [string](Get-SharePointValue $_ 'Name') } | Where-Object { $_ } | Sort-Object -Unique)
        if ($principal.type -eq 'sharepointGroup') {
            Write-DiscoveryObject -Package $Package -Type $principal.type -Namespace $principal.namespace -ExternalId $principal.externalId `
                -Attributes @{ title = ConvertTo-SharePointText (Get-SharePointValue $member 'Title') }
            foreach ($groupMember in @(Get-PnPGroupMember -Group ([int](Get-SharePointValue $member 'Id')) -ErrorAction Stop)) {
                $from = Get-SharePointPrincipal $groupMember $Context.SiteId
                if ($null -eq $from -or $from.type -eq 'sharepointGroup') { Add-SharePointCount $Counts 'PrincipalUnresolved'; continue }
                if (-not $memberships.Add("$($from.type)|$($from.namespace)|$($from.externalId)|$($principal.externalId)")) { continue }
                Write-DiscoveryRelationship -Package $Package -RelationType memberOf -From $from -To $principal
            }
        }
        Write-DiscoveryRelationship -Package $Package -RelationType sitePermission -From $principal -To $site -Properties @{ roles = $roles }
    }
}

function Write-SharePointSharing($Package, $Context, [hashtable]$Counts) {
    $tenantSite = Get-PnPTenantSite -Identity $Context.Url -ErrorAction Stop
    if ((ConvertTo-DiscoverySharePointUrl ([string](Get-SharePointValue $tenantSite 'Url'))) -ine $Context.Url) {
        throw 'The tenant site settings do not match the targeted site.'
    }
    $attributes = @{}
    foreach ($name in @('SharingCapability', 'SharingDomainRestrictionMode', 'DefaultSharingLinkType', 'DefaultLinkPermission', 'LockState')) {
        $attributes[$name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)] = ConvertTo-SharePointText (Get-SharePointValue $tenantSite $name)
    }
    Write-DiscoveryObject -Package $Package -Type site -Namespace 'sharepoint.siteId' -ExternalId $Context.SiteId `
        -Attributes $attributes -AlternativeKeys @(@{ type = 'site'; namespace = 'url'; externalId = $Context.Url })
}

$script:SharePointWarnings = @{
    SiteUsageUnavailable = 'site storage usage was not readable with this account and was left unobserved.'
    LibraryIdentifierUnavailable = 'libraries had no readable identifier and were not published.'
    LibraryAttributesMissing = 'library values were not returned and were left unobserved.'
    PrincipalUnresolved = 'permission principals or group members were not Entra users, Entra groups or SharePoint groups and were not published.'
}

function Invoke-SharePointDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath, [Parameter(Mandatory)][string]$ClientId)
    Assert-DiscoveryPrerequisites -MinimumPowerShell '7.4' -Modules @('PnP.PowerShell')
    $request = Read-DiscoveryParameters -Path $ParameterFile
    $requestedUrl = ConvertTo-DiscoverySharePointUrl ([string]$request.scope.value)
    $tenant = [guid]::Empty
    if ($request.environmentKind -ne 'M365Tenant' -or $request.scope.kind -ne 'resource' -or $request.producerVersion -ne '1.0.0' -or
        $null -eq $requestedUrl -or $requestedUrl -cne [string]$request.scope.value -or ([Uri]$requestedUrl).Host -match '-(admin|my)\.sharepoint\.com$' -or
        -not [guid]::TryParse([string]$request.expectedEnvironmentExternalId, [ref]$tenant) -or $tenant -eq [guid]::Empty) {
        throw 'Use a SharePoint 1.0.0 request that targets one SharePoint Online site URL of a Microsoft 365 tenant.'
    }
    $client = [guid]::Empty
    if (-not [guid]::TryParse($ClientId, [ref]$client) -or $client -eq [guid]::Empty) {
        throw 'Enter the client ID (GUID) of the Entra app registration your administrator authorized for PnP.PowerShell.'
    }
    $definitions = @{
        'sharepoint.siteDetails' = @{ Writer = 'Write-SharePointSiteDetails'; Fields = @('title', 'url', 'webTemplate', 'language', 'storageUsedBytes', 'lastItemUserModifiedDate'); Relations = @(); ErrorCode = 'PnPReadFailed' }
        'sharepoint.libraries' = @{ Writer = 'Write-SharePointLibraries'; Fields = @('title', 'url', 'itemCount', 'created', 'lastItemUserModifiedDate', 'enableVersioning', 'hasUniqueRoleAssignments'); Relations = @('partOf'); ErrorCode = 'PnPReadFailed' }
        'sharepoint.sitePermissions' = @{ Writer = 'Write-SharePointPermissions'; Fields = @('title'); Relations = @('sitePermission', 'memberOf'); ErrorCode = 'PnPReadFailed' }
        'sharepoint.siteSharing' = @{ Writer = 'Write-SharePointSharing'; Fields = @('sharingCapability', 'sharingDomainRestrictionMode', 'defaultSharingLinkType', 'defaultLinkPermission', 'lockState'); Relations = @(); ErrorCode = 'SharingReadFailed' }
    }
    if (@($request.sections | Where-Object { -not $definitions.ContainsKey($_) }).Count -gt 0 -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) { throw 'Unknown or duplicate SharePoint sections.' }
    Import-Module PnP.PowerShell -ErrorAction Stop
    # Interactive delegated sign-in through the customer-authorized app registration; the client ID is not written anywhere.
    Connect-PnPOnline -Url $requestedUrl -Interactive -ClientId $client.ToString() -ErrorAction Stop
    try {
        $observedTenant = [guid]::Empty
        if (-not [guid]::TryParse([string](Get-PnPTenantId -ErrorAction Stop), [ref]$observedTenant) -or $observedTenant -ne $tenant) {
            throw 'The signed-in SharePoint tenant does not match the request. Select the correct tenant and request.'
        }
        $site = Get-PnPSite -Includes Id, Url -ErrorAction Stop
        $siteUrl = ConvertTo-DiscoverySharePointUrl ([string](Get-SharePointValue $site 'Url'))
        $siteId = [guid]::Empty
        if ($null -eq $siteUrl -or $siteUrl -ine $requestedUrl -or
            -not [guid]::TryParse([string](Get-SharePointValue $site 'Id'), [ref]$siteId) -or $siteId -eq [guid]::Empty) {
            throw 'The requested URL is not the root of a readable site collection. Use the site collection URL of the request.'
        }
        $context = @{ SiteId = $siteId.ToString(); Url = $siteUrl }
        $version = @(Get-Module PnP.PowerShell)[0].Version.ToString()
        $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.sharepoint' -ProducerVersion '1.0.0' -EnvironmentExternalId $tenant.ToString() `
            -ModuleVersions @{ 'PnP.PowerShell' = $version; PowerShell = $PSVersionTable.PSVersion.ToString() } `
            -SourceId $(if ($siteUrl.Length -le 200) { $siteUrl } else { $context.SiteId })
        foreach ($capability in $request.sections) {
            $definition = $definitions[$capability]
            Start-DiscoverySection -Package $package -CapabilityId $capability -Scope $request.scope `
                -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
            $counts = @{}
            try {
                & $definition.Writer $package $context $counts
                $issues = @($counts.Keys | Sort-Object | ForEach-Object { @{ code = $_; message = "$($counts[$_]) $($script:SharePointWarnings[$_])" } })
                $status = if ($issues.Count -gt 0) { 'Partial' } else { 'Complete' }
                Complete-DiscoverySection -Package $package -Status $status -Warnings $issues
            } catch {
                $message = if ($definition.ErrorCode -eq 'SharingReadFailed') {
                    'Sharing settings were not readable. A SharePoint administrator must run this section, then relaunch.'
                } else { 'Read failed. Verify access to the targeted site with this account, then relaunch.' }
                Complete-DiscoverySection -Package $package -Status Failed -Errors @(@{ code = $definition.ErrorCode; message = $message })
            }
        }
        Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
    } finally {
        try { Disconnect-PnPOnline -ErrorAction Stop } catch { Write-Verbose 'No PnP connection to close.' }
    }
}

Export-ModuleMember -Function Invoke-SharePointDiscovery
