BeforeAll {
    $script:stubs = @('Connect-SPOService', 'Disconnect-SPOService', 'Get-SPOSite')
    function global:Connect-SPOService { [CmdletBinding()]param($Url) }
    function global:Disconnect-SPOService { [CmdletBinding()]param() }
    function global:Get-SPOSite { [CmdletBinding()]param($IncludePersonalSite, $Limit, $Filter) }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.OneDrive.psm1') -Force
    $script:adminUrl = 'https://synthetic-admin.sharepoint.com'
    $script:originalOs = $env:OS
    function script:Read-PackageEntry($Zip, [string]$Name) {
        $reader = [IO.StreamReader]::new($Zip.GetEntry($Name).Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($Name -like '*.jsonl') { return @($text.Trim().Split("`n") | Where-Object { $_ } | ConvertFrom-Json) }
        return $text
    }
}
AfterAll {
    foreach ($name in $script:stubs) { Remove-Item -LiteralPath "Function:\$name" -ErrorAction SilentlyContinue }
    $env:OS = $script:originalOs
}
Describe 'OneDrive SharePoint Online Management Shell collector' {
    BeforeEach {
        # The collector runs only in Windows PowerShell; the cmdlets are mocked so the suite also runs in CI.
        $env:OS = 'Windows_NT'
        Mock Assert-DiscoveryPrerequisites -ModuleName Clarity.Discovery.OneDrive {}
        Mock Import-Module -ModuleName Clarity.Discovery.OneDrive {}
        Mock Get-Module -ModuleName Clarity.Discovery.OneDrive { [pscustomobject]@{ Version = [version]'0.0.1' } }
        Mock Invoke-RestMethod -ModuleName Clarity.Discovery.OneDrive {
            [pscustomobject]@{ issuer = 'https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0' }
        }
        Mock Connect-SPOService -ModuleName Clarity.Discovery.OneDrive {}
        Mock Disconnect-SPOService -ModuleName Clarity.Discovery.OneDrive {}
        Mock Get-SPOSite -ModuleName Clarity.Discovery.OneDrive {
            function Site($Url, $Owner) {
                [pscustomobject]@{
                    Url = $Url; Owner = $Owner; StorageUsageCurrent = [long]1024; StorageQuota = [long]1048576; LockState = 'Unlock'; Status = 'Active'
                    LastContentModifiedDate = [datetime]::SpecifyKind([datetime]'2026-02-03T04:05:06', [DateTimeKind]::Unspecified)
                }
            }
            Site 'https://Synthetic-My.SharePoint.com/personal/adele_synthetic_example/' 'adele@synthetic.example'
            Site 'https://synthetic-my.sharepoint.com/personal/adele_synthetic_example' 'adele@synthetic.example'
            Site 'https://synthetic-my.sharepoint.com/personal/masked_synthetic_example' '0123456789ABCDEF0123456789ABCDEF'
            Site 'https://synthetic-my.sharepoint.com/personal/orphan_synthetic_example' ''
            Site 'https://other-my.sharepoint.com/personal/foreign_synthetic_example' 'foreign@other.example'
            [pscustomobject]@{ Url = 'https://synthetic-my.sharepoint.com/personal/sparse_synthetic_example'; Owner = 'sparse@synthetic.example' }
        }
    }
    It 'collects personal sites with provisional URL keys, links UPN owners and reports unresolved or masked owners' {
        $path = Invoke-OneDriveDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') `
            -OutputPath (Join-Path $TestDrive 'onedrive.zip') -AdminUrl $script:adminUrl
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $manifest = Read-PackageEntry $zip 'manifest.json' | ConvertFrom-Json
            ($manifest.producerId, $manifest.sourceId) | Should -Be @('clarity.onedrive', 'synthetic-admin.sharepoint.com')
            $result = Read-PackageEntry $zip 'results.json' | ConvertFrom-Json
            ($result.status, $result.objectCount, $result.relationshipCount) | Should -Be @('Partial', 4, 2)
            $warnings = @{}
            foreach ($warning in $result.warnings) { $warnings[$warning.code] = $warning.message }
            @($warnings.Keys | Sort-Object) | Should -Be @('DuplicateSiteCollection', 'OwnerMasked', 'OwnerUnresolved', 'PersonalSiteAttributesMissing', 'SiteIdentifierUnavailable')
            $warnings.Values | ForEach-Object { $_ | Should -BeLike '1 *' }
            $objects = Read-PackageEntry $zip 'objects.jsonl'
            $adele = $objects | Where-Object { $_.attributes.url -like '*adele*' }
            ($adele.type, $adele.namespace, $adele.externalId) |
                Should -Be @('oneDrive', 'sharepoint.siteUrl', 'https://synthetic-my.sharepoint.com/personal/adele_synthetic_example')
            ($adele.alternativeKeys[0].namespace, $adele.alternativeKeys[0].externalId) | Should -Be @('url', $adele.externalId)
            $adele.attributes.storageUsedMegabytes | Should -Be 1024
            # An unspecified module date is kept as UTC, not shifted by the workstation time zone.
            ([datetime]$adele.attributes.lastContentModifiedDate).ToUniversalTime() |
                Should -Be ([datetime]::SpecifyKind([datetime]'2026-02-03T04:05:06', [DateTimeKind]::Utc))
            $masked = $objects | Where-Object { $_.attributes.url -like '*masked*' }
            $masked.attributes.PSObject.Properties.Name | Should -Not -Contain 'ownerPrincipalName'
            Read-PackageEntry $zip 'objects.jsonl' | ConvertTo-Json -Depth 10 | Should -Not -Match '0123456789ABCDEF'
            ($objects | Where-Object { $_.attributes.url -like '*sparse*' }).attributes.PSObject.Properties.Name | Should -Not -Contain 'status'
            $relations = Read-PackageEntry $zip 'relationships.jsonl'
            $owner = $relations | Where-Object { $_.from.externalId -eq 'adele@synthetic.example' }
            ($owner.relationType, $owner.from.type, $owner.from.namespace, $owner.to.namespace) | Should -Be @('ownerOf', 'user', 'upn', 'sharepoint.siteUrl')
        } finally { $zip.Dispose() }
        Should -Invoke Connect-SPOService -ModuleName Clarity.Discovery.OneDrive -Times 1 -Exactly -ParameterFilter { $Url -eq $script:adminUrl }
        Should -Invoke Get-SPOSite -ModuleName Clarity.Discovery.OneDrive -Times 1 -Exactly -ParameterFilter {
            $IncludePersonalSite -eq $true -and $Limit -eq 'All' -and $Filter -eq "Url -like '-my.sharepoint.com/personal/'"
        }
        Should -Invoke Disconnect-SPOService -ModuleName Clarity.Discovery.OneDrive -Times 1 -Exactly
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'onedrive-personal.zip')
        }
    }
    It 'reports a failed read explicitly without exposing the module error' {
        Mock Get-SPOSite -ModuleName Clarity.Discovery.OneDrive { throw 'Synthetic access denied; never expose raw text.' }
        $path = Invoke-OneDriveDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') `
            -OutputPath (Join-Path $TestDrive 'failed.zip') -AdminUrl $script:adminUrl
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $result = Read-PackageEntry $zip 'results.json' | ConvertFrom-Json
            ($result.status, $result.errors[0].code) | Should -Be @('Failed', 'SpoReadFailed')
            $result.errors[0].message | Should -Not -BeLike '*Synthetic access denied*'
        } finally { $zip.Dispose() }
        if ($env:CLARITY_SCRIPT_FIXTURES) { Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'onedrive-failed.zip') }
    }
    It 'blocks an admin URL of another tenant before sign-in and writes no package' {
        Mock Invoke-RestMethod -ModuleName Clarity.Discovery.OneDrive {
            [pscustomobject]@{ issuer = 'https://login.microsoftonline.com/22222222-2222-3333-4444-555555555555/v2.0' }
        }
        { Invoke-OneDriveDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'wrong.zip') `
                -AdminUrl $script:adminUrl } | Should -Throw '*does not belong to the requested tenant*'
        Mock Invoke-RestMethod -ModuleName Clarity.Discovery.OneDrive { throw 'Synthetic network failure.' }
        { Invoke-OneDriveDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'offline.zip') `
                -AdminUrl $script:adminUrl } | Should -Throw '*does not belong to the requested tenant*'
        Test-Path (Join-Path $TestDrive 'wrong.zip') | Should -BeFalse
        Test-Path (Join-Path $TestDrive 'offline.zip') | Should -BeFalse
        Should -Invoke Connect-SPOService -ModuleName Clarity.Discovery.OneDrive -Times 0 -Exactly
    }
    It 'requires a SharePoint admin URL and a tenant-bound request' {
        foreach ($invalid in @('https://synthetic.sharepoint.com', 'https://synthetic-my.sharepoint.com', 'http://synthetic-admin.sharepoint.com',
                'https://synthetic-admin.sharepoint.com.evil.example', 'https://synthetic-admin.sharepoint.com/sites/x')) {
            { Invoke-OneDriveDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'invalid.zip') `
                    -AdminUrl $invalid } | Should -Throw '*admin URL in the form*'
        }
        $parameters = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        $parameters.scope.value = '22222222-2222-3333-4444-555555555555'
        $parameterPath = Join-Path $TestDrive 'other-scope.json'
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $parameterPath -Encoding UTF8
        { Invoke-OneDriveDiscovery -ParameterFile $parameterPath -OutputPath (Join-Path $TestDrive 'invalid.zip') -AdminUrl $script:adminUrl } |
            Should -Throw '*tenant scope is the requested tenant*'
        Should -Invoke Invoke-RestMethod -ModuleName Clarity.Discovery.OneDrive -Times 0 -Exactly
        Should -Invoke Connect-SPOService -ModuleName Clarity.Discovery.OneDrive -Times 0 -Exactly
    }
}
