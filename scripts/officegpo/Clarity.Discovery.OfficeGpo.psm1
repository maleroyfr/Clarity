Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

function Get-OfficeGpoProperty($Object, [string]$Name, [switch]$AllowNull) {
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) { throw 'Required source property was not returned.' }
    $value = $Object.PSObject.Properties[$Name].Value
    if ($null -eq $value -and -not $AllowNull) { throw 'Required source property was empty.' }
    return $value
}

function Get-OfficeGpoReference($Id) {
    $guid = [guid]$Id
    if ($guid -eq [guid]::Empty) { throw 'The GPO identifier was empty.' }
    return @{ type = 'groupPolicyObject'; namespace = 'ad.gpoId'; externalId = $guid.ToString() }
}

function Get-OfficeGpoBoolean($Value) {
    if ($Value -is [bool]) { return $Value }
    if ([string]$Value -eq 'Yes') { return $true }
    if ([string]$Value -eq 'No') { return $false }
    throw 'The source did not provide a supported boolean value.'
}

function Initialize-OfficeGpoModules {
    if ($env:OS -ne 'Windows_NT') { throw 'Use an authorized Windows machine with RSAT GroupPolicy and ActiveDirectory. No installation was performed.' }
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
        throw 'Run the collector in Windows PowerShell 5.1 (powershell.exe) to use native RSAT property types.'
    }
    Assert-DiscoveryPrerequisites -MinimumPowerShell '5.1' -Modules @('GroupPolicy', 'ActiveDirectory')
    $versions = @{ PowerShell = $PSVersionTable.PSVersion.ToString() }
    foreach ($name in @('GroupPolicy', 'ActiveDirectory')) {
        $version = if ($name -eq 'GroupPolicy') { '1.0.0.0' } else { '1.0.1.0' }
        try { $versions[$name] = (Import-Module -Name $name -RequiredVersion $version -PassThru -ErrorAction Stop).Version.ToString() }
        catch { throw "Cannot load $name $version. Install RSAT separately and use the documented local Windows session." }
    }
    return $versions
}

