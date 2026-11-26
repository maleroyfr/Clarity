BeforeAll {
    function global:Get-AzContext { [CmdletBinding()]param() throw 'Unmocked read' }
    function global:Get-AzSubscription { [CmdletBinding()]param($TenantId,$SubscriptionId,$DefaultProfile) throw 'Unmocked read' }
    function global:Get-AzResourceGroup { [CmdletBinding()]param($Name,$DefaultProfile) throw 'Unmocked read' }
    function global:Get-AzOperationalInsightsWorkspace { [CmdletBinding()]param($ResourceGroupName,$DefaultProfile) throw 'Unmocked read' }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Azure.psm1') -Force
    . (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'tests/Read-CollectorFixture.ps1')
    function Invoke-AzFixture($Name) {
        $data | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $TestDrive 'parameters.json')
        $path = Invoke-AzureDiscovery (Join-Path $TestDrive 'parameters.json') (Join-Path $TestDrive "$Name.zip")
        Read-CollectorFixture $path "azure-$Name"
    }
}
AfterAll {
    foreach ($cmd in @('Get-AzContext','Get-AzSubscription','Get-AzResourceGroup','Get-AzOperationalInsightsWorkspace')) {
        Remove-Item -LiteralPath "Function:\$cmd" -ErrorAction SilentlyContinue
    }
}
Describe 'Azure control-plane inventory' {
    BeforeEach {
        $data = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        Mock Get-AzureModuleVersion -ModuleName Clarity.Discovery.Azure { "$Required" }
        Mock Get-AzContext -ModuleName Clarity.Discovery.Azure {
            @{ Environment = @{ Name = 'AzureCloud' }; Tenant = @{ Id = '55555555-0000-0000-0000-000000000001' }
                Subscription = @{ Id = '55555555-0000-0000-0000-000000000002' } }
        }
        Mock Get-AzSubscription -ModuleName Clarity.Discovery.Azure {
            @{ TenantId = $TenantId; Id = $SubscriptionId; Name = 'Synthetic subscription'; State = 'Enabled' }
        }
        Mock Get-AzResourceGroup -ModuleName Clarity.Discovery.Azure {
            @{ ResourceId = "/subscriptions/$($DefaultProfile.Subscription.Id)/resourceGroups/Pilot"; ResourceGroupName = 'Pilot'; Location = 'westus' }
        }
        Mock Get-AzOperationalInsightsWorkspace -ModuleName Clarity.Discovery.Azure {
            @{ ResourceId = "/subscriptions/$($DefaultProfile.Subscription.Id)/resourceGroups/Pilot/providers/Microsoft.OperationalInsights/workspaces/Synthetic"
                ResourceGroupName = 'Pilot'; Name = 'Synthetic'; Location = 'westus'; Tags = @{ internal = 'DO-NOT-EXPORT' } }
        }
    }
    It 'publishes only the verified subscription, readable groups and workspace metadata with containment links' {
        $zip = Invoke-AzFixture complete
        @($zip['objects.jsonl']).Count | Should -Be 3
        @($zip['relationships.jsonl'].relationType) | Should -Be @('resourceGroupInSubscription','workspaceInResourceGroup')
        @($zip['results.json'] | Where-Object { $_.status -ne 'Complete' -or !$_.warnings }).Count | Should -Be 0
        ($zip | ConvertTo-Json -Depth 20) | Should -Not -Match 'DO-NOT-EXPORT'
    }
    It 'verifies a single group by name and never lists outside that group' {
        $data.scope.value += '/resourceGroups/Pilot'
        $zip = Invoke-AzFixture group
        ($zip['results.json'] | Where-Object capabilityId -eq 'azure.subscriptions').status | Should -Be NotApplicable
        $zip['objects.jsonl'].Count | Should -Be 2
        Should -Invoke Get-AzResourceGroup -ModuleName Clarity.Discovery.Azure -Times 1 -Exactly -ParameterFilter { $Name -eq 'Pilot' -and $DefaultProfile }
        Should -Invoke Get-AzOperationalInsightsWorkspace -ModuleName Clarity.Discovery.Azure -Times 1 -Exactly -ParameterFilter { $ResourceGroupName -eq 'Pilot' -and $DefaultProfile }
    }
    It 'rejects stale context and unverifiable group before creating output' {
        $data.scope.value += '/resourceGroups/Other'
        { Invoke-AzFixture wrongGroup } | Should -Throw '*exact requested resource group*'
        $data = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        Mock Get-AzContext -ModuleName Clarity.Discovery.Azure { @{ Tenant=@{Id='other'}; Subscription=@{Id='other'}; Environment=@{Name='AzureCloud'} } }
        { Invoke-AzFixture wrongContext } | Should -Throw '*active Azure public cloud context*'
        Should -Invoke Get-AzOperationalInsightsWorkspace -ModuleName Clarity.Discovery.Azure -Times 0 -Exactly
        Test-Path (Join-Path $TestDrive 'wrongContext.zip') | Should -BeFalse
    }
    It 'retains streamed groups on interruption and never represents a denied workspace list as zero' {
        $data.requestId = '55555555-0000-0000-0000-000000000020'
        Mock Get-AzResourceGroup -ModuleName Clarity.Discovery.Azure {
            @{ ResourceId = "/subscriptions/$($DefaultProfile.Subscription.Id)/resourceGroups/Pilot"; ResourceGroupName = 'Pilot'; Location = 'westus' }
            throw 'DO-NOT-EXPORT'
        }
        Mock Get-AzOperationalInsightsWorkspace -ModuleName Clarity.Discovery.Azure { throw 'DO-NOT-EXPORT' }
        $zip = Invoke-AzFixture partial
        $groups = $zip['results.json'] | Where-Object capabilityId -eq 'azure.resourceGroups'
        $groups.status | Should -Be Partial
        $groups.objectCount | Should -Be 1
        $ws = $zip['results.json'] | Where-Object capabilityId -eq 'azure.logAnalyticsWorkspaces'
        $ws.status | Should -Be Failed
        $ws.objectCount | Should -BeNullOrEmpty
        ($zip | ConvertTo-Json -Depth 20) | Should -Not -Match 'DO-NOT-EXPORT'
    }
    It 'omits a workspace whose resource ID disagrees with the selected subscription' {
        Mock Get-AzOperationalInsightsWorkspace -ModuleName Clarity.Discovery.Azure {
            @{ ResourceId='/subscriptions/foreign/resourceGroups/Pilot/providers/Microsoft.OperationalInsights/workspaces/Omitted'; Name='Omitted'; ResourceGroupName='Pilot'; Location='westus' }
        }
        $zip = Invoke-AzFixture foreign
        ($zip['results.json'] | Where-Object capabilityId -eq 'azure.logAnalyticsWorkspaces').status | Should -Be Failed
        ($zip | ConvertTo-Json -Depth 20) | Should -Not -Match 'subscriptions/foreign'
    }
}
