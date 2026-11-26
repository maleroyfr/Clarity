Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

$script:sections = @{
    'infrastructure.servers' = @{ Type='windowsServer'; Fields=@('displayName','operatingSystem','domainName'); Relations=@() }
    'infrastructure.dnsZones' = @{ Type='dnsZone'; Fields=@('displayName','zoneType'); Relations=@('dnsZoneOnServer')
        Query=@{Namespace='root\MicrosoftDNS';ClassName='MicrosoftDNS_Zone';Property=@('Name','ZoneType')} }
    'infrastructure.shares' = @{ Type='windowsShare'; Fields=@('displayName','shareType'); Relations=@('shareOnServer')
        Query=@{ClassName='Win32_Share';Property=@('Name','Type')} }
    'infrastructure.printers' = @{ Type='printQueue'; Fields=@('displayName'); Relations=@('printerOnServer')
        Query=@{ClassName='Win32_Printer';Property=@('Name');Filter='Local = TRUE AND Network = FALSE'} }
    'infrastructure.services' = @{ Type='windowsService'; Fields=@('displayName','status'); Relations=@('serviceOnServer')
        Query=@{ClassName='Win32_Service';Property=@('Name','State')} }
    'infrastructure.certificates' = @{ Type='machineCertificate'; Fields=@('displayName','notBefore','notAfter'); Relations=@('certificateOnServer') }
    'infrastructure.serviceDependencies' = @{ Fields=@(); Relations=@('serviceDependsOnService')
        Query=@{ClassName='Win32_DependentService';Property=@('Dependent','Antecedent','TypeOfDependency')} }
    'infrastructure.applicationPools' = @{ Type='iisApplicationPool'; Fields=@('displayName'); Relations=@('poolOnServer')
        Query=@{Namespace='root\WebAdministration';ClassName='ApplicationPool';Property=@('Name')} }
    'infrastructure.applications' = @{ Type='iisApplication'; Fields=@('displayName','siteName','virtualPath'); Relations=@('applicationOnServer','applicationUsesPool')
        Query=@{Namespace='root\WebAdministration';ClassName='Application';Property=@('SiteName','Path','ApplicationPool')} }
}

function Get-InfrastructureVersions {
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'5.1' -or ![Environment]::Is64BitProcess) {
        throw 'Run locally in native 64-bit Windows PowerShell 5.1 on the declared Windows Server.'
    }
    $cim = Get-Command Get-CimInstance -Module CimCmdlets -ErrorAction Stop
    return @{PowerShell=$PSVersionTable.PSVersion.ToString();CimCmdlets=$cim.Version.ToString()}
}

