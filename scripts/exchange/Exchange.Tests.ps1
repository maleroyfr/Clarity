BeforeAll {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function global:Get-ConnectionInformation { [CmdletBinding()]param() }
    function global:Get-EXORecipient { [CmdletBinding()]param($ResultSize, $Properties) }
    function global:Get-EXOMailbox { [CmdletBinding()]param($ResultSize, $Properties) }
    function global:Get-EXOMailboxStatistics { [CmdletBinding()]param($UserPrincipalName, [switch]$Archive, $Properties) }
    function global:Get-EXOMailboxPermission { [CmdletBinding()]param([guid]$ExternalDirectoryObjectId) }
    function global:Get-EXORecipientPermission { [CmdletBinding()]param([guid]$ExternalDirectoryObjectId) }
    function global:Get-DistributionGroup { [CmdletBinding()]param($ResultSize) }
    function global:Get-DistributionGroupMember { [CmdletBinding()]param($Identity, $ResultSize) }
    function global:Get-AcceptedDomain { [CmdletBinding()]param() }
    function global:Get-InboundConnector { [CmdletBinding()]param() }
    function global:Get-OutboundConnector { [CmdletBinding()]param() }
    function global:Get-TransportRule { [CmdletBinding()]param() }

    # Synthetic data only. Mocks run in the module scope, so the builders are global.
    function global:New-ExchangeTestRecipient {
        param([int]$Number, [string]$Alias, [string]$Details, [string]$Display, [bool]$HasUpn = $true, [bool]$HasEdoid = $true,
            [string]$Archive = 'None', [string[]]$Aliases = @(), [string[]]$SendOnBehalf = @())
        $suffix = '{0:D12}' -f $Number
        $mail = "$Alias@contoso.example"
        [pscustomobject]@{
            Guid = [guid]"22222222-0000-0000-0000-$suffix"
            ExternalDirectoryObjectId = $(if ($HasEdoid) { [guid]"33333333-0000-0000-0000-$suffix" } else { $null })
            ExchangeGuid = [guid]"44444444-0000-0000-0000-$suffix"
            Alias = $Alias; DisplayName = $Display; PrimarySmtpAddress = $mail
            EmailAddresses = @("SMTP:$mail") + @($Aliases | ForEach-Object { "smtp:$_" }) + @("x500:/o=Synthetic/cn=$Alias")
            RecipientType = $(if ($Details -like '*Mailbox') { 'UserMailbox' } elseif ($Details -like '*Group') { 'MailUniversalDistributionGroup' } else { 'MailContact' })
            RecipientTypeDetails = $Details
            UserPrincipalName = $(if ($HasUpn) { $mail } else { $null })
            ArchiveStatus = $Archive; ArchiveState = $(if ($Archive -eq 'Active') { 'Local' } else { 'None' })
            GrantSendOnBehalfTo = $SendOnBehalf
        }
    }
    function global:Get-ExchangeTestRecipients {
        @(
            (New-ExchangeTestRecipient 1 'alice' 'UserMailbox' 'Alice Synthetic' -Archive Active -Aliases @('a.synthetic@contoso.example'))
            (New-ExchangeTestRecipient 2 'bob' 'UserMailbox' 'Bob Synthetic' -SendOnBehalf @('alice@contoso.example'))
            (New-ExchangeTestRecipient 3 'support' 'SharedMailbox' 'Support Synthetic' -SendOnBehalf @('Bob Synthetic'))
            (New-ExchangeTestRecipient 4 'room1' 'RoomMailbox' 'Room 1 Synthetic')
            (New-ExchangeTestRecipient 5 'contact' 'MailContact' 'Contact Synthetic' -HasUpn $false -HasEdoid $false)
            (New-ExchangeTestRecipient 6 'team' 'MailUniversalDistributionGroup' 'Team Synthetic' -HasUpn $false)
            (New-ExchangeTestRecipient 7 'carol' 'UserMailbox' 'Carol Synthetic' -HasEdoid $false)
        )
    }
    function global:New-ExchangeTestParameters {
        param([string]$Path, [string[]]$Sections, [string]$Tenant = '11111111-0000-0000-0000-0000000000e1')
        $parameters = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        $parameters.expectedEnvironmentExternalId = $Tenant
        $parameters.scope.value = $Tenant
        if ($Sections) { $parameters.sections = $Sections }
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
        return $Path
    }
    function global:Read-ExchangeTestPackage {
        param([string]$Path)
        $zip = [IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $read = {
                param($name)
                $reader = [IO.StreamReader]::new($zip.GetEntry($name).Open())
                try { $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
            $lines = { param($name) @((& $read $name).Trim().Split("`n") | Where-Object { $_.Trim() } | ConvertFrom-Json) }
            return @{
                Results = @((& $read 'results.json') | ConvertFrom-Json)
                ResultsText = (& $read 'results.json')
                Manifest = (& $read 'manifest.json') | ConvertFrom-Json
                Objects = & $lines 'objects.jsonl'
                Relations = & $lines 'relationships.jsonl'
            }
        } finally { $zip.Dispose() }
    }
    $script:parameters = Join-Path $PSScriptRoot 'parameters.example.json'
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Exchange.psm1') -Force
}
AfterAll {
    foreach ($name in @('Get-ConnectionInformation', 'Get-EXORecipient', 'Get-EXOMailbox', 'Get-EXOMailboxStatistics', 'Get-EXOMailboxPermission',
            'Get-EXORecipientPermission', 'Get-DistributionGroup', 'Get-DistributionGroupMember', 'Get-AcceptedDomain', 'Get-InboundConnector',
            'Get-OutboundConnector', 'Get-TransportRule', 'New-ExchangeTestRecipient', 'Get-ExchangeTestRecipients', 'New-ExchangeTestParameters',
            'Read-ExchangeTestPackage')) {
        Remove-Item -LiteralPath "Function:\$name" -ErrorAction SilentlyContinue
    }
}
Describe 'Exchange Online metadata collector' {
    BeforeEach {
        Mock Assert-DiscoveryPrerequisites -ModuleName Clarity.Discovery.Exchange {}
        Mock Import-Module -ModuleName Clarity.Discovery.Exchange {}
        Mock Get-Module -ModuleName Clarity.Discovery.Exchange { [pscustomobject]@{ Version = [version]'3.9.0' } }
        Mock Get-ConnectionInformation -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ State = 'Connected'; TenantID = [guid]'11111111-0000-0000-0000-0000000000e1' }
        }
        Mock Get-EXORecipient -ModuleName Clarity.Discovery.Exchange { Get-ExchangeTestRecipients }
        Mock Get-EXOMailbox -ModuleName Clarity.Discovery.Exchange { Get-ExchangeTestRecipients | Where-Object { $_.RecipientTypeDetails -like '*Mailbox' } }
        Mock Get-EXOMailboxStatistics -ModuleName Clarity.Discovery.Exchange {
            $archiveSize = [pscustomobject]@{}
            $archiveRequested = ($null -ne $Archive -and $Archive.IsPresent)
            Add-Member -InputObject $archiveSize -MemberType ScriptMethod -Name ToBytes -Value { [long]2048 }
            switch ("$UserPrincipalName|$archiveRequested") {
                'alice@contoso.example|False' {
                    [pscustomobject]@{ TotalItemSize = '1.5 GB (1,610,612,736 bytes)'; ItemCount = 1200; LastLogonTime = [datetime]'2026-03-01T10:00:00Z' }
                }
                'alice@contoso.example|True' { [pscustomobject]@{ TotalItemSize = [pscustomobject]@{ Value = $archiveSize }; ItemCount = 30 } }
                'bob@contoso.example|False' { [pscustomobject]@{ TotalItemSize = 'Unlimited'; ItemCount = 7; LastLogonTime = $null } }
                'room1@contoso.example|False' { throw 'Synthetic failure for secret@contoso.example token=abc123' }
                default { [pscustomobject]@{ TotalItemSize = [long]1000; ItemCount = 5; LastLogonTime = $null } }
            }
        }
        Mock Get-EXOMailboxPermission -ModuleName Clarity.Discovery.Exchange {
            switch ($ExternalDirectoryObjectId.ToString()) {
                '33333333-0000-0000-0000-000000000003' {
                    [pscustomobject]@{ User = 'NT AUTHORITY\SELF'; AccessRights = @('FullAccess'); Deny = $false; IsInherited = $false }
                    [pscustomobject]@{ User = 'alice@contoso.example'; AccessRights = @('FullAccess', 'ReadPermission'); Deny = $false; IsInherited = $false }
                    [pscustomobject]@{ User = 'bob@contoso.example'; AccessRights = @('FullAccess'); Deny = $false; IsInherited = $true }
                    [pscustomobject]@{ User = 'ghost@elsewhere.example'; AccessRights = @('FullAccess'); Deny = $false; IsInherited = $false }
                    [pscustomobject]@{ User = 'carol@contoso.example'; AccessRights = @('ReadPermission'); Deny = $false; IsInherited = $false }
                }
                '33333333-0000-0000-0000-000000000004' {
                    [pscustomobject]@{ User = 'bob@contoso.example'; AccessRights = @('FullAccess'); Deny = $false; IsInherited = $false }
                    [pscustomobject]@{ User = 'bob@contoso.example'; AccessRights = @('FullAccess'); Deny = $true; IsInherited = $false }
                    [pscustomobject]@{ User = 'S-1-5-21-1-2-3-4'; AccessRights = @('FullAccess'); Deny = $false; IsInherited = $false }
                }
            }
        }
        Mock Get-EXORecipientPermission -ModuleName Clarity.Discovery.Exchange {
            if ($ExternalDirectoryObjectId.ToString() -eq '33333333-0000-0000-0000-000000000003') {
                [pscustomobject]@{ Trustee = 'alice@contoso.example'; AccessRights = @('SendAs'); AccessControlType = 'Allow'; IsInherited = $false }
                [pscustomobject]@{ Trustee = 'NT AUTHORITY\SELF'; AccessRights = @('SendAs'); AccessControlType = 'Allow'; IsInherited = $false }
            }
        }
        Mock Get-DistributionGroup -ModuleName Clarity.Discovery.Exchange { Get-ExchangeTestRecipients | Where-Object { $_.RecipientTypeDetails -like '*Group' } }
        Mock Get-DistributionGroupMember -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ Guid = [guid]'22222222-0000-0000-0000-000000000001'; RecipientTypeDetails = 'UserMailbox' }
            [pscustomobject]@{ Guid = [guid]'22222222-0000-0000-0000-000000000005'; RecipientTypeDetails = 'MailContact' }
            [pscustomobject]@{ Guid = $null; RecipientTypeDetails = 'MailUser' }
        }
        Mock Get-AcceptedDomain -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ Guid = [guid]'55555555-0000-0000-0000-000000000001'; DomainName = 'contoso.example'; DomainType = 'Authoritative'; Default = $true; InitialDomain = $false }
            [pscustomobject]@{ Guid = [guid]'55555555-0000-0000-0000-000000000002'; DomainName = 'contoso.onmicrosoft.example'; DomainType = 'Authoritative'; Default = $false; InitialDomain = $true }
        }
        Mock Get-InboundConnector -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ Guid = [guid]'66666666-0000-0000-0000-000000000001'; Name = 'Synthetic inbound'; Enabled = $true; ConnectorType = 'Partner'; SenderDomains = @('smtp:partner.example;1') }
        }
        Mock Get-OutboundConnector -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ Guid = [guid]'66666666-0000-0000-0000-000000000002'; Name = 'Synthetic outbound'; Enabled = $false; ConnectorType = 'OnPremises'; RecipientDomains = @('onprem.example') }
        }
        Mock Get-TransportRule -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ Guid = [guid]'77777777-0000-0000-0000-000000000001'; Name = 'Synthetic rule'; State = 'Enabled'; Mode = 'Enforce'; Priority = 0; WhenChanged = [datetime]'2026-02-01T00:00:00Z' }
        }
    }
    It 'collects recipients, mailboxes, sizes, aliases, delegations and groups with sanitized partial results' {
        $path = Invoke-ExchangeDiscovery -ParameterFile $script:parameters -OutputPath (Join-Path $TestDrive 'exchange.zip')
        $package = Read-ExchangeTestPackage $path
        $status = @{}
        foreach ($result in $package.Results) { $status[$result.capabilityId] = $result }
        $package.Manifest.producerId | Should -Be 'clarity.exchange'
        $package.Manifest.environmentExternalId | Should -Be '11111111-0000-0000-0000-0000000000e1'
        $package.Manifest.moduleVersions.ExchangeOnlineManagement | Should -Be '3.9.0'
        $status['exchange.recipients'].status | Should -Be 'Complete'
        $status['exchange.recipients'].objectCount | Should -Be 7
        $status['exchange.mailboxes'].status | Should -Be 'Complete'
        $status['exchange.mailboxes'].objectCount | Should -Be 5
        $status['exchange.mailboxStatistics'].status | Should -Be 'Partial'
        $status['exchange.mailboxPermissions'].status | Should -Be 'Partial'
        $status['exchange.mailEnabledGroups'].status | Should -Be 'Partial'
        foreach ($name in 'acceptedDomains', 'connectors', 'transportRules') { $status["exchange.$name"].status | Should -Be 'Complete' }
        $status['exchange.acceptedDomains'].objectCount | Should -Be 2
        $status['exchange.connectors'].objectCount | Should -Be 2
        $package.ResultsText | Should -Not -Match 'secret|abc123|ghost|alice|@'

        $alice = $package.Objects | Where-Object { $_.sectionId -eq $status['exchange.mailboxes'].sectionId -and $_.attributes.alias -eq 'alice' }
        $alice.type | Should -Be 'mailbox'
        $alice.namespace | Should -Be 'exchange.guid'
        $alice.externalId | Should -Be '22222222-0000-0000-0000-000000000001'
        $alice.attributes.userPrincipalName | Should -Be 'alice@contoso.example'
        $alice.attributes.externalDirectoryObjectId | Should -Be '33333333-0000-0000-0000-000000000001'
        $alice.attributes.proxyAddresses | Should -Contain 'smtp:a.synthetic@contoso.example'
        $alice.attributes.archiveStatus | Should -Be 'Active'
        @($package.Objects | Where-Object { $_.sectionId -eq $status['exchange.mailboxes'].sectionId } | ForEach-Object type | Sort-Object -Unique) |
            Should -Be @('mailbox', 'resourceMailbox', 'sharedMailbox')
        @($package.Objects | Where-Object { $_.sectionId -eq $status['exchange.recipients'].sectionId } | ForEach-Object type | Sort-Object -Unique) |
            Should -Be @('mailbox', 'mailGroup', 'recipient', 'resourceMailbox', 'sharedMailbox')
    }
    It 'reads exact sizes, tolerates unreadable values and never estimates' {
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'stats.json') @('exchange.mailboxes', 'exchange.mailboxStatistics')) `
            -OutputPath (Join-Path $TestDrive 'stats.zip')
        $package = Read-ExchangeTestPackage $path
        $result = $package.Results | Where-Object capabilityId -eq 'exchange.mailboxStatistics'
        $result.status | Should -Be 'Partial'
        @($result.warnings | ForEach-Object code | Sort-Object) | Should -Be @('UnreadableMailbox', 'UnreadableSize')
        $result.objectCount | Should -Be 4
        $stats = @($package.Objects | Where-Object sectionId -eq $result.sectionId)
        $alice = $stats | Where-Object externalId -eq '22222222-0000-0000-0000-000000000001'
        $alice.attributes.sizeBytes | Should -Be 1610612736
        $alice.attributes.itemCount | Should -Be 1200
        $alice.attributes.archiveSizeBytes | Should -Be 2048
        $alice.attributes.archiveItemCount | Should -Be 30
        ([datetime]$alice.attributes.lastLogonTime).ToUniversalTime().ToString('o') | Should -Be '2026-03-01T10:00:00.0000000Z'
        $bob = $stats | Where-Object externalId -eq '22222222-0000-0000-0000-000000000002'
        $bob.attributes.sizeBytes | Should -BeNullOrEmpty
        $bob.attributes.archiveSizeBytes | Should -BeNullOrEmpty
        ($stats | Where-Object externalId -eq '22222222-0000-0000-0000-000000000003').attributes.sizeBytes | Should -Be 1000
        @($stats | Where-Object externalId -eq '22222222-0000-0000-0000-000000000004').Count | Should -Be 0
        Should -Invoke Get-EXOMailboxStatistics -ModuleName Clarity.Discovery.Exchange -ParameterFilter { $Archive } -Times 1 -Exactly
    }
    It 'links user mailboxes by Entra object ID first, then UPN, then SMTP, and keeps shared and resource mailboxes independent' {
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'links.json') @('exchange.mailboxes')) `
            -OutputPath (Join-Path $TestDrive 'links.zip')
        $package = Read-ExchangeTestPackage $path
        ($package.Results | Where-Object capabilityId -eq 'exchange.mailboxes').relationshipCount | Should -Be 3
        $alice = $package.Relations | Where-Object { $_.from.externalId -eq '22222222-0000-0000-0000-000000000001' }
        $alice.relationType | Should -Be 'mailboxOf'
        $alice.from.type | Should -Be 'mailbox'
        $alice.to.type | Should -Be 'user'
        $alice.to.namespace | Should -Be 'microsoft.graph'
        $alice.to.externalId | Should -Be '33333333-0000-0000-0000-000000000001'
        @($alice.properties.fallbackTo | ForEach-Object { "$($_.namespace):$($_.externalId)" }) |
            Should -Be @('upn:alice@contoso.example', 'smtp:alice@contoso.example')
        $carol = $package.Relations | Where-Object { $_.from.externalId -eq '22222222-0000-0000-0000-000000000007' }
        $carol.to.namespace | Should -Be 'upn'
        $carol.to.externalId | Should -Be 'carol@contoso.example'
        @($carol.properties.fallbackTo | ForEach-Object { "$($_.namespace):$($_.externalId)" }) | Should -Be @('smtp:carol@contoso.example')
        @($package.Relations | Where-Object { $_.from.externalId -in @('22222222-0000-0000-0000-000000000003', '22222222-0000-0000-0000-000000000004') }).Count |
            Should -Be 0
    }
    It 'reports explicit Full Access, Send As and Send on Behalf, ignoring inherited, denied, self and orphaned entries' {
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'permissions.json') @('exchange.mailboxPermissions')) `
            -OutputPath (Join-Path $TestDrive 'permissions.zip')
        $package = Read-ExchangeTestPackage $path
        $result = $package.Results[0]
        $result.status | Should -Be 'Partial'
        $result.relationshipCount | Should -Be 4
        @($result.warnings | ForEach-Object code | Sort-Object) | Should -Be @('UnreadableMailbox', 'UnresolvedTrustees')
        $edges = @($package.Relations | ForEach-Object { "$($_.relationType):$($_.from.externalId.Substring(24))>$($_.to.type):$($_.to.externalId.Substring(24))" } | Sort-Object)
        $edges | Should -Be @(
            'fullAccessTo:000000000001>sharedMailbox:000000000003'
            'sendAsOn:000000000001>sharedMailbox:000000000003'
            'sendOnBehalfOf:000000000001>mailbox:000000000002'
            'sendOnBehalfOf:000000000002>sharedMailbox:000000000003')
        Should -Invoke Get-EXOMailboxPermission -ModuleName Clarity.Discovery.Exchange -Times 4 -Exactly
        Should -Invoke Get-EXOMailboxPermission -ModuleName Clarity.Discovery.Exchange -ParameterFilter { $ExternalDirectoryObjectId -eq [guid]::Empty } -Times 0 -Exactly
        $package.ResultsText | Should -Not -Match 'ghost|elsewhere'
    }
    It 'does not resolve an ambiguous trustee' {
        Mock Get-EXORecipient -ModuleName Clarity.Discovery.Exchange {
            $first = New-ExchangeTestRecipient 11 'twin1' 'UserMailbox' 'Twin Synthetic'
            $second = New-ExchangeTestRecipient 12 'twin2' 'UserMailbox' 'Twin Synthetic'
            $target = New-ExchangeTestRecipient 13 'target' 'SharedMailbox' 'Target Synthetic' -SendOnBehalf @('Twin Synthetic')
            @($first, $second, $target)
        }
        Mock Get-EXOMailbox -ModuleName Clarity.Discovery.Exchange { New-ExchangeTestRecipient 13 'target' 'SharedMailbox' 'Target Synthetic' -SendOnBehalf @('Twin Synthetic') }
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'ambiguous.json') @('exchange.mailboxPermissions')) `
            -OutputPath (Join-Path $TestDrive 'ambiguous.zip')
        $package = Read-ExchangeTestPackage $path
        $package.Results[0].status | Should -Be 'Partial'
        $package.Results[0].relationshipCount | Should -Be 0
        $package.Results[0].warnings[0].code | Should -Be 'UnresolvedTrustees'
    }
    It 'collects mail-enabled groups with direct members and counts members without a readable GUID' {
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'groups.json') @('exchange.mailEnabledGroups')) `
            -OutputPath (Join-Path $TestDrive 'groups.zip')
        $package = Read-ExchangeTestPackage $path
        $result = $package.Results[0]
        $result.status | Should -Be 'Partial'
        $result.warnings[0].code | Should -Be 'UnresolvedMembers'
        $result.objectCount | Should -Be 1
        $result.relationshipCount | Should -Be 2
        $package.Objects[0].type | Should -Be 'mailGroup'
        @($package.Relations | ForEach-Object { "$($_.from.type):$($_.to.type)" } | Sort-Object) | Should -Be @('mailbox:mailGroup', 'recipient:mailGroup')
        Should -Invoke Get-DistributionGroupMember -ModuleName Clarity.Discovery.Exchange -ParameterFilter { $Identity -eq '22222222-0000-0000-0000-000000000006' } -Times 1 -Exactly
    }
    It 'collects accepted domains, connectors and transport rules as tenant configuration' {
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'config.json') @('exchange.acceptedDomains', 'exchange.connectors', 'exchange.transportRules')) `
            -OutputPath (Join-Path $TestDrive 'config.zip')
        $package = Read-ExchangeTestPackage $path
        @($package.Results | Where-Object status -eq 'Complete').Count | Should -Be 3
        $default = $package.Objects | Where-Object { $_.type -eq 'acceptedDomain' -and $_.attributes.isDefault }
        $default.attributes.displayName | Should -Be 'contoso.example'
        ($package.Objects | Where-Object { $_.type -eq 'connector' -and $_.attributes.direction -eq 'Inbound' }).attributes.senderDomains | Should -Be 'smtp:partner.example;1'
        ($package.Objects | Where-Object { $_.type -eq 'connector' -and $_.attributes.direction -eq 'Outbound' }).attributes.enabled | Should -BeFalse
        $rule = $package.Objects | Where-Object type -eq 'transportRule'
        $rule.attributes.enabled | Should -BeTrue
        $rule.attributes.priority | Should -Be 0
    }
    It 'keeps sibling sections when one read fails and never exposes raw error text' {
        Mock Get-TransportRule -ModuleName Clarity.Discovery.Exchange { throw 'Synthetic failure for secret@contoso.example token=abc123' }
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'failed.json') @('exchange.acceptedDomains', 'exchange.transportRules')) `
            -OutputPath (Join-Path $TestDrive 'failed.zip')
        $package = Read-ExchangeTestPackage $path
        ($package.Results | Where-Object capabilityId -eq 'exchange.acceptedDomains').status | Should -Be 'Complete'
        $failed = $package.Results | Where-Object capabilityId -eq 'exchange.transportRules'
        $failed.status | Should -Be 'Failed'
        $failed.errors[0].code | Should -Be 'ExchangeReadFailed'
        $package.ResultsText | Should -Not -Match 'secret|abc123'
    }
    It 'never calls statistics or permission cmdlets without an identifier' {
        Mock Get-EXOMailbox -ModuleName Clarity.Discovery.Exchange { New-ExchangeTestRecipient 21 'orphan' 'UserMailbox' 'Orphan Synthetic' -HasUpn $false -HasEdoid $false }
        $path = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'identity.json') @('exchange.mailboxStatistics', 'exchange.mailboxPermissions')) `
            -OutputPath (Join-Path $TestDrive 'identity.zip')
        $package = Read-ExchangeTestPackage $path
        @($package.Results | Where-Object status -eq 'Failed').Count | Should -Be 2
        Should -Invoke Get-EXOMailboxStatistics -ModuleName Clarity.Discovery.Exchange -Times 0 -Exactly
        Should -Invoke Get-EXOMailboxPermission -ModuleName Clarity.Discovery.Exchange -Times 0 -Exactly
        Should -Invoke Get-EXORecipientPermission -ModuleName Clarity.Discovery.Exchange -Times 0 -Exactly
    }
    It 'blocks a different tenant before reading Exchange data or writing a package' {
        Mock Get-ConnectionInformation -ModuleName Clarity.Discovery.Exchange {
            [pscustomobject]@{ State = 'Connected'; TenantID = [guid]'99999999-0000-0000-0000-000000000009' }
        }
        { Invoke-ExchangeDiscovery -ParameterFile $script:parameters -OutputPath (Join-Path $TestDrive 'wrong.zip') } | Should -Throw '*does not match*'
        Test-Path (Join-Path $TestDrive 'wrong.zip') | Should -BeFalse
        Should -Invoke Get-EXORecipient -ModuleName Clarity.Discovery.Exchange -Times 0 -Exactly
    }
    It 'requires exactly one connected session and a supported module without connecting or installing' {
        Mock Get-ConnectionInformation -ModuleName Clarity.Discovery.Exchange { @() }
        { Invoke-ExchangeDiscovery -ParameterFile $script:parameters -OutputPath (Join-Path $TestDrive 'none.zip') } | Should -Throw '*Connect-ExchangeOnline*'
        Mock Get-Module -ModuleName Clarity.Discovery.Exchange { [pscustomobject]@{ Version = [version]'2.0.6' } }
        { Invoke-ExchangeDiscovery -ParameterFile $script:parameters -OutputPath (Join-Path $TestDrive 'old.zip') } | Should -Throw '*3.0.0*'
    }
    It 'rejects unknown or duplicate sections and a scope that is not the requested tenant' {
        { Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'unknown.json') @('exchange.unknown')) -OutputPath (Join-Path $TestDrive 'u.zip') } |
            Should -Throw '*Unknown or duplicate*'
        { Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'dup.json') @('exchange.recipients', 'exchange.recipients')) -OutputPath (Join-Path $TestDrive 'd.zip') } |
            Should -Throw '*Unknown or duplicate*'
        $parameters = Get-Content $script:parameters -Raw | ConvertFrom-Json
        $parameters.scope.value = '99999999-0000-0000-0000-000000000009'
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $TestDrive 'scope.json') -Encoding UTF8
        { Invoke-ExchangeDiscovery -ParameterFile (Join-Path $TestDrive 'scope.json') -OutputPath (Join-Path $TestDrive 's.zip') } | Should -Throw '*requested tenant*'
    }
    It 'publishes generated packages for the .NET contract validator' {
        if (-not $env:CLARITY_SCRIPT_FIXTURES) { Set-ItResult -Skipped -Because 'CLARITY_SCRIPT_FIXTURES is not set.'; return }
        [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
        $full = Invoke-ExchangeDiscovery -ParameterFile $script:parameters -OutputPath (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'exchange-partial.zip')
        $config = Invoke-ExchangeDiscovery -ParameterFile (New-ExchangeTestParameters (Join-Path $TestDrive 'fixture.json') @('exchange.acceptedDomains', 'exchange.connectors', 'exchange.transportRules')) `
            -OutputPath (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'exchange-complete.zip')
        Test-Path $full | Should -BeTrue
        Test-Path $config | Should -BeTrue
    }
}
