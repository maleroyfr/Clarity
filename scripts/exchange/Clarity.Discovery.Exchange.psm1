Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

$script:CoreMap = @(
    @{ Field = 'displayName'; Property = 'DisplayName'; Convert = 'Get-ExText' }
    @{ Field = 'alias'; Property = 'Alias'; Convert = 'Get-ExText' }
    @{ Field = 'mail'; Property = 'PrimarySmtpAddress'; Convert = 'Get-ExText' }
    @{ Field = 'proxyAddresses'; Property = 'EmailAddresses'; Convert = 'Get-ExTextList' }
    @{ Field = 'recipientTypeDetails'; Property = 'RecipientTypeDetails'; Convert = 'Get-ExText' }
    @{ Field = 'externalDirectoryObjectId'; Property = 'ExternalDirectoryObjectId'; Convert = 'Get-ExGuidText' }
)
$script:RecipientMap = $script:CoreMap + @(@{ Field = 'recipientType'; Property = 'RecipientType'; Convert = 'Get-ExText' })
$script:MailboxMap = $script:CoreMap + @(
    @{ Field = 'userPrincipalName'; Property = 'UserPrincipalName'; Convert = 'Get-ExText' }
    @{ Field = 'exchangeGuid'; Property = 'ExchangeGuid'; Convert = 'Get-ExGuidText' }
    @{ Field = 'archiveStatus'; Property = 'ArchiveStatus'; Convert = 'Get-ExText' }
    @{ Field = 'archiveState'; Property = 'ArchiveState'; Convert = 'Get-ExText' }
)
$script:AcceptedDomainMap = @(
    @{ Field = 'displayName'; Property = 'DomainName'; Convert = 'Get-ExText' }
    @{ Field = 'domainType'; Property = 'DomainType'; Convert = 'Get-ExText' }
    @{ Field = 'isDefault'; Property = 'Default'; Convert = 'Get-ExBool' }
    @{ Field = 'isInitialDomain'; Property = 'InitialDomain'; Convert = 'Get-ExBool' }
)
$script:ConnectorMap = @(
    @{ Field = 'displayName'; Property = 'Name'; Convert = 'Get-ExText' }
    @{ Field = 'enabled'; Property = 'Enabled'; Convert = 'Get-ExBool' }
    @{ Field = 'connectorType'; Property = 'ConnectorType'; Convert = 'Get-ExText' }
)
$script:TransportRuleMap = @(
    @{ Field = 'displayName'; Property = 'Name'; Convert = 'Get-ExText' }
    @{ Field = 'enabled'; Property = 'State'; Convert = 'Get-ExEnabledState' }
    @{ Field = 'mode'; Property = 'Mode'; Convert = 'Get-ExText' }
    @{ Field = 'priority'; Property = 'Priority'; Convert = 'Get-ExInteger' }
    @{ Field = 'whenChanged'; Property = 'WhenChanged'; Convert = 'Get-ExUtc' }
)
$script:RecipientProperties = @('Alias', 'DisplayName', 'EmailAddresses', 'ExternalDirectoryObjectId', 'Guid', 'PrimarySmtpAddress', 'RecipientType', 'RecipientTypeDetails')
$script:MailboxProperties = @('Alias', 'ArchiveState', 'ArchiveStatus', 'DisplayName', 'EmailAddresses', 'ExchangeGuid', 'ExternalDirectoryObjectId',
    'GrantSendOnBehalfTo', 'Guid', 'PrimarySmtpAddress', 'RecipientTypeDetails', 'UserPrincipalName')
$script:StatisticsProperties = @('ItemCount', 'LastLogonTime', 'TotalItemSize')

