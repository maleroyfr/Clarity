Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

$script:sections = @{
    'azure.subscriptions' = @{ Type = 'azureSubscription'; Fields = @('displayName','status'); Relations = @() }
    'azure.resourceGroups' = @{ Type = 'azureResourceGroup'; Fields = @('displayName','location'); Relations = @('resourceGroupInSubscription') }
    'azure.logAnalyticsWorkspaces' = @{ Type = 'logAnalyticsWorkspace'; Fields = @('displayName','location'); Relations = @('workspaceInResourceGroup') }
}

function Get-AzureModuleVersion([string]$Name, [version]$Required) {
    if ($PSVersionTable.PSVersion -lt [version]'7.4') { throw 'Use PowerShell 7.4 or newer.' }
    $loaded = @(Get-Module -Name $Name)
    if ($loaded.Count -eq 0) {
        try { Import-Module $Name -RequiredVersion $Required -ErrorAction Stop }
        catch { throw "Open an authorized session with $Name $Required installed separately." }
        $loaded = @(Get-Module -Name $Name)
    }
    if ($loaded.Count -ne 1 -or $loaded[0].Version -ne $Required) { throw "Use the documented version of $Name ($Required)." }
    return $loaded[0].Version.ToString()
}

function Invoke-AzureDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    $PSDefaultParameterValues = @{ Disabled = $true }
    $request = Read-DiscoveryParameters $ParameterFile
    $guid = '[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}'
    $groupName = '[a-zA-Z0-9_.()-]{0,89}[a-zA-Z0-9_()-]'
    if ($request.environmentKind -cne 'AzureScope' -or $request.producerVersion -cne '1.0.0' -or $request.scope.kind -cne 'resource' -or
        ($request.PSObject.Properties['domain'] -and $request.domain) -or
        @($request.sections | Where-Object { $_ -cnotin $script:sections.Keys }).Count -or
        @($request.sections | Sort-Object -Unique).Count -ne $request.sections.Count -or
        $request.expectedEnvironmentExternalId -cnotmatch "\A($guid)/subscriptions/($guid)\z") {
        throw 'Download Azure parameters for one declared tenant/subscription.'
    }
    $tenant = $Matches[1]; $sub = $Matches[2]; $root = "/subscriptions/$sub"
    if ([guid]$tenant -eq [guid]::Empty -or [guid]$sub -eq [guid]::Empty -or
        $request.scope.value -notmatch "\A$root(?:/resourceGroups/($groupName))?\z") { throw 'Request one subscription or an exact resource group within it.' }
    $group = $Matches[1]
    $versions = @{ PowerShell = $PSVersionTable.PSVersion.ToString(); 'Az.Accounts' = Get-AzureModuleVersion 'Az.Accounts' '5.5.5' }
    try {
        $context = Get-AzContext -ErrorAction Stop
        if ($context.Environment.Name -ne 'AzureCloud' -or $context.Tenant.Id -ne $tenant -or $context.Subscription.Id -ne $sub) { throw 'Mismatch' }
        $verified = @(Get-AzSubscription -TenantId $tenant -SubscriptionId $sub -DefaultProfile $context -ErrorAction Stop)
        if ($verified.Count -ne 1 -or $verified[0].TenantId -ne $tenant -or $verified[0].Id -ne $sub) { throw 'Mismatch' }
        $subscription = $verified[0]
    } catch { throw 'Cannot verify the requested tenant and subscription in the active Azure public cloud context. No package was created.' }
    if ($group) {
        $versions['Az.Resources'] = Get-AzureModuleVersion 'Az.Resources' '10.2.3'
        try {
            $verified = @(Get-AzResourceGroup -Name $group -DefaultProfile $context -ErrorAction Stop)
            if ($verified.Count -ne 1 -or $verified[0].ResourceId -ne $request.scope.value -or $verified[0].ResourceGroupName -ne $group) { throw 'Mismatch' }
            $verifiedGroup = $verified[0]
        } catch { throw 'Cannot verify the exact requested resource group. No package was created.' }
    }
    $actual = "$($subscription.TenantId)/subscriptions/$($subscription.Id)"
    $package = New-DiscoveryPackage $request 'clarity.azure' '1.0.0' $actual $versions 'Azure Resource Manager (Az)'
    foreach ($id in $request.sections) {
        $section = $script:sections[$id]
        Start-DiscoverySection $package $id $request.scope $section.Fields $section.Relations
        if ($id -eq 'azure.subscriptions' -and $group) {
            Complete-DiscoverySection $package NotApplicable -Warnings @(@{code='AzureParentOutsideScope';message='The parent subscription is outside the requested group.'})
            continue
        }
        $issues = @{}
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $write = {
            $row = $_
            try {
                $parent = $null
                switch ($id) {
                    'azure.subscriptions' {
                        $key = $root; $attrs = @{displayName=[string]$row.Name; status=[string]$row.State}
                    }
                    'azure.resourceGroups' {
                        $name = [string]$row.ResourceGroupName
                        $key = [string]$row.ResourceId
                        if ($name -notmatch "\A$groupName\z" -or $key -ne "$root/resourceGroups/$name" -or ($group -and $name -ne $group)) { throw 'Unverified row' }
                        $parent = @{ type='azureSubscription'; namespace='azure.armId'; externalId=$root.ToLowerInvariant() }
                        $attrs = @{displayName=$name; location=[string]$row.Location}
                    }
                    'azure.logAnalyticsWorkspaces' {
                        $name = [string]$row.Name; $rg = [string]$row.ResourceGroupName; $rgPath = "$root/resourceGroups/$rg"
                        $key = [string]$row.ResourceId
                        if ($name -notmatch '\A[a-zA-Z0-9-]{1,63}\z' -or $rg -notmatch "\A$groupName\z" -or ($group -and $rg -ne $group) -or
                            $key -ne "$rgPath/providers/Microsoft.OperationalInsights/workspaces/$name") { throw 'Unverified row' }
                        $parent = @{ type='azureResourceGroup'; namespace='azure.armId'; externalId=$rgPath.ToLowerInvariant() }
                        $attrs = @{displayName=$name; location=[string]$row.Location}
                    }
                }
                if (@($attrs.Values | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -or !$seen.Add($key)) { throw 'Incomplete row' }
            } catch { $issues.AzureUnreadableRow = 'A row was incomplete, duplicated or outside the verified scope and was omitted.'; return }
            $from = @{ type=$section.Type; namespace='azure.armId'; externalId=$key.ToLowerInvariant() }
            Write-DiscoveryObject $package $from.type $from.namespace $from.externalId $attrs
            if ($parent) { Write-DiscoveryRelationship $package $section.Relations[0] $from $parent }
        }
        try {
            switch ($id) {
                'azure.subscriptions' { $subscription | ForEach-Object $write }
                'azure.resourceGroups' {
                    if ($group) { $verifiedGroup | ForEach-Object $write } else {
                        $versions['Az.Resources'] = Get-AzureModuleVersion 'Az.Resources' '10.2.3'
                        Get-AzResourceGroup -DefaultProfile $context -ErrorAction Stop | ForEach-Object $write
                    }
                }
                'azure.logAnalyticsWorkspaces' {
                    $versions['Az.OperationalInsights'] = Get-AzureModuleVersion 'Az.OperationalInsights' '3.4.1'
                    $argsForScope = @{}
                    if ($group) { $argsForScope.ResourceGroupName = $group }
                    Get-AzOperationalInsightsWorkspace @argsForScope -DefaultProfile $context -ErrorAction Stop | ForEach-Object $write
                }
            }
        } catch { $issues.AzureReadFailed = 'Enumeration did not finish. Verify the documented module version and scoped read rights.' }
        $errors = @($issues.GetEnumerator() | ForEach-Object { @{code=$_.Key;message=$_.Value} })
        $status = if ($errors.Count) { 'Failed' } else { 'Complete' }
        Complete-DiscoverySection $package $status -Errors $errors -Warnings @(@{code='AzureVisibility';message='Only resources visible to this identity were observed. Omission does not imply absence.'})
    }
    Complete-DiscoveryPackage $package $OutputPath
}
Export-ModuleMember -Function Invoke-AzureDiscovery
