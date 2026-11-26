function Connect-PowerBIServiceAccount {
    [CmdletBinding()]param([string]$Environment)
    if ($Environment -ne 'Public') { throw 'Unexpected environment' }
    $tenant = if ($env:CLARITY_POWERBI_TEST_CASE -eq 'wrongTenant') { '00000000-0000-0000-0000-000000000002' } else { '00000000-0000-0000-0000-000000000001' }
    [pscustomobject]@{ TenantId = $tenant }
}
function Disconnect-PowerBIServiceAccount { [CmdletBinding()]param() }
function Invoke-PowerBIRestMethod {
    [CmdletBinding()]param([string]$Url, [string]$Method)
    if ($Method -cne 'Get') { throw 'Unexpected verb' }
    $w = '10000000-0000-0000-0000-000000000001'
    $w2 = '10000000-0000-0000-0000-000000000002'
    $m = '20000000-0000-0000-0000-000000000001'
    $g = '30000000-0000-0000-0000-000000000001'
    $s = '40000000-0000-0000-0000-000000000001'
    $rows = @(switch -Exact ($Url) {
        'groups?$top=100&$skip=0' {
            if ($env:CLARITY_POWERBI_TEST_CASE -eq 'paged') {
                1..100 | ForEach-Object { @{ id = ('10000000-0000-0000-0000-{0:D12}' -f $_); name = "Workspace $_" } }
                break
            }
            if ($env:CLARITY_POWERBI_TEST_CASE -eq 'empty') { break }
            @{ id = $w; name = 'Synthetic workspace' }
            @{ id = $w2; name = 'Restricted workspace' }
        }
        'groups?$top=100&$skip=100' { @{ id = '10000000-0000-0000-0000-000000000101'; name = 'Last workspace' } }
        "groups/$w/reports" {
            @{ id = '50000000-0000-0000-0000-000000000001'; name = 'Synthetic report'; datasetId = $m; embedUrl = 'PrivacyCanary' }
            @{ id = '50000000-0000-0000-0000-000000000002'; name = 'Paginated report'; reportType = 'PaginatedReport' }
        }
        "groups/$w2/reports" { throw '403 PrivacyCanary' }
        "groups/$w/datasets" { @{ id = $m; name = 'Synthetic model'; configuredBy = 'owner@synthetic.example' } }
        "groups/$w2/datasets" { @{ id = '20000000-0000-0000-0000-000000000002'; name = 'Read-only model' } }
        "groups/$w/users?`$top=100&`$skip=0" {
            @{ principalType = 'Group'; groupUserAccessRight = 'Member'; identifier = '60000000-0000-0000-0000-000000000001' }
            @{ principalType = 'App'; groupUserAccessRight = 'Admin'; identifier = 'unlinked' }
        }
        "groups/$w2/users?`$top=100&`$skip=0" { }
        'gateways' {
            if ($env:CLARITY_POWERBI_TEST_CASE -eq 'paged') { throw 'Forbidden PrivacyCanary' }
            if ($env:CLARITY_POWERBI_TEST_CASE -ne 'empty') { @{ id = $g; name = 'Synthetic gateway'; publicKey = 'PrivacyCanary' } }
        }
        "gateways/$g/datasources" {
            @{ id = $s; gatewayId = $g; datasourceType = 'Sql'; datasourceName = 'PrivacyCanary'; credentialDetails = @{ token = 'PrivacyCanary' }; connectionDetails = 'PrivacyCanary' }
        }
        "groups/$w/datasets/$m/datasources" {
            @{ datasourceId = $s; gatewayId = $g; datasourceType = 'Sql'; connectionDetails = @{ server = 'PrivacyCanary' } }
            @{ datasourceType = 'Web'; connectionDetails = @{ url = 'PrivacyCanary' } }
        }
        "groups/$w2/datasets/20000000-0000-0000-0000-000000000002/datasources" { throw '403 PrivacyCanary' }
        default { throw 'Unexpected REST call' }
    })
    @{ value = $rows } | ConvertTo-Json -Depth 5 -Compress
}
