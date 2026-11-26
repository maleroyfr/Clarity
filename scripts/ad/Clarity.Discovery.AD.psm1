Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

function Get-AdValue($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Get-AdSid($Object) {
    $sid = Get-AdValue $Object 'SID'
    if ($null -eq $sid) { $sid = Get-AdValue $Object 'objectSid' }
    if ($null -eq $sid) { return $null }
    if ($null -ne $sid.PSObject.Properties['Value']) { return [string]$sid.Value }
    return [string]$sid
}

function Get-AdReference($Object, [string]$Type) {
    $guid = Get-AdValue $Object 'objectGUID'
    if ($null -eq $guid) { throw 'The stable AD objectGUID was not readable.' }
    return @{ type = $Type; namespace = 'ad.objectGUID'; externalId = ([guid]$guid).ToString() }
}

function Get-AdRangedMembers([string]$Identity, [string]$Server) {
    $start = 0
    while ($true) {
        $entry = Get-ADObject -Identity $Identity -Server $Server -Properties "member;range=$start-$($start + 999)" -ErrorAction Stop
        $ranges = @($entry.PSObject.Properties | Where-Object { $_.Name -match '^member;range=\d+-(\d+|\*)$' })
        if ($ranges.Count -eq 0) {
            if ($start -ne 0) { throw 'The directory did not return the requested membership range.' }
            if ($null -eq $entry.PSObject.Properties['member']) { throw 'Membership values were not returned; verify attribute read permissions.' }
            foreach ($member in @(Get-AdValue $entry 'member')) { if ($null -ne $member) { [string]$member } }
            return
        }
        if ($ranges.Count -ne 1 -or $ranges[0].Name -notmatch "^member;range=$start-(\d+|\*)$") {
            throw 'The directory returned an inconsistent membership range.'
        }
        $last = $Matches[1]
        foreach ($member in @($ranges[0].Value)) { [string]$member }
        if ($last -eq '*') { return }
        $end = [int]$last
        if ($end -lt $start) { throw 'The directory membership range did not advance.' }
        $start = $end + 1
    }
}

function Invoke-AdDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    if ($env:OS -ne 'Windows_NT') { throw 'ActiveDirectory requires an authorized Windows workstation with RSAT. No installation was performed.' }
    Assert-DiscoveryPrerequisites -MinimumPowerShell '5.1' -Modules @('ActiveDirectory')
    Import-Module ActiveDirectory -ErrorAction Stop
    $request = Read-DiscoveryParameters -Path $ParameterFile
    if ($request.environmentKind -ne 'AdForest' -or $request.scope.kind -notin @('domain', 'ou') -or $request.producerVersion -ne '1.0.0') {
        throw 'Use an AD 1.0.0 request with an explicit domain or OU scope.'
    }
    $domainName = if ($request.scope.kind -eq 'domain') { [string]$request.scope.value } else { [string](Get-AdValue $request 'domain') }
    if ([string]::IsNullOrWhiteSpace($domainName)) { throw 'An OU request requires its expected domain in the separate parameter file.' }
    $domain = Get-ADDomain -Server $domainName -ErrorAction Stop
    $forest = Get-ADForest -Server $domain.DNSRoot -ErrorAction Stop
    if ($domain.DNSRoot -ine $domainName -or $forest.Name -ine $request.expectedEnvironmentExternalId -or $domain.Forest -ine $forest.Name) {
        throw 'The observed forest/domain does not match the request. Select the correct local AD session and request.'
    }
    $base = [string]$domain.DistinguishedName
    if ($request.scope.kind -eq 'ou') {
        $ou = Get-ADOrganizationalUnit -Identity $request.scope.value -Server $domain.DNSRoot -ErrorAction Stop
        if ($ou.DistinguishedName -ine $request.scope.value -or
            -not $ou.DistinguishedName.EndsWith(",$base", [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The requested OU is outside the observed domain.'
        }
        $base = [string]$ou.DistinguishedName
    }
    [void](Get-ADObject -Identity $base -Server $domain.DNSRoot -ErrorAction Stop)
    $definitions = @{
        'ad.users' = @{ Command = 'Get-ADUser'; Type = 'user'; Fields = @('displayName','distinguishedName','sAMAccountName','userPrincipalName','mail','proxyAddresses','objectSid','msDSConsistencyGuid','primaryGroupId','enabled','whenChanged'); Relations = @('primaryMemberOf') }
        'ad.computers' = @{ Command = 'Get-ADComputer'; Type = 'computer'; Fields = @('displayName','distinguishedName','sAMAccountName','dnsHostName','objectSid','msDSConsistencyGuid','primaryGroupId','enabled','operatingSystem','whenChanged'); Relations = @('primaryMemberOf') }
        'ad.groups' = @{ Command = 'Get-ADGroup'; Type = 'group'; Fields = @('displayName','distinguishedName','sAMAccountName','mail','proxyAddresses','objectSid','groupScope','groupCategory','whenChanged'); Relations = @('memberOf') }
        'ad.ous' = @{ Command = 'Get-ADOrganizationalUnit'; Type = 'organizationalUnit'; Fields = @('displayName','distinguishedName','whenChanged'); Relations = @() }
    }
    if (@($request.sections | Where-Object { -not $definitions.ContainsKey($_) }).Count -gt 0 -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) { throw 'Unknown or duplicate AD sections.' }
    $version = (Get-Module ActiveDirectory).Version.ToString()
    $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.ad' -ProducerVersion '1.0.0' `
        -EnvironmentExternalId $forest.Name -ModuleVersions @{ ActiveDirectory = $version; PowerShell = $PSVersionTable.PSVersion.ToString() } `
        -SourceId $domain.DNSRoot
    foreach ($capability in $request.sections) {
        $definition = $definitions[$capability]
        Start-DiscoverySection -Package $package -CapabilityId $capability -Scope $request.scope `
            -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
        $warnings = New-Object 'Collections.Generic.HashSet[string]'
        try {
            $properties = @($definition.Fields | ForEach-Object {
                switch ($_) { 'msDSConsistencyGuid' { 'mS-DS-ConsistencyGuid' }; 'objectSid' { 'objectSid' }; default { $_ } }
            })
            & $definition.Command -Filter '*' -SearchBase $base -Server $domain.DNSRoot -ResultPageSize 500 -Properties $properties -ErrorAction Stop |
                ForEach-Object {
                    $item = $_
                    $reference = Get-AdReference $item $definition.Type
                    $attributes = @{}
                    foreach ($field in $definition.Fields) {
                        $name = if ($field -eq 'msDSConsistencyGuid') { 'mS-DS-ConsistencyGuid' } elseif ($field -eq 'objectSid') { 'SID' } else { $field }
                        $property = $item.PSObject.Properties[$name]
                        if ($null -eq $property -and $field -eq 'objectSid') { $property = $item.PSObject.Properties['objectSid'] }
                        if ($null -eq $property) { [void]$warnings.Add('MissingAttributes'); continue }
                        $value = $property.Value
                        if ($field -eq 'objectSid') { $value = Get-AdSid $item }
                        elseif ($field -eq 'msDSConsistencyGuid' -and $null -ne $value) { $value = [Convert]::ToBase64String([byte[]]$value) }
                        elseif ($field -eq 'whenChanged' -and $null -ne $value) { $value = ([datetime]$value).ToUniversalTime().ToString('o') }
                        elseif ($field -eq 'proxyAddresses') { $value = [string[]]@($value | Where-Object { $null -ne $_ }) }
                        elseif ($field -in @('primaryGroupId','groupScope','groupCategory') -and $null -ne $value) { $value = [string]$value }
                        $attributes[$field] = $value
                    }
                    $sid = Get-AdSid $item
                    $alternatives = @()
                    if ($sid) { $alternatives += @{ type = $definition.Type; namespace = 'ad.objectSid'; externalId = $sid } }
                    Write-DiscoveryObject -Package $package -Type $definition.Type -Namespace $reference.namespace -ExternalId $reference.externalId `
                        -Attributes $attributes -AlternativeKeys $alternatives
                    $primaryGroup = Get-AdValue $item 'primaryGroupID'
                    if ($definition.Type -in @('user', 'computer') -and $sid -and $null -ne $primaryGroup) {
                        $groupSid = $sid.Substring(0, $sid.LastIndexOf('-') + 1) + [string]$primaryGroup
                        Write-DiscoveryRelationship -Package $package -RelationType primaryMemberOf -From $reference `
                            -To @{ type = 'group'; namespace = 'ad.objectSid'; externalId = $groupSid } -Properties @{ referenceStatus = 'Unresolved' }
                    }
                    if ($definition.Type -eq 'group') {
                        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
                        Get-AdRangedMembers -Identity $item.DistinguishedName -Server $domain.DNSRoot | ForEach-Object {
                            $memberDn = [string]$_
                            if (-not $seen.Add($memberDn)) { [void]$warnings.Add('ConcurrentMembershipChange'); return }
                            $outside = -not $memberDn.EndsWith(",$base", [StringComparison]::OrdinalIgnoreCase)
                            $memberReference = @{ type = 'directoryObject'; namespace = 'ad.distinguishedName'; externalId = $memberDn }
                            if (-not $outside) {
                                try {
                                    $member = Get-ADObject -Identity $memberDn -Server $domain.DNSRoot -Properties objectGUID,objectClass -ErrorAction Stop
                                    $memberReference = Get-AdReference $member ([string]$member.ObjectClass)
                                } catch { [void]$warnings.Add('UnresolvedMember') }
                            }
                            Write-DiscoveryRelationship -Package $package -RelationType memberOf -From $memberReference -To $reference `
                                -Properties @{ outOfScope = $outside }
                        }
                    }
                }
            $issues = @($warnings | ForEach-Object { @{ code = $_; message = 'Some attributes or references were not fully readable; verify local read rights and scope, then relaunch.' } })
            $status = if ($issues.Count -gt 0) { 'Partial' } else { 'Complete' }
            Complete-DiscoverySection -Package $package -Status $status -Warnings $issues
        } catch {
            Complete-DiscoverySection -Package $package -Status Failed `
                -Errors @(@{ code = 'AdReadFailed'; message = 'Read failed. Verify domain connectivity and read permissions for this section, then relaunch.' })
        }
    }
    Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
}

Export-ModuleMember -Function Invoke-AdDiscovery
