BeforeAll {
    $script:originalOS = $env:OS
    $env:OS = 'Windows_NT'
    function global:Get-ADDomain { [CmdletBinding()]param($Server) }
    function global:Get-ADForest { [CmdletBinding()]param($Server) }
    function global:Get-ADObject { [CmdletBinding()]param($Identity, $Server, $Properties) }
    function global:Get-ADUser { [CmdletBinding()]param($Filter, $SearchBase, $Server, $ResultPageSize, $Properties) }
    function global:Get-ADComputer { [CmdletBinding()]param($Filter, $SearchBase, $Server, $ResultPageSize, $Properties) }
    function global:Get-ADGroup { [CmdletBinding()]param($Filter, $SearchBase, $Server, $ResultPageSize, $Properties) }
    function global:Get-ADOrganizationalUnit { [CmdletBinding()]param($Identity, $Filter, $SearchBase, $Server, $ResultPageSize, $Properties) }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.AD.psm1') -Force
}
AfterAll {
    $env:OS = $script:originalOS
    foreach ($name in @('Get-ADDomain','Get-ADForest','Get-ADObject','Get-ADUser','Get-ADComputer','Get-ADGroup','Get-ADOrganizationalUnit')) {
        Remove-Item -LiteralPath "Function:\$name" -ErrorAction SilentlyContinue
    }
}
Describe 'AD metadata collector' {
    BeforeEach {
        Mock Assert-DiscoveryPrerequisites -ModuleName Clarity.Discovery.AD {}
        Mock Import-Module -ModuleName Clarity.Discovery.AD {}
        Mock Get-Module -ModuleName Clarity.Discovery.AD { [pscustomobject]@{ Version = [version]'1.0.1.0' } }
        Mock Get-ADDomain -ModuleName Clarity.Discovery.AD { [pscustomobject]@{ DNSRoot = 'example.test'; Forest = 'forest.example.test'; DistinguishedName = 'DC=example,DC=test' } }
        Mock Get-ADForest -ModuleName Clarity.Discovery.AD { [pscustomobject]@{ Name = 'forest.example.test' } }
        Mock Get-ADObject -ModuleName Clarity.Discovery.AD {
            if ($Properties -like 'member;range=0-*') {
                return [pscustomobject]@{ 'member;range=0-999' = @(1..1000 | ForEach-Object { "CN=External$_,DC=other,DC=test" }) }
            }
            if ($Properties -like 'member;range=1000-*') {
                return [pscustomobject]@{ 'member;range=1000-*' = @('CN=User,DC=example,DC=test') }
            }
            return [pscustomobject]@{ objectGUID = [guid]'11111111-0000-0000-0000-000000000003'; ObjectClass = 'user' }
        }
        Mock Get-ADUser -ModuleName Clarity.Discovery.AD {
            [pscustomobject]@{
                objectGUID = [guid]'11111111-0000-0000-0000-000000000003'; SID = 'S-1-5-21-100-200-300-1001'
                displayName = 'Synthetic User'; distinguishedName = 'CN=User,DC=example,DC=test'; sAMAccountName = 'synthetic'
                userPrincipalName = 'synthetic@example.test'; mail = $null; proxyAddresses = @()
                'mS-DS-ConsistencyGuid' = [byte[]](1,2,3,4); primaryGroupID = 513; enabled = $true; whenChanged = [datetime]::UtcNow
            }
        }
        Mock Get-ADComputer -ModuleName Clarity.Discovery.AD { throw 'Synthetic inaccessible section; never expose raw text.' }
        Mock Get-ADGroup -ModuleName Clarity.Discovery.AD {
            [pscustomobject]@{
                objectGUID = [guid]'11111111-0000-0000-0000-000000000004'; SID = 'S-1-5-21-100-200-300-513'
                displayName = 'Synthetic Group'; distinguishedName = 'CN=Group,DC=example,DC=test'; sAMAccountName = 'synthetic-group'
                mail = $null; proxyAddresses = @(); groupScope = 'Global'; groupCategory = 'Security'; whenChanged = [datetime]::UtcNow
            }
        }
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.AD { }
    }
    It 'collects identities, synchronization anchors, direct ranged memberships and primary groups while retaining partial results' {
        $path = Invoke-AdDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'ad.zip')
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $reader = [IO.StreamReader]::new($zip.GetEntry('results.json').Open())
            try { $results = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            ($results | Where-Object capabilityId -eq 'ad.users').status | Should -Be 'Complete'
            ($results | Where-Object capabilityId -eq 'ad.computers').status | Should -Be 'Failed'
            ($results | Where-Object capabilityId -eq 'ad.ous').objectCount | Should -Be 0
            ($results | Where-Object capabilityId -eq 'ad.groups').relationshipCount | Should -Be 1001
            $reader = [IO.StreamReader]::new($zip.GetEntry('objects.jsonl').Open())
            try { $objects = @($reader.ReadToEnd().Trim().Split("`n") | ConvertFrom-Json) } finally { $reader.Dispose() }
            ($objects | Where-Object type -eq 'user').attributes.msDSConsistencyGuid | Should -Be 'AQIDBA=='
            $reader = [IO.StreamReader]::new($zip.GetEntry('relationships.jsonl').Open())
            try { $relations = @($reader.ReadToEnd().Trim().Split("`n") | ConvertFrom-Json) } finally { $reader.Dispose() }
            ($relations | Where-Object relationType -eq 'primaryMemberOf').to.externalId | Should -Be 'S-1-5-21-100-200-300-513'
            @($relations | Where-Object { $_.properties.outOfScope -eq $true }).Count | Should -Be 1000
        } finally { $zip.Dispose() }
        Should -Invoke Get-ADObject -ModuleName Clarity.Discovery.AD -ParameterFilter { $Properties -like 'member;range=1000-*' } -Times 1 -Exactly
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'ad-partial.zip')
        }
    }
    It 'blocks a different forest before writing a package' {
        Mock Get-ADForest -ModuleName Clarity.Discovery.AD { [pscustomobject]@{ Name = 'other.example.test' } }
        { Invoke-AdDiscovery -ParameterFile (Join-Path $PSScriptRoot 'parameters.example.json') -OutputPath (Join-Path $TestDrive 'wrong.zip') } |
            Should -Throw '*does not match*'
        Test-Path (Join-Path $TestDrive 'wrong.zip') | Should -BeFalse
    }
    It 'collects computer and OU records with a verified OU request' {
        $parameters = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        $parameters.scope = @{ kind = 'ou'; value = 'OU=Departing,DC=example,DC=test' }
        $parameters.sections = @('ad.computers', 'ad.ous')
        $parameterPath = Join-Path $TestDrive 'ou-parameters.json'
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $parameterPath -Encoding UTF8
        Mock Get-ADComputer -ModuleName Clarity.Discovery.AD {
            [pscustomobject]@{
                objectGUID = [guid]'11111111-0000-0000-0000-000000000005'; SID = 'S-1-5-21-100-200-300-1002'
                displayName = 'Synthetic Computer'; distinguishedName = 'CN=PC,OU=Departing,DC=example,DC=test'
                sAMAccountName = 'synthetic-pc$'; dnsHostName = 'synthetic-pc.example.test'
                'mS-DS-ConsistencyGuid' = $null; primaryGroupID = 515; enabled = $true
                operatingSystem = 'Synthetic Windows'; whenChanged = [datetime]::UtcNow
            }
        }
        Mock Get-ADOrganizationalUnit -ModuleName Clarity.Discovery.AD {
            [pscustomobject]@{
                objectGUID = [guid]'11111111-0000-0000-0000-000000000006'
                displayName = 'Synthetic OU'; distinguishedName = 'OU=Departing,DC=example,DC=test'; whenChanged = [datetime]::UtcNow
            }
        }
        $path = Invoke-AdDiscovery -ParameterFile $parameterPath -OutputPath (Join-Path $TestDrive 'ad-ou.zip')
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $reader = [IO.StreamReader]::new($zip.GetEntry('results.json').Open())
            try { $results = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            @($results | Where-Object status -eq 'Complete').Count | Should -Be 2
            ($results | Where-Object capabilityId -eq 'ad.computers').objectCount | Should -Be 1
            ($results | Where-Object capabilityId -eq 'ad.ous').objectCount | Should -Be 1
        } finally { $zip.Dispose() }
        Should -Invoke Get-ADComputer -ModuleName Clarity.Discovery.AD -ParameterFilter { $SearchBase -eq 'OU=Departing,DC=example,DC=test' } -Times 1 -Exactly
        if ($env:CLARITY_SCRIPT_FIXTURES) { Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'ad-ou.zip') }
    }
}
