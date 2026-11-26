BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'Clarity.Discovery.Common.psd1') -Force
}

Describe 'Canonical Discovery package' {
    It 'keeps valid records on partial failure and writes four UTF-8 root files' {
        $request = [pscustomobject]@{
            requestId = '11111111-0000-0000-0000-000000000002'; environmentKind = 'AdForest'
            scope = [ordered]@{ kind = 'domain'; value = 'example.test' }
        }
        $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.ad' -ProducerVersion '1.0.0' `
            -EnvironmentExternalId 'forest.example.test' -SourceId 'Synthetic common module test'
        Start-DiscoverySection -Package $package -CapabilityId 'ad.users' -Scope $request.scope -CoveredFields @('displayName', 'mail', 'proxyAddresses')
        Write-DiscoveryObject -Package $package -Type user -Namespace 'ad.objectGUID' -ExternalId 'synthetic-user' `
            -Attributes @{ displayName = 'Synthetic'; mail = $null; proxyAddresses = @() }
        Complete-DiscoverySection -Package $package -Status Failed -Errors @(@{ code = 'AccessDenied'; message = 'Read permission missing.' })
        $path = Complete-DiscoveryPackage -Package $package -OutputPath (Join-Path $TestDrive 'partial.zip')
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $zip.Entries.Count | Should -Be 4
            $reader = [IO.StreamReader]::new($zip.GetEntry('results.json').Open())
            try { $result = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            $result[0].status | Should -Be 'Partial'
            $result[0].objectCount | Should -Be 1
        } finally { $zip.Dispose() }
        if ($env:CLARITY_SCRIPT_FIXTURES) {
            [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
            Copy-Item -LiteralPath $path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES 'common-partial.zip')
        }
    }

    It 'rejects credential-shaped and invalid request parameter files' {
        $path = Join-Path $TestDrive 'parameters.json'
        '{"schemaVersion":"1.0","requestId":"11111111-0000-0000-0000-000000000002","environmentKind":"AdForest","expectedEnvironmentExternalId":"forest.example.test","scope":{"kind":"domain","value":"example.test"},"sections":["ad.users"],"producerVersion":"1.0.0","password":"synthetic-not-a-secret"}' |
            Set-Content -LiteralPath $path -Encoding UTF8
        { Read-DiscoveryParameters -Path $path } | Should -Throw '*Do not place credentials*'
    }

    It 'normalizes SharePoint Online URLs exactly like the server report adapter' {
        ConvertTo-DiscoverySharePointUrl 'https://Contoso.SharePoint.com/sites/Finance/' | Should -BeExactly 'https://contoso.sharepoint.com/sites/Finance'
        ConvertTo-DiscoverySharePointUrl 'https://contoso-my.sharepoint.com/personal/a_example_test' | Should -BeExactly 'https://contoso-my.sharepoint.com/personal/a_example_test'
        foreach ($invalid in @($null, '', 'http://contoso.sharepoint.com/sites/x', 'https://contoso.example.test/sites/x',
                'https://contoso.sharepoint.com/sites/x?a=1', 'https://contoso.sharepoint.com:8443/sites/x', 'https://contoso.sharepoint.com/sites/a b')) {
            ConvertTo-DiscoverySharePointUrl $invalid | Should -BeNullOrEmpty
        }
    }

    It 'reports missing prerequisites without installing modules' {
        { Assert-DiscoveryPrerequisites -Modules 'Clarity.Synthetic.Nonexistent.Module' } | Should -Throw '*install*separately*'
    }
}
