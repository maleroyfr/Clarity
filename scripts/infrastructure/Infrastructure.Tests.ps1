BeforeAll {
    function global:Get-CimInstance { [CmdletBinding()]param($Namespace,$ClassName,$Property,$Filter) throw 'Unmocked local read' }
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Infrastructure.psm1') -Force
    . (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'tests/Read-CollectorFixture.ps1')
    function Invoke-InfraFixture($Name) {
        $data | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $TestDrive 'parameters.json')
        $path = Invoke-InfrastructureDiscovery (Join-Path $TestDrive 'parameters.json') (Join-Path $TestDrive "$Name.zip")
        Read-CollectorFixture $path "infra-$Name"
    }
}
AfterAll { Remove-Item -LiteralPath Function:\Get-CimInstance -ErrorAction SilentlyContinue }
Describe 'Local infrastructure metadata' {
    BeforeEach {
        $data = Get-Content (Join-Path $PSScriptRoot 'parameters.example.json') -Raw | ConvertFrom-Json
        Mock Get-InfrastructureVersions -ModuleName Clarity.Discovery.Infrastructure { @{ PowerShell='5.1'; CimCmdlets='1.0.0.0' } }
        Mock Get-LocalDnsIdentity -ModuleName Clarity.Discovery.Infrastructure { @{HostName='server';DomainName='example.test'} }
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure {
            if (!$Property -or ($Property -match 'PathName|StartName|Password|UserName|ProcessModel|\*')) { throw 'Overbroad query' }
            switch ($ClassName) {
                Win32_ComputerSystem { @{DNSHostName='SERVER';Domain='EXAMPLE.TEST';PartOfDomain=$true} }
                Win32_OperatingSystem { @{ProductType=3;Version='10.0.20348';Caption='Synthetic Windows Server'} }
                MicrosoftDNS_Zone { @{Name='example.test';ZoneType=1} }
                Win32_Share { @{Name='Pilot';Type=0;Path='DO-NOT-EXPORT'} }
                Win32_Printer { @{Name='Pilot print queue'} }
                Win32_Service { @{Name='PilotSvc';State='Running';PathName='DO-NOT-EXPORT'} }
                Win32_DependentService { @{Dependent=@{Name='PilotSvc'};Antecedent=@{Name='UncollectedSvc'};TypeOfDependency=2} }
                ApplicationPool { @{Name='PilotPool';ProcessModel='DO-NOT-EXPORT'} }
                Application { @{SiteName='Pilot';Path='/App';ApplicationPool='PilotPool'} }
                default { throw 'Unknown provider query' }
            }
        }
        Mock Get-ChildItem -ModuleName Clarity.Discovery.Infrastructure {
            if ($LiteralPath -ne 'Microsoft.PowerShell.Security\Certificate::LocalMachine\My') { throw 'Unexpected store' }
            @{ Thumbprint='0123456789ABCDEF0123456789ABCDEF01234567'; NotBefore=[datetime]'2026-01-01Z'; NotAfter=[datetime]'2027-01-01Z'; PrivateKey='DO-NOT-EXPORT' }
        }
    }
    It 'emits typed local inventory, a configured IIS pool and only directly declared dependency edges' {
        $zip = Invoke-InfraFixture complete
        @($zip['results.json'] | Where-Object { $_.status -ne 'Complete' -or !$_.warnings }).Count | Should -Be 0
        @($zip['objects.jsonl']).Count | Should -Be 8
        $dep = $zip['relationships.jsonl'] | Where-Object relationType -eq 'serviceDependsOnService'
        $dep.from.externalId | Should -Be 'server.example.test/pilotsvc'
        $dep.to.externalId | Should -Be 'server.example.test/uncollectedsvc'
        $pool = $zip['relationships.jsonl'] | Where-Object relationType -eq 'applicationUsesPool'
        $pool.to.externalId | Should -BeExactly 'server.example.test/PilotPool'
        ($zip | ConvertTo-Json -Depth 20) | Should -Not -Match 'DO-NOT-EXPORT'
        Should -Invoke Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -Times 1 -Exactly -ParameterFilter {
            $ClassName -eq 'Win32_Service' -and ($Property -join ',') -eq 'Name,State'
        }
    }
    It 'rejects a different host, joined domain or primary DNS suffix before any inventory read' {
        $data.scope.value = 'other.example.test'
        { Invoke-InfraFixture wrongHost } | Should -Throw '*Cannot verify*'
        $data.scope.value = 'server.example.test'
        Mock Get-LocalDnsIdentity -ModuleName Clarity.Discovery.Infrastructure { @{HostName='server';DomainName='other.test'} }
        { Invoke-InfraFixture wrongSuffix } | Should -Throw '*Cannot verify*'
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -ParameterFilter { $ClassName -eq 'Win32_ComputerSystem' } {
            @{DNSHostName='server';Domain='other.test';PartOfDomain=$false}
        }
        { Invoke-InfraFixture workgroup } | Should -Throw '*Cannot verify*'
        Should -Invoke Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -Times 0 -Exactly -ParameterFilter { $ClassName -eq 'Win32_Service' }
        Should -Invoke Get-ChildItem -ModuleName Clarity.Discovery.Infrastructure -Times 0 -Exactly
    }
    It 'keeps remaining sections and observed rows when one provider is missing or a read stops part way' {
        $data.requestId = '55555555-0000-0000-0000-000000000021'
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -ParameterFilter { $ClassName -eq 'Win32_Service' } {
            @{Name='PilotSvc';State='Running'}; throw 'DO-NOT-EXPORT'
        }
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -ParameterFilter { $ClassName -eq 'MicrosoftDNS_Zone' } { throw 'DO-NOT-EXPORT' }
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -ParameterFilter { $ClassName -eq 'Win32_Printer' } {}
        Mock Get-CimInstance -ModuleName Clarity.Discovery.Infrastructure -ParameterFilter { $ClassName -eq 'Application' } {
            @{SiteName='Pilot';Path='/App';ApplicationPool=$null}
        }
        $zip = Invoke-InfraFixture partial
        $dns = $zip['results.json'] | Where-Object capabilityId -eq 'infrastructure.dnsZones'
        $dns.status | Should -Be Failed
        $dns.objectCount | Should -BeNullOrEmpty
        $printer = $zip['results.json'] | Where-Object capabilityId -eq 'infrastructure.printers'
        $printer.status | Should -Be Complete
        $printer.objectCount | Should -Be 0
        $service = $zip['results.json'] | Where-Object capabilityId -eq 'infrastructure.services'
        $service.status | Should -Be Partial
        $service.objectCount | Should -Be 1
        ($zip['results.json'] | Where-Object capabilityId -eq 'infrastructure.applications').status | Should -Be Partial
        @($zip['relationships.jsonl'] | Where-Object relationType -eq 'applicationUsesPool').Count | Should -Be 0
        ($zip | ConvertTo-Json -Depth 20) | Should -Not -Match 'DO-NOT-EXPORT'
    }
}