function Write-OfficeGpoSettings($Package, $Gpo, [string]$Domain, [string]$Server, $Errors) {
    $reference = Get-OfficeGpoReference $Gpo.Id
    $readAnyKey = $false
    foreach ($application in @('word', 'excel', 'powerpoint')) {
        $key = "HKEY_CURRENT_USER\Software\Policies\Microsoft\office\16.0\$application\security"
        $keyComplete = $true
        try {
            Get-GPRegistryValue -Guid $Gpo.Id -Key $key -Domain $Domain -Server $Server -ErrorAction Stop | ForEach-Object {
                $row = $_
                try {
                    $fullKey = [string](Get-OfficeGpoProperty $row 'FullKeyPath')
                    # A key-only read may also return first-level subkey descriptors.
                    if ($fullKey.StartsWith("$key\", [StringComparison]::OrdinalIgnoreCase)) { return }
                    if ($fullKey -ine $key) { throw 'Registry result is outside the selected key.' }
                    $name = [string](Get-OfficeGpoProperty $row 'ValueName' -AllowNull)
                    if ($name -notin @('VBAWarnings', 'blockcontentexecutionfrominternet')) { return }
                    $state = [string](Get-OfficeGpoProperty $row 'PolicyState')
                    $type = [string](Get-OfficeGpoProperty $row 'Type')
                    if ((Get-OfficeGpoProperty $row 'HasValue') -ne $true -or
                        $state -notin @('Set', 'Delete') -or $type -ne 'DWord') { throw 'Unsupported registry setting shape.' }
                    $value = $null
                    if ($state -eq 'Set') {
                        $raw = Get-OfficeGpoProperty $row 'Value'
                        if ($raw -isnot [int] -and $raw -isnot [uint32] -and $raw -isnot [long]) { throw 'A DWORD value was not returned.' }
                        $value = [long]$raw
                        if ($value -lt 0 -or $value -gt [uint32]::MaxValue) { throw 'Invalid DWORD value.' }
                    }
                    $valueName = $name.ToLowerInvariant()
                    $id = "$($reference.externalId)|$application|$valueName"
                    Write-DiscoveryObject -Package $Package -Type officePolicySetting -Namespace 'clarity.officeGpo.setting' -ExternalId $id `
                        -Attributes @{ displayName = "$application : $valueName"; application = $application; configuration = 'User'; officeVersion = '16.0'
                            registryKey = $key; valueName = $valueName; valueType = $type; policyState = $state; integerValue = $value }
                    Write-DiscoveryRelationship -Package $Package -RelationType officeSettingInGpo `
                        -From @{ type = 'officePolicySetting'; namespace = 'clarity.officeGpo.setting'; externalId = $id } -To $reference
                } catch { $keyComplete = $false; [void]$Errors.Add('OfficeRegistryShapeUnsupported') }
            }
            if ($keyComplete) { $readAnyKey = $true }
        } catch { [void]$Errors.Add('OfficeRegistryReadFailed') }
    }
    return $readAnyKey
}

function Invoke-OfficeGpoDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    $request = Read-DiscoveryParameters -Path $ParameterFile
    $definitions = @{
        'officeGpo.policies' = @{ Fields = @('displayName','domainName','gpoStatus','creationTime','modificationTime','hasWmiFilter'); Relations = @() }
        'officeGpo.links' = @{ Fields = @(); Relations = @('gpoLinkedToOu') }
        'officeGpo.securityFiltering' = @{ Fields = @(); Relations = @('gpoSecurityFilter','gpoPermission') }
        'officeGpo.officeSettings' = @{ Fields = @('displayName','application','configuration','officeVersion','registryKey','valueName','valueType','policyState','integerValue'); Relations = @('officeSettingInGpo') }
    }
    if ($request.environmentKind -ne 'AdForest' -or $request.scope.kind -notin @('domain','ou') -or $request.producerVersion -ne '1.0.0' -or
        @($request.sections | Where-Object { -not $definitions.ContainsKey($_) }).Count -gt 0 -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) {
        throw 'Use an Office/GPO 1.0.0 request with distinct supported sections and an explicit domain or OU scope.'
    }
    $domainName = if ($request.scope.kind -eq 'domain') { [string]$request.scope.value } else { [string](Get-OfficeGpoProperty $request 'domain') }
    if ([string]::IsNullOrWhiteSpace($domainName)) { throw 'An OU request requires its expected DNS domain.' }
    $versions = Initialize-OfficeGpoModules
    try {
        $domain = Get-ADDomain -Server $domainName -ErrorAction Stop
        $forest = Get-ADForest -Server $domain.DNSRoot -ErrorAction Stop
        if ($domain.DNSRoot -ine $domainName -or $forest.Name -ine $request.expectedEnvironmentExternalId -or $domain.Forest -ine $forest.Name) {
            throw 'Environment mismatch.'
        }
        $server = [string](Get-OfficeGpoProperty $domain 'PDCEmulator')
        $base = [string](Get-OfficeGpoProperty $domain 'DistinguishedName')
        $rootOu = $null
        if ($request.scope.kind -eq 'ou') {
            $rootOu = Get-ADOrganizationalUnit -Identity $request.scope.value -Server $server -ErrorAction Stop
            if ($rootOu.DistinguishedName -ine $request.scope.value -or
                -not $rootOu.DistinguishedName.EndsWith(",$base", [StringComparison]::OrdinalIgnoreCase)) { throw 'OU mismatch.' }
            $base = [string]$rootOu.DistinguishedName
        }
    } catch { throw 'The requested forest/domain/OU could not be verified. Check local read rights, connectivity and the selected environment.' }

    $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.officegpo' -ProducerVersion '1.0.0' `
        -EnvironmentExternalId $forest.Name -SourceId $server -ModuleVersions $versions
    $ous = @{}
    $readEmptyOu = $false
    $links = New-Object 'Collections.Generic.List[object]'
    $linkErrors = New-Object 'Collections.Generic.HashSet[string]'
    $selectionErrors = New-Object 'Collections.Generic.HashSet[string]'
    $gpos = @{}
    if ($request.scope.kind -eq 'ou' -or 'officeGpo.links' -in $request.sections) {
        if ($null -ne $rootOu) { $ous[$rootOu.DistinguishedName] = $rootOu }
        try {
            Get-ADOrganizationalUnit -Filter '*' -SearchBase $base -SearchScope Subtree -Server $server -ResultPageSize 500 -ErrorAction Stop |
                ForEach-Object {
                    $dn = [string](Get-OfficeGpoProperty $_ 'DistinguishedName')
                    if ($dn -ine $base -and -not $dn.EndsWith(",$base", [StringComparison]::OrdinalIgnoreCase)) { throw 'Out-of-scope OU.' }
                    $ous[$dn] = $_
                }
        } catch { [void]$linkErrors.Add('OuEnumerationFailed') }
        foreach ($ou in $ous.Values) {
            try {
                $ouId = [guid](Get-OfficeGpoProperty $ou 'ObjectGUID')
                if ($ouId -eq [guid]::Empty) { throw 'Missing OU identity.' }
                $inheritance = Get-GPInheritance -Target $ou.DistinguishedName -Domain $domainName -Server $server -ErrorAction Stop
                if ((Get-OfficeGpoProperty $inheritance 'Path') -ine $ou.DistinguishedName) { throw 'Link target mismatch.' }
                $blocked = Get-OfficeGpoBoolean (Get-OfficeGpoProperty $inheritance 'GpoInheritanceBlocked')
                $directLinks = @(Get-OfficeGpoProperty $inheritance 'GpoLinks')
                if ($directLinks.Count -eq 0) { $readEmptyOu = $true }
                foreach ($link in $directLinks) {
                    try {
                        $from = Get-OfficeGpoReference (Get-OfficeGpoProperty $link 'GpoId')
                        if ((Get-OfficeGpoProperty $link 'Target') -ine $ou.DistinguishedName) { throw 'Direct link target mismatch.' }
                        $order = Get-OfficeGpoProperty $link 'Order'
                        if ($order -isnot [int] -or $order -lt 1) { throw 'Unsupported link order.' }
                        $links.Add(@{
                            From = $from; To = @{ type = 'organizationalUnit'; namespace = 'ad.objectGUID'; externalId = $ouId.ToString() }
                            Properties = @{ enabled = (Get-OfficeGpoBoolean (Get-OfficeGpoProperty $link 'Enabled'))
                                enforced = (Get-OfficeGpoBoolean (Get-OfficeGpoProperty $link 'Enforced')); order = $order
                                targetDistinguishedName = [string]$ou.DistinguishedName; inheritanceBlocked = $blocked }
                        })
                    } catch { [void]$linkErrors.Add('LinkShapeUnsupported') }
                }
            } catch { [void]$linkErrors.Add('OuLinksReadFailed') }
        }
    }
    if (@($request.sections | Where-Object { $_ -ne 'officeGpo.links' }).Count -gt 0) {
        if ($request.scope.kind -eq 'domain') {
            try {
                Get-GPO -All -Domain $domainName -Server $server -ErrorAction Stop | ForEach-Object {
                    $reference = Get-OfficeGpoReference (Get-OfficeGpoProperty $_ 'Id')
                    if ((Get-OfficeGpoProperty $_ 'DomainName') -ine $domainName) { throw 'GPO domain mismatch.' }
                    $gpos[$reference.externalId] = $_
                }
            } catch { [void]$selectionErrors.Add('GpoEnumerationFailed') }
        } else {
            foreach ($errorCode in $linkErrors) { [void]$selectionErrors.Add($errorCode) }
            foreach ($id in @($links | ForEach-Object { $_.From.externalId } | Select-Object -Unique)) {
                try {
                    $gpo = Get-GPO -Guid $id -Domain $domainName -Server $server -ErrorAction Stop
                    if ((Get-OfficeGpoReference $gpo.Id).externalId -ne $id -or $gpo.DomainName -ine $domainName) { throw 'GPO identity mismatch.' }
                    $gpos[$id] = $gpo
                } catch { [void]$selectionErrors.Add('LinkedGpoReadFailed') }
            }
        }
    }
    foreach ($capability in $request.sections) {
        $definition = $definitions[$capability]
        Start-DiscoverySection -Package $package -CapabilityId $capability -Scope $request.scope `
            -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
        $errors = New-Object 'Collections.Generic.HashSet[string]'
        $readEmptyUnit = $readEmptyOu -and ($capability -eq 'officeGpo.links' -or $request.scope.kind -eq 'ou')
        $sourceErrors = if ($capability -eq 'officeGpo.links') { $linkErrors } else { $selectionErrors }
        foreach ($code in $sourceErrors) { [void]$errors.Add($code) }
        $warnings = @(@{ code = 'VisibilityLimited'; message = 'Coverage is limited to readable objects and declared fields. Hidden ACL-filtered objects, inherited/site links and effective policy are not established.' })
        if ($capability -eq 'officeGpo.links') {
            foreach ($link in $links) {
                Write-DiscoveryRelationship -Package $package -RelationType gpoLinkedToOu -From $link.From -To $link.To -Properties $link.Properties
            }
        } else {
            foreach ($gpo in $gpos.Values) {
                try {
                    $reference = Get-OfficeGpoReference $gpo.Id
                    switch ($capability) {
                        'officeGpo.policies' {
                            $attributes = @{
                                displayName = [string](Get-OfficeGpoProperty $gpo 'DisplayName')
                                domainName = [string](Get-OfficeGpoProperty $gpo 'DomainName')
                                gpoStatus = [string](Get-OfficeGpoProperty $gpo 'GpoStatus')
                                creationTime = ([datetime](Get-OfficeGpoProperty $gpo 'CreationTime')).ToUniversalTime().ToString('o')
                                modificationTime = ([datetime](Get-OfficeGpoProperty $gpo 'ModificationTime')).ToUniversalTime().ToString('o')
                                hasWmiFilter = $null -ne (Get-OfficeGpoProperty $gpo 'WmiFilter' -AllowNull)
                            }
                            Write-DiscoveryObject -Package $package -Type $reference.type -Namespace $reference.namespace -ExternalId $reference.externalId -Attributes $attributes
                        }
                        'officeGpo.securityFiltering' {
                            $sawPermission = $false
                            Get-GPPermission -Guid $gpo.Id -All -DomainName $domainName -Server $server -ErrorAction Stop | ForEach-Object {
                                $sawPermission = $true
                                try {
                                    $permission = [string](Get-OfficeGpoProperty $_ 'Permission')
                                    $trustee = Get-OfficeGpoProperty $_ 'Trustee'
                                    $sid = Get-OfficeGpoProperty $trustee 'Sid'
                                    if ($null -ne $sid.PSObject.Properties['Value']) { $sid = $sid.Value }
                                    if ([string]$sid -notmatch '^S-\d+(-\d+)+$') { throw 'Missing trustee SID.' }
                                    $trusteeType = [string](Get-OfficeGpoProperty $trustee 'SidType')
                                    $type = switch ($trusteeType) {
                                        'User' { 'user' }; 'Computer' { 'computer' }
                                        { $_ -in @('Group','Alias','WellKnownGroup') } { 'group' }
                                        default { 'directoryObject' }
                                    }
                                    if ($permission -notin @('GpoApply','GpoRead','GpoEdit','GpoEditDeleteModifySecurity','GpoCustom','GpoNoPermissions')) {
                                        throw 'Unsupported permission summary.'
                                    }
                                    if ($permission -eq 'GpoCustom') { [void]$errors.Add('CustomSecurityDescriptorNotEvaluated') }
                                    $relation = if ($permission -eq 'GpoApply') { 'gpoSecurityFilter' } else { 'gpoPermission' }
                                    Write-DiscoveryRelationship -Package $package -RelationType $relation -From $reference `
                                        -To @{ type = $type; namespace = 'ad.objectSid'; externalId = [string]$sid } `
                                        -Properties @{ permission = $permission; trusteeType = $trusteeType
                                            inherited = (Get-OfficeGpoBoolean (Get-OfficeGpoProperty $_ 'Inherited')) }
                                } catch { [void]$errors.Add('PermissionShapeUnsupported') }
                            }
                            if (-not $sawPermission) { $readEmptyUnit = $true }
                        }
                        'officeGpo.officeSettings' {
                            if (Write-OfficeGpoSettings $package $gpo $domainName $server $errors) { $readEmptyUnit = $true }
                        }
                    }
                } catch { [void]$errors.Add('GpoSectionReadFailed') }
            }
        }
        $issues = @($errors | ForEach-Object { @{ code = $_; message = 'Some requested metadata could not be read or interpreted. Check RSAT versions, read permissions and connectivity, then rerun. No absence or effective policy is inferred.' } })
        $status = if ($issues.Count -eq 0) { 'Complete' } elseif ($readEmptyUnit -or $package.ActiveSection.objectCount -gt 0 -or $package.ActiveSection.relationshipCount -gt 0) { 'Partial' } else { 'Failed' }
        Complete-DiscoverySection -Package $package -Status $status -Warnings $warnings -Errors $issues
    }
    Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
}

Export-ModuleMember -Function Invoke-OfficeGpoDiscovery