# Fixed, aggregate-only messages: they never contain identities, addresses or exception text.
$script:IssueMessages = @{
    MissingAttributes = '{0} attribute values were not returned; verify the Exchange read role for this section, then relaunch.'
    MissingIdentity = '{0} objects had no readable Exchange GUID and were omitted.'
    UnreadableSize = '{0} mailbox sizes could not be parsed and were left empty.'
    UnreadableMailbox = '{0} mailboxes could not be read individually; verify the read role, then relaunch.'
    UnreadableArchive = '{0} archive mailboxes could not be read; verify the read role, then relaunch.'
    UnreadableGroup = '{0} groups could not be read individually; verify the read role, then relaunch.'
    UnresolvedMembers = '{0} group members had no readable Exchange GUID and were omitted.'
    UnresolvedTrustees = '{0} permission trustees could not be matched to a unique observed recipient and were omitted.'
    UnlinkedMailbox = '{0} user mailboxes had no identifier usable to link them to a user.'
}

function Get-ExValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Get-ExText($Value) {
    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim()
    if ($text.Length -eq 0) { return $null }
    return $text
}

function Get-ExTextList($Value) {
    $items = New-Object 'Collections.Generic.List[string]'
    foreach ($entry in @($Value)) {
        $text = Get-ExText $entry
        if ($null -ne $text) { $items.Add($text) }
    }
    return , [string[]]$items.ToArray()
}

function Get-ExGuidText($Value) {
    $text = Get-ExText $Value
    $parsed = [guid]::Empty
    if ($null -ne $text -and [guid]::TryParse($text, [ref]$parsed) -and $parsed -ne [guid]::Empty) { return $parsed.ToString() }
    return $null
}

function Get-ExBool($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [bool]) { return $Value }
    $parsed = $false
    if ([bool]::TryParse([string]$Value, [ref]$parsed)) { return $parsed }
    return $null
}

function Get-ExEnabledState($Value) {
    $text = Get-ExText $Value
    if ($null -eq $text) { return $null }
    return $text -ieq 'Enabled'
}

function Get-ExInteger($Value) {
    if ($null -eq $Value) { return $null }
    $parsed = [long]0
    if ([long]::TryParse(([string]$Value).Trim(), [ref]$parsed) -and $parsed -ge 0) { return $parsed }
    return $null
}

