BeforeAll {
    $script:stubs = @('Connect-PnPOnline', 'Disconnect-PnPOnline', 'Get-PnPTenantId', 'Get-PnPSite', 'Get-PnPWeb', 'Get-PnPList',
        'Get-PnPProperty', 'Get-PnPGroupMember', 'Get-PnPTenantSite')
    function global:Connect-PnPOnline { [CmdletBinding()]param($Url, [switch]$Interactive, $ClientId) }
    function global:Disconnect-PnPOnline { [CmdletBinding()]param() }
    function global:Get-PnPTenantId { [CmdletBinding()]param() }
    function global:Get-PnPSite { [CmdletBinding()]param($Includes) }
    function global:Get-PnPWeb { [CmdletBinding()]param($Includes) }
    function global:Get-PnPList { [CmdletBinding()]param($Includes) }
    function global:Get-PnPProperty { [CmdletBinding()]param($ClientObject, $Property) }
    function global:Get-PnPGroupMember { [CmdletBinding()]param($Group) }
    function global:Get-PnPTenantSite { [CmdletBinding()]param($Identity) }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.SharePoint.psm1') -Force
    $script:clientId = '99999999-8888-7777-6666-555555555555'
    function script:Read-PackageEntry($Zip, [string]$Name) {
        $reader = [IO.StreamReader]::new($Zip.GetEntry($Name).Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($Name -like '*.jsonl') { return @($text.Trim().Split("`n") | Where-Object { $_ } | ConvertFrom-Json) }
        return $text
    }
}
AfterAll {
    foreach ($name in $script:stubs) { Remove-Item -LiteralPath "Function:\$name" -ErrorAction SilentlyContinue }
}
Describe 'SharePoint PnP.PowerShell collector' {
    BeforeEach {
        Mock Assert-DiscoveryPrerequisites -ModuleName Clarity.Discovery.SharePoint {}
        Mock Import-Module -ModuleName Clarity.Discovery.SharePoint {}
        Mock Get-Module -ModuleName Clarity.Discovery.SharePoint { [pscustomobject]@{ Version = [version]'0.0.1' } }
        Mock Connect-PnPOnline -ModuleName Clarity.Discovery.SharePoint {}
        Mock Disconnect-PnPOnline -ModuleName Clarity.Discovery.SharePoint {}
        Mock Get-PnPTenantId -ModuleName Clarity.Discovery.SharePoint { '11111111-2222-3333-4444-555555555555' }
        Mock Get-PnPSite -ModuleName Clarity.Discovery.SharePoint {
            if ($Includes -contains 'Usage') { return [pscustomobject]@{ Usage = [pscustomobject]@{ Storage = [long]1048576 } } }
            [pscustomobject]@{ Id = [guid]'11111111-1111-1111-1111-111111111111'; Url = 'https://Synthetic.SharePoint.com/sites/alpha/' }
        }
        Mock Get-PnPWeb -ModuleName Clarity.Discovery.SharePoint {
            [pscustomobject]@{
                Title = 'Synthetic Alpha'; WebTemplate = 'GROUP'; Configuration = 0; Language = [uint32]1036
                LastItemUserModifiedDate = [datetime]::SpecifyKind([datetime]'2026-02-03T04:05:06', [DateTimeKind]::Utc)
            }
        }
        Mock Get-PnPList -ModuleName Clarity.Discovery.SharePoint {
            [pscustomobject]@{
                Id = [guid]'21111111-1111-1111-1111-111111111111'; Title = 'Documents'; BaseTemplate = 101; Hidden = $false
                RootFolder = [pscustomobject]@{ ServerRelativeUrl = '/sites/alpha/Shared Documents' }; ItemCount = 42
                Created = [datetime]::SpecifyKind([datetime]'2025-01-02T03:04:05', [DateTimeKind]::Utc)
                LastItemUserModifiedDate = [datetime]::SpecifyKind([datetime]'2026-01-02T03:04:05', [DateTimeKind]::Utc)
                EnableVersioning = $true; HasUniqueRoleAssignments = $false
            }
            [pscustomobject]@{ Id = [guid]'31111111-1111-1111-1111-111111111111'; Title = 'Hidden'; BaseTemplate = 101; Hidden = $true }
            [pscustomobject]@{ Id = [guid]'41111111-1111-1111-1111-111111111111'; Title = 'Tasks'; BaseTemplate = 100; Hidden = $false }
        }
        Mock Get-PnPProperty -ModuleName Clarity.Discovery.SharePoint {
            if ($Property -ne 'RoleAssignments') { return }
            function Assignment($LoginName, $PrincipalType, $Roles, $Id = 0, $Title = 'Synthetic') {
                [pscustomobject]@{
                    Member = [pscustomobject]@{ LoginName = $LoginName; PrincipalType = $PrincipalType; Id = $Id; Title = $Title }
                    RoleDefinitionBindings = @($Roles | ForEach-Object { [pscustomobject]@{ Name = $_ } })
                }
            }
            Assignment 'i:0#.f|membership|owner@synthetic.example' 'User' @('Full Control')
            Assignment 'Alpha Members' 'SharePointGroup' @('Edit', 'Edit') 7 'Alpha Members'
            Assignment 'c:0o.c|federateddirectoryclaimprovider|51111111-1111-1111-1111-111111111111' 'SecurityGroup' @('Read')
            Assignment 'c:0o.c|federateddirectoryclaimprovider|51111111-1111-1111-1111-111111111111_o' 'SecurityGroup' @('Full Control')
            Assignment 'c:0(.s|true' 'SecurityGroup' @('Read')
        }
        Mock Get-PnPGroupMember -ModuleName Clarity.Discovery.SharePoint {
            [pscustomobject]@{ LoginName = 'i:0#.f|membership|member@synthetic.example'; PrincipalType = 'User' }
            [pscustomobject]@{ LoginName = 'c:0t.c|tenant|61111111-1111-1111-1111-111111111111'; PrincipalType = 'SecurityGroup' }
            [pscustomobject]@{ LoginName = 'c:0-.f|rolemanager|spo-grid-all-users/11111111-2222-3333-4444-555555555555'; PrincipalType = 'SecurityGroup' }
        }
        Mock Get-PnPTenantSite -ModuleName Clarity.Discovery.SharePoint { throw 'Synthetic access denied; never expose raw text.' }
    }
    It 'collects targeted site details, libraries and permissions, flags unresolved principals and fails sharing explicitly' {
        $path = Invoke-SharePointDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') `
            -OutputPath (Join-Path $TestDrive 'sharepoint.zip') -ClientId $script:clientId
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $manifest = Read-PackageEntry $zip 'manifest.json'
            $manifest | Should -Not -Match $script:clientId
            ($manifest | ConvertFrom-Json).producerId | Should -Be 'clarity.sharepoint'
            $results = Read-PackageEntry $zip 'results.json' | ConvertFrom-Json
            ($results | Where-Object capabilityId -eq 'sharepoint.siteDetails').status | Should -Be 'Complete'
            ($results | Where-Object capabilityId -eq 'sharepoint.libraries').objectCount | Should -Be 1
            $permissions = $results | Where-Object capabilityId -eq 'sharepoint.sitePermissions'
            $permissions.status | Should -Be 'Partial'
            $permissions.warnings[0].code | Should -Be 'PrincipalUnresolved'
            $permissions.warnings[0].message | Should -BeLike '3 *'
            $permissions.relationshipCount | Should -Be 5
            $sharing = $results | Where-Object capabilityId -eq 'sharepoint.siteSharing'
            ($sharing.status, $sharing.errors[0].code) | Should -Be @('Failed', 'SharingReadFailed')
            $sharing.errors[0].message | Should -Not -BeLike '*Synthetic access denied*'
            $objects = Read-PackageEntry $zip 'objects.jsonl'
            $site = $objects | Where-Object type -eq 'site'
            ($site.namespace, $site.externalId) | Should -Be @('sharepoint.siteId', '11111111-1111-1111-1111-111111111111')
            $site.attributes.url | Should -BeExactly 'https://synthetic.sharepoint.com/sites/alpha'
            $site.attributes.webTemplate | Should -Be 'GROUP#0'
            $site.attributes.storageUsedBytes | Should -Be 1048576
            $site.alternativeKeys[0].namespace | Should -Be 'url'
            ($objects | Where-Object type -eq 'documentLibrary').attributes.url | Should -BeExactly 'https://synthetic.sharepoint.com/sites/alpha/Shared Documents'
            ($objects | Where-Object type -eq 'sharepointGroup').externalId | Should -Be '11111111-1111-1111-1111-111111111111/7'
            $relations = Read-PackageEntry $zip 'relationships.jsonl'
            $user = $relations | Where-Object { $_.relationType -eq 'sitePermission' -and $_.from.type -eq 'user' }
            ($user.from.namespace, $user.from.externalId, $user.properties.roles[0]) | Should -Be @('upn', 'owner@synthetic.example', 'Full Control')
            ($relations | Where-Object { $_.relationType -eq 'sitePermission' -and $_.from.type -eq 'sharepointGroup' }).properties.roles | Should -Be @('Edit')
            ($relations | Where-Object { $_.relationType -eq 'sitePermission' -and $_.from.type -eq 'group' }).from.externalId |
                Should -Be '51111111-1111-1111-1111-111111111111'
            @($relations | Where-Object relationType -eq 'memberOf').Count | Should -Be 2
            ($relations | Where-Object relationType -eq 'partOf').to.externalId | Should -Be '11111111-1111-1111-1111-111111111111'
        } finally { $zip.Dispose() }
        Should -Invoke Connect-PnPOnline -ModuleName Clarity.Discovery.SharePoint -Times 1 -Exactly -ParameterFilter {
            $Interactive -and $ClientId -eq $script:clientId -and $Url -eq 'https://synthetic.sharepoint.com/sites/Alpha'
        }
        Should -Invoke Disconnect-PnPOnline -ModuleName Clarity.Discovery.SharePoint -Times 1 -Exactly
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'sharepoint-partial.zip')
        }
    }
    It 'collects site sharing settings for a SharePoint administrator and leaves unreadable usage unobserved' {
        Mock Get-PnPTenantSite -ModuleName Clarity.Discovery.SharePoint {
            [pscustomobject]@{
                Url = 'https://synthetic.sharepoint.com/sites/Alpha'; SharingCapability = 'ExternalUserSharingOnly'
                SharingDomainRestrictionMode = 'None'; DefaultSharingLinkType = 'Internal'; DefaultLinkPermission = 'View'; LockState = 'Unlock'
            }
        }
        Mock Get-PnPSite -ModuleName Clarity.Discovery.SharePoint {
            if ($Includes -contains 'Usage') { throw 'Synthetic usage denied.' }
            [pscustomobject]@{ Id = [guid]'11111111-1111-1111-1111-111111111111'; Url = 'https://synthetic.sharepoint.com/sites/Alpha' }
        }
        $parameters = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        $parameters.sections = @('sharepoint.siteSharing', 'sharepoint.siteDetails')
        $parameterPath = Join-Path $TestDrive 'sharing-parameters.json'
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $parameterPath -Encoding UTF8
        $path = Invoke-SharePointDiscovery -ParameterFile $parameterPath -OutputPath (Join-Path $TestDrive 'sharing.zip') -ClientId $script:clientId
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $results = Read-PackageEntry $zip 'results.json' | ConvertFrom-Json
            ($results | Where-Object capabilityId -eq 'sharepoint.siteSharing').status | Should -Be 'Complete'
            $details = $results | Where-Object capabilityId -eq 'sharepoint.siteDetails'
            ($details.status, $details.warnings[0].code) | Should -Be @('Partial', 'SiteUsageUnavailable')
            $objects = Read-PackageEntry $zip 'objects.jsonl'
            ($objects | Where-Object { $_.attributes.PSObject.Properties.Name -contains 'sharingCapability' }).attributes.sharingCapability |
                Should -Be 'ExternalUserSharingOnly'
            ($objects | Where-Object { $_.attributes.PSObject.Properties.Name -contains 'title' }).attributes.PSObject.Properties.Name |
                Should -Not -Contain 'storageUsedBytes'
        } finally { $zip.Dispose() }
        if ($env:CLARITY_SCRIPT_FIXTURES) { Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'sharepoint-sharing.zip') }
    }
    It 'blocks a different tenant before writing a package and closes the connection' {
        Mock Get-PnPTenantId -ModuleName Clarity.Discovery.SharePoint { '22222222-2222-3333-4444-555555555555' }
        { Invoke-SharePointDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'wrong.zip') `
                -ClientId $script:clientId } | Should -Throw '*does not match*'
        Test-Path (Join-Path $TestDrive 'wrong.zip') | Should -BeFalse
        Should -Invoke Disconnect-PnPOnline -ModuleName Clarity.Discovery.SharePoint -Times 1 -Exactly
    }
    It 'requires a client ID and the site collection root before collecting' {
        { Invoke-SharePointDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'no-client.zip') `
                -ClientId 'not-a-guid' } | Should -Throw '*client ID*'
        Should -Invoke Connect-PnPOnline -ModuleName Clarity.Discovery.SharePoint -Times 0 -Exactly
        Mock Get-PnPSite -ModuleName Clarity.Discovery.SharePoint {
            [pscustomobject]@{ Id = [guid]'11111111-1111-1111-1111-111111111111'; Url = 'https://synthetic.sharepoint.com/sites/Other' }
        }
        { Invoke-SharePointDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'subweb.zip') `
                -ClientId $script:clientId } | Should -Throw '*root of a readable site collection*'
        Test-Path (Join-Path $TestDrive 'subweb.zip') | Should -BeFalse
    }
}