function Get-LocalDnsIdentity { [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties() }

function New-HostReference([string]$Server, [string]$Type, [string[]]$Parts) {
    $key = $Server
    foreach ($part in $Parts) {
        if ([string]::IsNullOrWhiteSpace($part)) { throw 'Missing source identity.' }
        $key += '/' + [Uri]::EscapeDataString($part)
    }
    if ($key.Length -gt 500) { throw 'Identity exceeds the ZIP contract limit.' }
    return @{type=$Type;namespace='windows.hostResource';externalId=$key}
}

function Invoke-InfrastructureDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    # Do not inherit a profile's default -ComputerName or -CimSession.
    $PSDefaultParameterValues = @{ Disabled = $true }
    $request = Read-DiscoveryParameters $ParameterFile
    $label = '[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?'
    if ($request.environmentKind -cne 'Other' -or $request.producerVersion -cne '1.0.0' -or $request.scope.kind -cne 'resource' -or
        ($request.PSObject.Properties['domain'] -and $request.domain) -or $request.scope.value.Length -gt 253 -or
        $request.expectedEnvironmentExternalId -notmatch "\A$label(?:\.$label)*\z" -or
        $request.scope.value -notmatch "\A$label\.$([regex]::Escape($request.expectedEnvironmentExternalId))\z" -or
        @($request.sections | Where-Object { $_ -cnotin $script:sections.Keys }).Count -or
        @($request.sections | Sort-Object -Unique).Count -ne $request.sections.Count) { throw 'Request one explicit local server FQDN in its declared DNS domain.' }
    $versions = Get-InfrastructureVersions
    try {
        $computers = @(Get-CimInstance -ClassName Win32_ComputerSystem -Property DNSHostName,Domain,PartOfDomain -ErrorAction Stop)
        $systems = @(Get-CimInstance -ClassName Win32_OperatingSystem -Property ProductType,Version,Caption -ErrorAction Stop)
        $dns = Get-LocalDnsIdentity
        $computer = $computers[0]; $os = $systems[0]
        $actualDomain = ([string]$computer.Domain).ToLowerInvariant()
        $server = ("{0}.{1}" -f $dns.HostName,$dns.DomainName).ToLowerInvariant()
        if ($computers.Count -ne 1 -or $systems.Count -ne 1 -or $computer.PartOfDomain -ne $true -or
            $os.ProductType -notin @(2,3) -or [version]$os.Version -lt [version]'10.0.17763' -or
            $computer.DNSHostName -ne $dns.HostName -or $actualDomain -ne $dns.DomainName -or
            $actualDomain -ne $request.expectedEnvironmentExternalId -or $server -ne $request.scope.value) { throw 'Mismatch' }
    } catch { throw 'Cannot verify the local server, operating system and joined DNS domain. No package was created.' }
    $serverRef = New-HostReference $server 'windowsServer' @()
    $package = New-DiscoveryPackage $request 'clarity.infrastructure' '1.0.0' $actualDomain $versions 'Local Windows Server metadata'
    foreach ($id in $request.sections) {
        $section = $script:sections[$id]
        Start-DiscoverySection $package $id $request.scope $section.Fields $section.Relations
        $issues = @{}
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $write = {
            $row = $_
            try {
                if ($id -eq 'infrastructure.serviceDependencies') {
                    $from = New-HostReference $server 'windowsService' @($row.Dependent.Name.ToLowerInvariant())
                    $to = New-HostReference $server 'windowsService' @($row.Antecedent.Name.ToLowerInvariant())
                    if ($null -eq $row.TypeOfDependency -or !$seen.Add("$($from.externalId) $($to.externalId)")) { throw 'Missing type or duplicate dependency.' }
                    $properties = @{dependencyType=[int]$row.TypeOfDependency}
                } else {
                    $parts = @(); $pool = $null
                    switch ($id) {
                        'infrastructure.servers' { $attrs = @{ displayName=$server; operatingSystem=$os.Caption; domainName=$actualDomain } }
                        'infrastructure.certificates' {
                            if ($row.Thumbprint -notmatch '\A[0-9a-fA-F]{40}\z') { throw 'Missing thumbprint.' }
                            $parts = @('my',$row.Thumbprint.ToUpperInvariant())
                            $attrs = @{displayName=$parts[1];notBefore=$row.NotBefore.ToUniversalTime().ToString('o');notAfter=$row.NotAfter.ToUniversalTime().ToString('o')}
                        }
                        'infrastructure.applications' {
                            $parts = @($row.SiteName,$row.Path)
                            $attrs = @{displayName="$($row.SiteName)$($row.Path)";siteName=$row.SiteName;virtualPath=$row.Path}
                            try { $pool = New-HostReference $server 'iisApplicationPool' @($row.ApplicationPool) }
                            catch { $issues.InfrastructureMissingEndpoint = 'An application has no usable reported pool reference.' }
                        }
                        default {
                            $name = [string]$row.Name
                            $parts = if ($id -eq 'infrastructure.applicationPools') { @($name) } else { @($name.ToLowerInvariant()) }
                            $attrs = @{displayName=$name}
                            switch ($id) {
                                'infrastructure.dnsZones' { $attrs.zoneType = $row.ZoneType }
                                'infrastructure.shares' { $attrs.shareType = $row.Type }
                                'infrastructure.services' { $attrs.status = $row.State }
                            }
                        }
                    }
                    $from = New-HostReference $server $section.Type $parts
                    if (@($attrs.Values | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -or !$seen.Add($from.externalId)) { throw 'Invalid or duplicate row.' }
                }
            } catch { $issues.InfrastructureUnreadableRow = 'An incomplete or duplicated provider row was omitted.'; return }
            if ($id -eq 'infrastructure.serviceDependencies') {
                Write-DiscoveryRelationship $package 'serviceDependsOnService' $from $to $properties
            } else {
                Write-DiscoveryObject $package $from.type $from.namespace $from.externalId $attrs
                if ($section.Relations.Count) { Write-DiscoveryRelationship $package $section.Relations[0] $from $serverRef }
                if ($pool) { Write-DiscoveryRelationship $package 'applicationUsesPool' $from $pool }
            }
        }
        try {
            switch ($id) {
                'infrastructure.servers' { $computer | ForEach-Object $write }
                'infrastructure.certificates' {
                    Get-ChildItem -LiteralPath 'Microsoft.PowerShell.Security\Certificate::LocalMachine\My' -ErrorAction Stop | ForEach-Object $write
                }
                default {
                    $query = $section.Query
                    Get-CimInstance @query -ErrorAction Stop | ForEach-Object $write
                }
            }
        } catch { $issues.InfrastructureReadFailed = 'The local provider or store is unavailable, denied, or did not finish. Verify prerequisites and read access.' }
        $errors = @($issues.GetEnumerator() | ForEach-Object { @{code=$_.Key;message=$_.Value} })
        $status = if ($errors.Count) { 'Failed' } else { 'Complete' }
        Complete-DiscoverySection $package $status -Errors $errors -Warnings @(@{code='InfrastructureVisibility';message='Only locally readable metadata is covered. Omission does not establish absence.'})
    }
    Complete-DiscoveryPackage $package $OutputPath
}
Export-ModuleMember -Function Invoke-InfrastructureDiscovery
