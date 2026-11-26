BeforeAll {
    $script:entry = Join-Path $PSScriptRoot 'Collect-PowerBI.ps1'
    $script:parameters = Join-Path $PSScriptRoot 'parameters.example.json'
    # Only the synthetic profile is discoverable as MicrosoftPowerBIMgmt.Profile.
    $script:oldModulePath = $env:PSModulePath
    $mockRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../tests/fixtures/powerbi')).Path
    $env:PSModulePath = "$mockRoot$([IO.Path]::PathSeparator)$(Join-Path $PSHOME 'Modules')"
    function Read-PbiZip($path) {
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $text = @{}
            foreach ($item in $zip.Entries) {
                $r = [IO.StreamReader]::new($item.Open())
                try { $text[$item.Name] = $r.ReadToEnd() } finally { $r.Dispose() }
            }
            $text
        } finally { $zip.Dispose() }
    }
}
AfterAll {
    Remove-Module MicrosoftPowerBIMgmt.Profile -ErrorAction SilentlyContinue
    $env:PSModulePath = $script:oldModulePath
    Remove-Item Env:\CLARITY_POWERBI_TEST_CASE -ErrorAction SilentlyContinue
}
Describe 'Power BI Discovery' {
    It 'uses documented skip/top paging and distinguishes a denied list from a visible empty one' {
        $env:CLARITY_POWERBI_TEST_CASE = 'paged'
        $p = Get-Content -LiteralPath $parameters -Raw | ConvertFrom-Json
        $p.sections = @('powerbi.workspaces','powerbi.gateways')
        $file = Join-Path $TestDrive 'subset.json'
        $p | ConvertTo-Json | Set-Content -LiteralPath $file
        $out = Join-Path $TestDrive 'paged.zip'
        & $entry -ParameterFile $file -OutputPath $out | Out-Null
        $results = (Read-PbiZip $out)['results.json'] | ConvertFrom-Json
        $results[0].objectCount | Should -Be 101
        $results[0].status | Should -Be 'Partial'
        $results[1].status | Should -Be 'Failed'
        $results[1].objectCount | Should -BeNullOrEmpty
        $env:CLARITY_POWERBI_TEST_CASE = 'empty'
        $out = Join-Path $TestDrive 'empty.zip'
        & $entry -ParameterFile $file -OutputPath $out | Out-Null
        $results = (Read-PbiZip $out)['results.json'] | ConvertFrom-Json
        $results[1].status | Should -Be 'Partial'
        $results[1].objectCount | Should -Be 0
    }
    It 'rejects a different authenticated tenant before reading or creating a ZIP' {
        $env:CLARITY_POWERBI_TEST_CASE = 'wrongTenant'
        $out = Join-Path $TestDrive 'wrong.zip'
        { & $entry -ParameterFile $parameters -OutputPath $out } | Should -Throw '*tenant does not match*'
        Test-Path -LiteralPath $out | Should -BeFalse
    }
    It 'emits only bounded metadata and typed links, preserving successful siblings of denied reads' {
        $env:CLARITY_POWERBI_TEST_CASE = 'visible'
        $out = Join-Path $TestDrive 'powerbi.zip'
        & $entry -ParameterFile $parameters -OutputPath $out | Out-Null
        $text = Read-PbiZip $out
        ($text.Values -join '') | Should -Not -Match 'PrivacyCanary|credentialDetails|connectionDetails|embedUrl'
        $results = $text['results.json'] | ConvertFrom-Json
        @($results | Where-Object status -ne 'Partial').Count | Should -Be 0
        ($results | Where-Object capabilityId -eq 'powerbi.reports').errors.code | Should -Contain 'PowerBIReadFailed'
        ($results | Where-Object capabilityId -eq 'powerbi.owners').warnings.code | Should -Contain 'PowerBIUnlinked'
        $objects = $text['objects.jsonl'].Trim().Split("`n") | ConvertFrom-Json
        ($objects | Where-Object type -eq 'powerBIReport').Count | Should -Be 2
        @($objects | Where-Object type -eq 'user').Count | Should -Be 0
        $links = $text['relationships.jsonl'].Trim().Split("`n") | ConvertFrom-Json
        ($links | Where-Object relationType -eq 'usesSemanticModel').Count | Should -Be 1
        ($links | Where-Object relationType -eq 'modelOwnedBy').to.externalId | Should -Be 'owner@synthetic.example'
        ($links | Where-Object relationType -eq 'workspaceRoleAssignedTo').to.type | Should -Be 'group'
        ($links | Where-Object relationType -eq 'usesPowerBIDataSource').to.externalId | Should -Be '40000000-0000-0000-0000-000000000001'
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $out -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'powerbi.zip') -Force
        }
    }
}