function Get-ExUtc($Value) {
    if ($null -eq $Value) { return $null }
    $parsed = [datetime]::MinValue
    if ($Value -is [datetime]) { $parsed = $Value }
    elseif (-not [datetime]::TryParse([string]$Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) { return $null }
    return $parsed.ToUniversalTime().ToString('o')
}

# A size that cannot be read exactly is null; it is never estimated from a rounded display value.
function Get-ExBytes($Value) {
    if ($null -eq $Value) { return $null }
    $inner = $Value
    if ($null -ne $Value.PSObject.Properties['Value'] -and $null -ne $Value.Value) { $inner = $Value.Value }
    $number = [long]0
    if ($inner -is [ValueType] -and $inner -isnot [bool] -and $inner -isnot [datetime]) {
        if ([long]::TryParse([string]$inner, [ref]$number) -and $number -ge 0) { return $number }
        return $null
    }
    if ($null -ne $inner.PSObject.Methods['ToBytes']) {
        try { $number = [long]$inner.ToBytes() } catch { return $null }
        if ($number -ge 0) { return $number }
        return $null
    }
    $text = [string]$inner
    if ($text -match '\(([0-9][0-9,\. ]*)\s*bytes\)' -and [long]::TryParse(($Matches[1] -replace '[^0-9]', ''), [ref]$number)) { return $number }
    if ($text -match '^\s*([0-9]+)\s*(B|bytes)?\s*$' -and [long]::TryParse($Matches[1], [ref]$number)) { return $number }
    return $null
}

function Get-ExObjectType([string]$Details) {
    switch ($Details) {
        'UserMailbox' { 'mailbox' }
        'SharedMailbox' { 'sharedMailbox' }
        'RoomMailbox' { 'resourceMailbox' }
        'EquipmentMailbox' { 'resourceMailbox' }
        'MailUniversalDistributionGroup' { 'mailGroup' }
        'MailUniversalSecurityGroup' { 'mailGroup' }
        'MailNonUniversalGroup' { 'mailGroup' }
        'DynamicDistributionGroup' { 'mailGroup' }
        'RoomList' { 'mailGroup' }
        default { 'recipient' }
    }
}

function Add-ExIssue($Issues, [string]$Code, [int]$Count = 1) {
    if ($Issues.ContainsKey($Code)) { $Issues[$Code] += $Count } else { $Issues[$Code] = $Count }
}

function Get-ExWarnings($Issues) {
    foreach ($code in ($Issues.Keys | Sort-Object)) {
        @{ code = $code; message = [string]::Format($script:IssueMessages[$code], $Issues[$code]) }
    }
}

function Get-ExReference([string]$Type, [string]$Guid) {
    return @{ type = $Type; namespace = 'exchange.guid'; externalId = $Guid }
}

function ConvertTo-ExRecord($Item, $Map) {
    $guid = Get-ExGuidText (Get-ExValue $Item 'Guid')
    $fields = @{}
    foreach ($entry in $Map) {
        $property = $Item.PSObject.Properties[$entry.Property]
        if ($null -eq $property) { continue }
        $fields[$entry.Field] = & $entry.Convert $property.Value
    }
    return @{
        Guid = $guid; Type = (Get-ExObjectType ([string]$fields['recipientTypeDetails'])); Details = $fields['recipientTypeDetails']
        Fields = $fields; SendOnBehalf = (Get-ExTextList (Get-ExValue $Item 'GrantSendOnBehalfTo'))
    }
}

function Get-ExRecipientRecords($State) {
    if ($null -eq $State.Recipients) {
        $records = New-Object 'Collections.Generic.List[object]'
        Get-EXORecipient -ResultSize Unlimited -Properties $script:RecipientProperties -ErrorAction Stop |
            ForEach-Object { $records.Add((ConvertTo-ExRecord $_ $script:RecipientMap)) }
        $State.Recipients = @($records | Sort-Object { [string]$_.Guid })
    }
    return $State.Recipients
}

function Get-ExMailboxRecords($State) {
    if ($null -eq $State.Mailboxes) {
        $records = New-Object 'Collections.Generic.List[object]'
        Get-EXOMailbox -ResultSize Unlimited -Properties $script:MailboxProperties -ErrorAction Stop |
            ForEach-Object { $records.Add((ConvertTo-ExRecord $_ $script:MailboxMap)) }
        $State.Mailboxes = @($records | Sort-Object { [string]$_.Guid })
    }
    return $State.Mailboxes
}

function Write-ExObjects($Package, $Records, [string[]]$Fields, $Issues) {
    foreach ($record in $Records) {
        if ($null -eq $record.Guid) { Add-ExIssue $Issues 'MissingIdentity'; continue }
        $attributes = @{}
        foreach ($field in $Fields) {
            if ($record.Fields.ContainsKey($field)) { $attributes[$field] = $record.Fields[$field] }
        }
        if ($attributes.Count -lt $Fields.Count) { Add-ExIssue $Issues 'MissingAttributes' ($Fields.Count - $attributes.Count) }
        Write-DiscoveryObject -Package $Package -Type $record.Type -Namespace 'exchange.guid' -ExternalId $record.Guid -Attributes $attributes
    }
}

function Write-ExRecipientsSection($Package, $State, [string[]]$Fields, $Issues) {
    Write-ExObjects $Package @(Get-ExRecipientRecords $State) $Fields $Issues
}

function Write-ExMailboxesSection($Package, $State, [string[]]$Fields, $Issues) {
    $records = @(Get-ExMailboxRecords $State)
    Write-ExObjects $Package $records $Fields $Issues
    foreach ($record in $records | Where-Object { $null -ne $_.Guid -and $_.Details -ieq 'UserMailbox' }) {
        # Precedence: Entra object ID, then exact UPN, then exact primary SMTP address. The first is the edge target;
        # the projection tries the others, in order, only when the first does not resolve to an observed user.
        $targets = New-Object 'Collections.Generic.List[object]'
        foreach ($candidate in @(
                @{ Namespace = 'microsoft.graph'; Value = $record.Fields['externalDirectoryObjectId'] }
                @{ Namespace = 'upn'; Value = $record.Fields['userPrincipalName'] }
                @{ Namespace = 'smtp'; Value = $record.Fields['mail'] })) {
            if ($candidate.Value) { $targets.Add(@{ type = 'user'; namespace = $candidate.Namespace; externalId = [string]$candidate.Value }) }
        }
        if ($targets.Count -eq 0) { Add-ExIssue $Issues 'UnlinkedMailbox'; continue }
        $properties = @{}
        if ($targets.Count -gt 1) { $properties['fallbackTo'] = @($targets.GetRange(1, $targets.Count - 1).ToArray()) }
        Write-DiscoveryRelationship -Package $Package -RelationType mailboxOf -From (Get-ExReference $record.Type $record.Guid) `
            -To $targets[0] -Properties $properties
    }
}

function Read-ExStatistics($Statistics, $Issues, [bool]$IncludeLastLogon) {
    $result = @{ Size = $null; Count = $null; LastLogon = $null }
    $size = $Statistics.PSObject.Properties['TotalItemSize']
    $count = $Statistics.PSObject.Properties['ItemCount']
    if ($null -eq $size) { Add-ExIssue $Issues 'MissingAttributes' } else {
        $result.Size = Get-ExBytes $size.Value
        if ($null -eq $result.Size -and $null -ne $size.Value) { Add-ExIssue $Issues 'UnreadableSize' }
    }
    if ($null -eq $count) { Add-ExIssue $Issues 'MissingAttributes' } else { $result.Count = Get-ExInteger $count.Value }
    if ($IncludeLastLogon) {
        $logon = $Statistics.PSObject.Properties['LastLogonTime']
        if ($null -eq $logon) { Add-ExIssue $Issues 'MissingAttributes' } else { $result.LastLogon = Get-ExUtc $logon.Value }
    }
    return $result
}

function Write-ExStatisticsSection($Package, $State, [string[]]$Fields, $Issues) {
    $records = @(Get-ExMailboxRecords $State)
    $read = 0
    foreach ($record in $records) {
        if ($null -eq $record.Guid) { Add-ExIssue $Issues 'MissingIdentity'; continue }
        $upn = $record.Fields['userPrincipalName']
        # A null identity makes the cmdlet return every mailbox, so it is never passed.
        if (-not $upn) { Add-ExIssue $Issues 'UnreadableMailbox'; continue }
        try {
            $statistics = @(Get-EXOMailboxStatistics -UserPrincipalName $upn -Properties $script:StatisticsProperties -ErrorAction Stop)
            if ($statistics.Count -eq 0) { throw 'No statistics were returned.' }
            $primary = Read-ExStatistics $statistics[0] $Issues $true
        } catch { Add-ExIssue $Issues 'UnreadableMailbox'; continue }
        $attributes = @{ sizeBytes = $primary.Size; itemCount = $primary.Count; lastLogonTime = $primary.LastLogon }
        if ($record.Fields['archiveStatus'] -ieq 'Active') {
            try {
                $archive = @(Get-EXOMailboxStatistics -UserPrincipalName $upn -Archive -Properties $script:StatisticsProperties -ErrorAction Stop)
                if ($archive.Count -eq 0) { throw 'No archive statistics were returned.' }
                $archiveValues = Read-ExStatistics $archive[0] $Issues $false
                $attributes['archiveSizeBytes'] = $archiveValues.Size
                $attributes['archiveItemCount'] = $archiveValues.Count
            } catch { Add-ExIssue $Issues 'UnreadableArchive' }
        } else {
            $attributes['archiveSizeBytes'] = $null
            $attributes['archiveItemCount'] = $null
        }
        $read++
        Write-DiscoveryObject -Package $Package -Type $record.Type -Namespace 'exchange.guid' -ExternalId $record.Guid -Attributes $attributes
    }
    if ($records.Count -gt 0 -and $read -eq 0) { throw 'No mailbox statistics could be read.' }
}

function Add-ExTrusteeKey($Tier, $Key, [string]$Guid) {
    $text = Get-ExText $Key
    if ($null -eq $text) { return }
    $normalized = $text.ToLowerInvariant()
    if (-not $Tier.ContainsKey($normalized)) { $Tier[$normalized] = New-Object 'Collections.Generic.HashSet[string]' }
    [void]$Tier[$normalized].Add($Guid)
}

function Get-ExTrusteeIndex($State) {
    if ($null -ne $State.Trustees) { return $State.Trustees }
    $index = @{ Identifier = @{}; Alias = @{}; Name = @{}; ByGuid = @{} }
    foreach ($record in @(Get-ExRecipientRecords $State) + @(Get-ExMailboxRecords $State)) {
        if ($null -eq $record.Guid) { continue }
        $index.ByGuid[$record.Guid] = $record
        foreach ($key in @($record.Guid, $record.Fields['externalDirectoryObjectId'], $record.Fields['mail'], $record.Fields['userPrincipalName'])) {
            Add-ExTrusteeKey $index.Identifier $key $record.Guid
        }
        foreach ($address in @($record.Fields['proxyAddresses'])) {
            if ($address -match '^(?i:smtp):(.+)$') { Add-ExTrusteeKey $index.Identifier $Matches[1] $record.Guid }
        }
        Add-ExTrusteeKey $index.Alias $record.Fields['alias'] $record.Guid
        Add-ExTrusteeKey $index.Name $record.Fields['displayName'] $record.Guid
    }
    $State.Trustees = $index
    return $index
}

# Exact, unique matches only. An ambiguous value at a tier is never resolved by falling through to a weaker tier.
function Resolve-ExTrustee($State, [string]$Text) {
    $key = Get-ExText $Text
    if ($null -eq $key) { return $null }
    $index = Get-ExTrusteeIndex $State
    foreach ($tier in 'Identifier', 'Alias', 'Name') {
        $candidates = $index[$tier][$key.ToLowerInvariant()]
        if ($null -eq $candidates) { continue }
        if ($candidates.Count -ne 1) { return $null }
        return $index.ByGuid[@($candidates)[0]]
    }
    return $null
}

function Test-ExIgnoredTrustee([string]$Text) {
    $value = Get-ExText $Text
    return $null -eq $value -or $value -match '^(?i:NT AUTHORITY\\)' -or $value -match '^S-1-[0-9]+(-[0-9]+)+$'
}

function Test-ExRight($Entry, [string]$Right) {
    return @(@(Get-ExValue $Entry 'AccessRights') | Where-Object { [string]$_ -ieq $Right }).Count -gt 0
}

# Explicit allows only; inherited entries and any trustee with a matching deny are not reported as grants.
function Get-ExExplicitGrants($Entries, [string]$TrusteeProperty, [string]$Right, [scriptblock]$IsDeny) {
    $denied = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $allowed = New-Object 'Collections.Generic.List[string]'
    foreach ($entry in @($Entries)) {
        $trustee = Get-ExText (Get-ExValue $entry $TrusteeProperty)
        if ($null -eq $trustee -or -not (Test-ExRight $entry $Right)) { continue }
        if (& $IsDeny $entry) { [void]$denied.Add($trustee); continue }
        if ((Get-ExBool (Get-ExValue $entry 'IsInherited')) -eq $true) { continue }
        $allowed.Add($trustee)
    }
    return , [string[]]@($allowed | Where-Object { -not $denied.Contains($_) })
}

function Write-ExPermissionsSection($Package, $State, [string[]]$Fields, $Issues) {
    $mailboxes = @(Get-ExMailboxRecords $State)
    $written = New-Object 'Collections.Generic.HashSet[string]'
    $read = 0
    foreach ($mailbox in $mailboxes) {
        $edoid = $mailbox.Fields['externalDirectoryObjectId']
        # A null identifier makes the cmdlets return every mailbox, so it is never passed.
        if ($null -eq $mailbox.Guid -or -not $edoid) { Add-ExIssue $Issues 'UnreadableMailbox'; continue }
        $grants = New-Object 'Collections.Generic.List[object]'
        try {
            $fullAccess = Get-ExExplicitGrants @(Get-EXOMailboxPermission -ExternalDirectoryObjectId ([guid]$edoid) -ErrorAction Stop) 'User' 'FullAccess' {
                param($entry) (Get-ExBool (Get-ExValue $entry 'Deny')) -eq $true }
            $sendAs = Get-ExExplicitGrants @(Get-EXORecipientPermission -ExternalDirectoryObjectId ([guid]$edoid) -ErrorAction Stop) 'Trustee' 'SendAs' {
                param($entry) (Get-ExText (Get-ExValue $entry 'AccessControlType')) -ieq 'Deny' }
        } catch { Add-ExIssue $Issues 'UnreadableMailbox'; continue }
        foreach ($trustee in $fullAccess) { $grants.Add(@{ Type = 'fullAccessTo'; Trustee = $trustee }) }
        foreach ($trustee in $sendAs) { $grants.Add(@{ Type = 'sendAsOn'; Trustee = $trustee }) }
        foreach ($trustee in @($mailbox.SendOnBehalf)) { $grants.Add(@{ Type = 'sendOnBehalfOf'; Trustee = $trustee }) }
        $read++
        $to = Get-ExReference $mailbox.Type $mailbox.Guid
        foreach ($grant in $grants) {
            if (Test-ExIgnoredTrustee $grant.Trustee) { continue }
            $trustee = Resolve-ExTrustee $State $grant.Trustee
            if ($null -eq $trustee) { Add-ExIssue $Issues 'UnresolvedTrustees'; continue }
            if ($trustee.Guid -eq $mailbox.Guid) { continue }
            if (-not $written.Add("$($grant.Type)|$($trustee.Guid)|$($mailbox.Guid)")) { continue }
            Write-DiscoveryRelationship -Package $Package -RelationType $grant.Type -From (Get-ExReference $trustee.Type $trustee.Guid) -To $to
        }
    }
    if ($mailboxes.Count -gt 0 -and $read -eq 0) { throw 'No mailbox permissions could be read.' }
}

function Write-ExGroupsSection($Package, $State, [string[]]$Fields, $Issues) {
    $groups = New-Object 'Collections.Generic.List[object]'
    Get-DistributionGroup -ResultSize Unlimited -ErrorAction Stop | ForEach-Object { $groups.Add((ConvertTo-ExRecord $_ $script:CoreMap)) }
    $sorted = @($groups | Sort-Object { [string]$_.Guid })
    Write-ExObjects $Package $sorted $Fields $Issues
    $read = 0
    foreach ($group in $sorted | Where-Object { $null -ne $_.Guid }) {
        try { $members = @(Get-DistributionGroupMember -Identity $group.Guid -ResultSize Unlimited -ErrorAction Stop) }
        catch { Add-ExIssue $Issues 'UnreadableGroup'; continue }
        $read++
        $to = Get-ExReference $group.Type $group.Guid
        $seen = New-Object 'Collections.Generic.HashSet[string]'
        foreach ($member in $members) {
            $guid = Get-ExGuidText (Get-ExValue $member 'Guid')
            if ($null -eq $guid) { Add-ExIssue $Issues 'UnresolvedMembers'; continue }
            if (-not $seen.Add($guid)) { continue }
            $type = Get-ExObjectType ([string](Get-ExText (Get-ExValue $member 'RecipientTypeDetails')))
            Write-DiscoveryRelationship -Package $Package -RelationType memberOf -From (Get-ExReference $type $guid) -To $to
        }
    }
    if ($sorted.Count -gt 0 -and $read -eq 0) { throw 'No group membership could be read.' }
}

function Write-ExConfigurationObjects($Package, $Items, [string]$Type, $Map, $Issues, [scriptblock]$Extra = $null) {
    foreach ($item in @($Items | Sort-Object { [string](Get-ExValue $_ 'Guid') })) {
        $guid = Get-ExGuidText (Get-ExValue $item 'Guid')
        if ($null -eq $guid) { Add-ExIssue $Issues 'MissingIdentity'; continue }
        $attributes = @{}
        foreach ($entry in $Map) {
            $property = $item.PSObject.Properties[$entry.Property]
            if ($null -eq $property) { Add-ExIssue $Issues 'MissingAttributes'; continue }
            $attributes[$entry.Field] = & $entry.Convert $property.Value
        }
        if ($null -ne $Extra) {
            $additional = & $Extra $item
            foreach ($name in $additional.Keys) { $attributes[$name] = $additional[$name] }
        }
        Write-DiscoveryObject -Package $Package -Type $Type -Namespace 'exchange.guid' -ExternalId $guid -Attributes $attributes
    }
}

function Write-ExAcceptedDomainsSection($Package, $State, [string[]]$Fields, $Issues) {
    Write-ExConfigurationObjects $Package @(Get-AcceptedDomain -ErrorAction Stop) 'acceptedDomain' $script:AcceptedDomainMap $Issues
}

function Write-ExConnectorsSection($Package, $State, [string[]]$Fields, $Issues) {
    $inbound = @(Get-InboundConnector -ErrorAction Stop)
    $outbound = @(Get-OutboundConnector -ErrorAction Stop)
    Write-ExConfigurationObjects $Package $inbound 'connector' $script:ConnectorMap $Issues {
        param($item) @{ direction = 'Inbound'; senderDomains = (Get-ExTextList (Get-ExValue $item 'SenderDomains')); recipientDomains = [string[]]@() }
    }
    Write-ExConfigurationObjects $Package $outbound 'connector' $script:ConnectorMap $Issues {
        param($item) @{ direction = 'Outbound'; senderDomains = [string[]]@(); recipientDomains = (Get-ExTextList (Get-ExValue $item 'RecipientDomains')) }
    }
}

function Write-ExTransportRulesSection($Package, $State, [string[]]$Fields, $Issues) {
    Write-ExConfigurationObjects $Package @(Get-TransportRule -ErrorAction Stop) 'transportRule' $script:TransportRuleMap $Issues
}

function Get-ExDefinitions {
    $core = @($script:CoreMap | ForEach-Object { $_.Field })
    return @{
        'exchange.recipients' = @{ Fields = @($script:RecipientMap | ForEach-Object { $_.Field }); Relations = @(); Run = 'Write-ExRecipientsSection' }
        'exchange.mailboxes' = @{ Fields = @($script:MailboxMap | ForEach-Object { $_.Field }); Relations = @('mailboxOf'); Run = 'Write-ExMailboxesSection' }
        'exchange.mailboxStatistics' = @{ Fields = @('sizeBytes', 'itemCount', 'archiveSizeBytes', 'archiveItemCount', 'lastLogonTime'); Relations = @(); Run = 'Write-ExStatisticsSection' }
        'exchange.mailboxPermissions' = @{ Fields = @(); Relations = @('fullAccessTo', 'sendAsOn', 'sendOnBehalfOf'); Run = 'Write-ExPermissionsSection' }
        'exchange.mailEnabledGroups' = @{ Fields = $core; Relations = @('memberOf'); Run = 'Write-ExGroupsSection' }
        'exchange.acceptedDomains' = @{ Fields = @('displayName', 'domainType', 'isDefault', 'isInitialDomain'); Relations = @(); Run = 'Write-ExAcceptedDomainsSection' }
        'exchange.connectors' = @{ Fields = @('displayName', 'enabled', 'direction', 'connectorType', 'senderDomains', 'recipientDomains'); Relations = @(); Run = 'Write-ExConnectorsSection' }
        'exchange.transportRules' = @{ Fields = @('displayName', 'enabled', 'mode', 'priority', 'whenChanged'); Relations = @(); Run = 'Write-ExTransportRulesSection' }
    }
}

function Invoke-ExchangeDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    Assert-DiscoveryPrerequisites -MinimumPowerShell '5.1' -Modules @('ExchangeOnlineManagement')
    Import-Module ExchangeOnlineManagement -ErrorAction Stop
    $request = Read-DiscoveryParameters -Path $ParameterFile
    if ($request.environmentKind -ne 'M365Tenant' -or $request.scope.kind -ne 'tenant' -or $request.producerVersion -ne '1.0.0' -or
        $request.scope.value -ine $request.expectedEnvironmentExternalId) {
        throw 'Use an Exchange 1.0.0 request whose tenant scope is the requested tenant.'
    }
    $definitions = Get-ExDefinitions
    if (@($request.sections | Where-Object { -not $definitions.ContainsKey($_) }).Count -gt 0 -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) { throw 'Unknown or duplicate Exchange sections.' }
    $module = @(Get-Module -Name ExchangeOnlineManagement | Sort-Object Version -Descending) | Select-Object -First 1
    if ($null -eq $module -or $module.Version -lt [version]'3.0.0') {
        throw 'ExchangeOnlineManagement 3.0.0 or later is required. Ask your administrator to install it separately; no installation was performed.'
    }
    $connections = @(Get-ConnectionInformation -ErrorAction Stop | Where-Object { [string]$_.State -eq 'Connected' })
    if ($connections.Count -ne 1) {
        throw 'Connect to exactly one Exchange Online tenant with Connect-ExchangeOnline, then rerun. No sign-in was attempted.'
    }
    $connectedTenant = Get-ExGuidText (Get-ExValue $connections[0] 'TenantID')
    if ($null -eq $connectedTenant -or $connectedTenant -ine (Get-ExGuidText $request.expectedEnvironmentExternalId)) {
        throw 'The connected Exchange Online tenant does not match the request. Reconnect to the requested tenant and relaunch.'
    }
    $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.exchange' -ProducerVersion '1.0.0' `
        -EnvironmentExternalId ([string]$request.expectedEnvironmentExternalId) `
        -ModuleVersions @{ ExchangeOnlineManagement = $module.Version.ToString(); PowerShell = $PSVersionTable.PSVersion.ToString() } `
        -SourceId 'Exchange Online PowerShell'
    $state = @{ Recipients = $null; Mailboxes = $null; Trustees = $null }
    foreach ($capability in $request.sections) {
        $definition = $definitions[$capability]
        Start-DiscoverySection -Package $package -CapabilityId $capability -Scope $request.scope `
            -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
        $issues = New-Object 'Collections.Generic.Dictionary[string,int]'
        try {
            & $definition.Run $package $state ([string[]]$definition.Fields) $issues
            $warnings = @(Get-ExWarnings $issues)
            $status = if ($warnings.Count -gt 0) { 'Partial' } else { 'Complete' }
            Complete-DiscoverySection -Package $package -Status $status -Warnings $warnings
        } catch {
            Complete-DiscoverySection -Package $package -Status Failed `
                -Errors @(@{ code = 'ExchangeReadFailed'; message = 'Read failed. Verify the Exchange Online session and read permissions for this section, then relaunch.' })
        }
    }
    Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
}

Export-ModuleMember -Function Invoke-ExchangeDiscovery
