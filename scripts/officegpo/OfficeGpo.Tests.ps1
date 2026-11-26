BeforeAll {
    function global:Get-ADDomain { [CmdletBinding()]param($Server) }
    function global:Get-ADForest { [CmdletBinding()]param($Server) }
    function global:Get-ADOrganizationalUnit { [CmdletBinding()]param($Identity, $Filter, $SearchBase, $SearchScope, $Server, $ResultPageSize) }
    function global:Get-GPO { [CmdletBinding()]param([switch]$All, $Guid, $Domain, $Server) }
    function global:Get-GPInheritance { [CmdletBinding()]param($Target, $Domain, $Server) }
    function global:Get-GPPermission { [CmdletBinding()]param($Guid, [switch]$All, $DomainName, $Server) }
    function global:Get-GPRegistryValue { [CmdletBinding()]param($Guid, $Key, $Domain, $Server) }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.OfficeGpo.psm1') -Force
    function Read-OfficeGpoZip($Path) {
        $zip = [IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $result = @{}
            foreach ($name in @('manifest.json','results.json','objects.jsonl','relationships.jsonl')) {
                $reader = [IO.StreamReader]::new($zip.GetEntry($name).Open())
                try {
                    $text = $reader.ReadToEnd()
                    $result[$name] = if ($name.EndsWith('.jsonl')) { @($text -split "`n" | Where-Object { $_.Trim() } | ConvertFrom-Json) } else { $text | ConvertFrom-Json }
                } finally { $reader.Dispose() }
            }
            return $result
        } finally { $zip.Dispose() }
    }
    function Invoke-Fixture($Name, $Parameters = (Join-Path $PSScriptRoot 'parameters.example.json')) {
        $path = Invoke-OfficeGpoDiscovery -ParameterFile $Parameters -OutputPath (Join-Path $TestDrive "$Name.zip")
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES "office-gpo-$Name.zip")
        }
        return Read-OfficeGpoZip $path
    }
}
AfterAll {
    foreach ($name in @('Get-ADDomain','Get-ADForest','Get-ADOrganizationalUnit','Get-GPO','Get-GPInheritance','Get-GPPermission','Get-GPRegistryValue')) {
        Remove-Item -LiteralPath "Function:\$name" -ErrorAction SilentlyContinue
    }
}
Describe 'Office/GPO metadata collector' {
    BeforeEach {
        Mock Initialize-OfficeGpoModules -ModuleName Clarity.Discovery.OfficeGpo {
            @{ GroupPolicy = '1.0.0.0'; ActiveDirectory = '1.0.1.0'; PowerShell = $PSVersionTable.PSVersion.ToString() }
        }
        Mock Get-ADDomain -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ DNSRoot = 'example.test'; Forest = 'forest.example.test'; DistinguishedName = 'DC=example,DC=test'; PDCEmulator = 'dc.example.test' }
        }
        Mock Get-ADForest -ModuleName Clarity.Discovery.OfficeGpo { [pscustomobject]@{ Name = 'forest.example.test' } }
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ ObjectGUID = [guid]'44444444-0000-0000-0000-000000000002'; DistinguishedName = 'OU=Pilot,DC=example,DC=test' }
        }
        Mock Get-GPO -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{
                Id = [guid]'44444444-0000-0000-0000-000000000003'; DisplayName = 'Synthetic Office policy'; DomainName = 'example.test'
                GpoStatus = 'AllSettingsEnabled'; CreationTime = [datetime]'2026-01-01T00:00:00Z'
                ModificationTime = [datetime]'2026-01-02T00:00:00Z'; WmiFilter = $null
            }
        }
        Mock Get-GPInheritance -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{
                Path = $Target; GpoInheritanceBlocked = 'No'
                GpoLinks = @([pscustomobject]@{ GpoId = [guid]'44444444-0000-0000-0000-000000000003'; Target = $Target; Enabled = $false; Enforced = $true; Order = 1 })
                InheritedGpoLinks = @([pscustomobject]@{ GpoId = [guid]'44444444-0000-0000-0000-000000000099' })
            }
        }
        Mock Get-GPPermission -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ Trustee = [pscustomobject]@{ Sid = [pscustomobject]@{ Value = 'S-1-5-21-100-200-300-2001' }; SidType = 'Group' }; Permission = 'GpoApply'; Inherited = $false }
            [pscustomobject]@{ Trustee = [pscustomobject]@{ Sid = 'S-1-5-21-100-200-300-2002'; SidType = 'Computer' }; Permission = 'GpoApply'; Inherited = $true }
            [pscustomobject]@{ Trustee = [pscustomobject]@{ Sid = 'S-1-5-21-100-200-300-2003'; SidType = 'User' }; Permission = 'GpoRead'; Inherited = $false }
            [pscustomobject]@{ Trustee = [pscustomobject]@{ Sid = 'S-1-5-21-100-200-300-2999'; SidType = 'Unknown' }; Permission = 'GpoApply'; Inherited = $false }
        }
        Mock Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ FullKeyPath = $Key; ValueName = 'VBAWarnings'; HasValue = $true; PolicyState = 'Set'; Type = 'DWord'; Value = 3 }
            [pscustomobject]@{ FullKeyPath = $Key; ValueName = 'blockcontentexecutionfrominternet'; HasValue = $true; PolicyState = 'Delete'; Type = 'DWord'; Value = $null }
            [pscustomobject]@{ FullKeyPath = $Key; ValueName = 'unrelatedValue'; Value = 'MUST-NOT-BE-EXPORTED' }
            [pscustomobject]@{ FullKeyPath = "$Key\Subkey" }
        }
    }
    It 'emits stable policies, direct OU links, typed principals and only supported ADMX values' {
        $data = Invoke-Fixture complete
        @($data['results.json'] | Where-Object status -eq Complete).Count | Should -Be 4
        $data['manifest.json'].producerId | Should -Be 'clarity.officegpo'
        $data['manifest.json'].moduleVersions.GroupPolicy | Should -Be '1.0.0.0'
        $data['manifest.json'].scope.value | Should -Be 'example.test'
        @($data['objects.jsonl']).Count | Should -Be 7
        $data['objects.jsonl'] | ConvertTo-Json -Depth 20 | Should -Not -Match 'MUST-NOT|effective'
        @($data['objects.jsonl'] | Where-Object { $_.attributes.policyState -eq 'Delete' }).Count | Should -Be 3
        $link = @($data['relationships.jsonl'] | Where-Object relationType -eq gpoLinkedToOu)[0]
        $link.to.namespace | Should -Be 'ad.objectGUID'
        $link.to.externalId | Should -Be '44444444-0000-0000-0000-000000000002'
        $link.properties.enabled | Should -BeFalse
        $link.properties.enforced | Should -BeTrue
        @($data['relationships.jsonl'] | Where-Object relationType -eq gpoSecurityFilter).to.type | Should -Be @('group','computer','directoryObject')
        @($data['relationships.jsonl'] | Where-Object relationType -eq gpoPermission).to.type | Should -Be @('user')
        Should -Invoke Get-GPO -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $All -and $Domain -eq 'example.test' -and $Server -eq 'dc.example.test' } -Times 1 -Exactly
        Should -Invoke Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo -Times 3 -Exactly
    }
    It 'preserves an exact OU subtree and does not select inherited GPOs or enumerate directory populations' {
        $parameters = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        $parameters.scope = @{ kind = 'ou'; value = 'OU=Pilot,DC=example,DC=test' }
        $path = Join-Path $TestDrive 'ou.json'
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
        $data = Invoke-Fixture ou $path
        $data['manifest.json'].scope.value | Should -Be $parameters.scope.value
        @($data['results.json'] | Where-Object { $_.scope.value -ne $parameters.scope.value }).Count | Should -Be 0
        Should -Invoke Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $SearchBase -eq 'OU=Pilot,DC=example,DC=test' -and $SearchScope -eq 'Subtree' } -Times 1 -Exactly
        Should -Invoke Get-GPO -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $All } -Times 0 -Exactly
        Should -Invoke Get-GPO -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $Guid -eq '44444444-0000-0000-0000-000000000003' } -Times 1 -Exactly
        Should -Invoke Get-GPInheritance -ModuleName Clarity.Discovery.OfficeGpo -Times 1 -Exactly
    }
    It 'distinguishes readable empty scope from an inaccessible scope without inventing zero counts' {
        Mock Get-GPO -ModuleName Clarity.Discovery.OfficeGpo {}
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo {}
        $empty = Invoke-Fixture empty
        @($empty['results.json'] | Where-Object { $_.status -eq 'Complete' -and $_.objectCount -eq 0 -and $_.relationshipCount -eq 0 }).Count | Should -Be 4
        Mock Get-GPO -ModuleName Clarity.Discovery.OfficeGpo { throw 'RAW-PRIVATE-ERROR' }
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo { throw 'RAW-PRIVATE-ERROR' }
        $failed = Invoke-Fixture failed
        @($failed['results.json'] | Where-Object { $_.status -eq 'Failed' -and $null -eq $_.objectCount }).Count | Should -Be 4
        $failed | ConvertTo-Json -Depth 20 | Should -Not -Match 'RAW-PRIVATE-ERROR'
    }
    It 'retains successful Office settings and permissions when another key or custom ACL cannot be evaluated' {
        Mock Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $Key -like '*\excel\*' } { throw 'Missing or inaccessible registry key.' }
        Mock Get-GPPermission -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ Trustee = [pscustomobject]@{ Sid = 'S-1-5-21-100-200-300-2001'; SidType = 'Group' }; Permission = 'GpoCustom'; Inherited = $false }
        }
        $data = Invoke-Fixture partial
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.officeSettings).status | Should -Be Partial
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.officeSettings).objectCount | Should -Be 4
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.securityFiltering).status | Should -Be Partial
        @($data['relationships.jsonl'] | Where-Object relationType -eq gpoSecurityFilter).Count | Should -Be 0
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.policies).status | Should -Be Complete
    }
    It 'preserves valid streamed GPOs and reduces every dependent section when enumeration stops' {
        Mock Get-GPO -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{
                Id = [guid]'44444444-0000-0000-0000-000000000003'; DisplayName = 'Synthetic Office policy'; DomainName = 'example.test'
                GpoStatus = 'AllSettingsEnabled'; CreationTime = [datetime]'2026-01-01Z'; ModificationTime = [datetime]'2026-01-02Z'; WmiFilter = $null
            }
            throw 'RAW-PRIVATE-ERROR'
        }
        $data = Invoke-Fixture interrupted
        @($data['results.json'] | Where-Object status -eq Partial).Count | Should -Be 3
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.policies).objectCount | Should -Be 1
    }
    It 'keeps valid OU links when another OU is unreadable' {
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ ObjectGUID = [guid]'44444444-0000-0000-0000-000000000002'; DistinguishedName = 'OU=Pilot,DC=example,DC=test' }
            [pscustomobject]@{ ObjectGUID = [guid]'44444444-0000-0000-0000-000000000004'; DistinguishedName = 'OU=Hidden,DC=example,DC=test' }
        }
        Mock Get-GPInheritance -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $Target -like 'OU=Hidden,*' } { throw 'RAW-PRIVATE-ERROR' }
        $data = Invoke-Fixture linksPartial
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.links).status | Should -Be Partial
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.links).relationshipCount | Should -Be 1
    }
    It 'retains partial zero when a verified empty OU and a missing link collection share a scope' {
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ ObjectGUID = [guid]'44444444-0000-0000-0000-000000000002'; DistinguishedName = 'OU=Pilot,DC=example,DC=test' }
            [pscustomobject]@{ ObjectGUID = [guid]'44444444-0000-0000-0000-000000000004'; DistinguishedName = 'OU=Hidden,DC=example,DC=test' }
        }
        Mock Get-GPInheritance -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ Path = $Target; GpoInheritanceBlocked = 'No'; GpoLinks = @() }
        }
        Mock Get-GPInheritance -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $Target -like 'OU=Hidden,*' } {
            [pscustomobject]@{ Path = $Target; GpoInheritanceBlocked = 'No'; GpoLinks = $null }
        }
        $data = Invoke-Fixture partialZero
        $result = $data['results.json'] | Where-Object capabilityId -eq officeGpo.links
        $result.status | Should -Be Partial
        $result.relationshipCount | Should -Be 0
        $result.errors.code | Should -Contain 'OuLinksReadFailed'
    }
    It 'distinguishes a readable registry key with no supported settings from inaccessible keys' {
        Mock Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo { throw 'Private key error.' }
        Mock Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo -ParameterFilter { $Key -like '*\word\*' } {
            [pscustomobject]@{ FullKeyPath = $Key; ValueName = 'anotherSetting'; Value = 'MUST-NOT-BE-EXPORTED' }
        }
        $data = Invoke-Fixture partialSettingsZero
        $result = $data['results.json'] | Where-Object capabilityId -eq officeGpo.officeSettings
        $result.status | Should -Be Partial
        $result.objectCount | Should -Be 0
        $result.errors.code | Should -Contain 'OfficeRegistryReadFailed'
    }
    It 'rejects a different observed forest before packaging' {
        Mock Get-ADForest -ModuleName Clarity.Discovery.OfficeGpo { [pscustomobject]@{ Name = 'other.test' } }
        { Invoke-Fixture wrongForest } | Should -Throw '*could not be verified*'
        Test-Path (Join-Path $TestDrive 'wrongForest.zip') | Should -BeFalse
        Should -Invoke Get-GPO -ModuleName Clarity.Discovery.OfficeGpo -Times 0 -Exactly
    }
    It 'rejects malformed supported registry values rather than leaking text or claiming complete coverage' {
        Mock Get-GPRegistryValue -ModuleName Clarity.Discovery.OfficeGpo {
            [pscustomobject]@{ FullKeyPath = $Key; ValueName = 'VBAWarnings'; HasValue = $true; PolicyState = 'Set'; Type = 'DWord'; Value = 'PRIVATE-TEXT' }
        }
        $data = Invoke-Fixture malformed
        ($data['results.json'] | Where-Object capabilityId -eq officeGpo.officeSettings).status | Should -Be Failed
        $data | ConvertTo-Json -Depth 20 | Should -Not -Match 'PRIVATE-TEXT'
    }
}
Describe 'Office/GPO prerequisite checks' {
    It 'rejects unsupported OS without attempting module installation' {
        InModuleScope Clarity.Discovery.OfficeGpo {
            $original = $env:OS
            try {
                $env:OS = 'SyntheticUnsupportedOS'
                { Initialize-OfficeGpoModules } | Should -Throw '*authorized Windows*'
            } finally { $env:OS = $original }
        }
    }
    It 'rejects a proxy runtime or reports the exact native module requirement without raw error text' {
        InModuleScope Clarity.Discovery.OfficeGpo {
            $original = $env:OS
            try {
                $env:OS = 'Windows_NT'
                Mock Assert-DiscoveryPrerequisites {}
                Mock Import-Module { throw 'RAW-PRIVATE-ERROR' }
                if ($PSVersionTable.PSEdition -ne 'Desktop') {
                    { Initialize-OfficeGpoModules } | Should -Throw '*Windows PowerShell 5.1*'
                    Should -Invoke Import-Module -Times 0 -Exactly
                } else {
                    { Initialize-OfficeGpoModules } | Should -Throw '*Cannot load GroupPolicy 1.0.0.0*'
                    Should -Invoke Assert-DiscoveryPrerequisites -ParameterFilter { 'GroupPolicy' -in $Modules -and 'ActiveDirectory' -in $Modules } -Times 1 -Exactly
                    Should -Invoke Import-Module -ParameterFilter { $Name -eq 'GroupPolicy' -and $RequiredVersion -eq '1.0.0.0' -and $PassThru } -Times 1 -Exactly
                }
            } finally { $env:OS = $original }
        }
    }
}
