#requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'Clarity.Discovery.Common/Clarity.Discovery.Common.psd1')

function ConvertTo-PbiId($Value) {
    $id = [guid]::Empty
    if ([guid]::TryParse($Value, [ref]$id) -and $id -ne [guid]::Empty) { $id.ToString() }
}

$script:sections = @{
    'powerbi.workspaces'     = @{ Fields = @('displayName'); Relations = @() }
    'powerbi.reports'        = @{ Fields = @('displayName'); Relations = @('inPowerBIWorkspace', 'usesSemanticModel') }
    'powerbi.semanticModels' = @{ Fields = @('displayName'); Relations = @('inPowerBIWorkspace') }
    'powerbi.owners'         = @{ Fields = @(); Relations = @('modelOwnedBy') }
    'powerbi.workspaceRoles' = @{ Fields = @(); Relations = @('workspaceRoleAssignedTo') }
    'powerbi.gateways'       = @{ Fields = @('displayName'); Relations = @() }
    'powerbi.dataSources'    = @{ Fields = @('dataSourceType'); Relations = @('onPowerBIGateway') }
    'powerbi.modelSources'   = @{ Fields = @('dataSourceType'); Relations = @('usesPowerBIDataSource', 'onPowerBIGateway') }
}
$script:sourceTypes = 'ActiveDirectory AdobeAnalytics AdoDotNet AnalysisServices AzureBlobs AzureDataLakeStorage AzureMarketplace AzureTables BizTalk CDPA CustomConnector CustomHttpApi DB2 Essbase EventHub Excel Exchange Extension Facebook File Folder GoogleAnalytics Hdfs HDInsight Informix MQ MySql OData ODBC OleDb Oracle PostgreSql PowerQueryMashup PubNub Salesforce SAPBW SAPBWMessageServer SapErp SAPHana SharePoint SharePointDocLib SharePointList Sql Sybase Teradata UIFlow Web' -split ' '

function Read-PbiList($State, [string]$Path, [switch]$Paged) {
    $skip = 0
    $seen = [Collections.Generic.HashSet[string]]::new()
    do {
        $url = if ($Paged) { "${Path}?`$top=100&`$skip=$skip" } else { $Path }
        try {
            $raw = Invoke-PowerBIRestMethod -Url $url -Method Get -Verbose:$false -Debug:$false -ErrorAction Stop
            $page = ConvertFrom-Json -InputObject $raw -AsHashtable
            if ($page -isnot [Collections.IDictionary] -or $page.value -isnot [array]) { throw 'Invalid collection shape' }
        } catch {
            $State.ReadFailed = $true
            return
        }
        $advanced = $false
        foreach ($row in $page.value) {
            if ($row -isnot [Collections.IDictionary]) { $State.Unlinked = $true; continue }
            if ($Paged) {
                $key = if ($Path -eq 'groups') { $row['id'] } else { "$($row['principalType']):$($row['identifier'])" }
                if (-not $seen.Add([string]$key)) { continue }
                $advanced = $true
            }
            $row
        }
        $skip += $page.value.Length
        if ($Paged -and $page.value.Length -eq 100 -and -not $advanced) { $State.ReadFailed = $true; return }
        # These v1.0 lists document either top/skip or no paging. Never follow an arbitrary URL.
        if ($page['@odata.nextLink'] -and -not $Paged) { $State.ReadFailed = $true }
    } while ($Paged -and $page.value.Length -eq 100)
}

function New-PbiRef($State, [string]$Type, $Value) {
    $id = ConvertTo-PbiId $Value
    if (-not $id) { $State.Unlinked = $true; return }
    @{ type = $Type; namespace = 'powerbi.id'; externalId = $id }
}

function Write-PbiItem($State, $Ref, [hashtable]$Attributes) {
    if ($State.Objects.Add("$($Ref.type):$($Ref.externalId)")) {
        Write-DiscoveryObject -Package $State.Package -Type $Ref.type -Namespace $Ref.namespace -ExternalId $Ref.externalId -Attributes $Attributes
    }
}

function Write-PbiNamedItem($State, $Ref, $Name) {
    $fields = @{}
    if ($Name -is [string] -and $Name.Length -le 500 -and -not [string]::IsNullOrWhiteSpace($Name)) { $fields.displayName = $Name }
    else { $State.Unlinked = $true }
    Write-PbiItem $State $Ref $fields
}

function Write-PbiEdge($State, [string]$Relation, $From, $To, [hashtable]$Properties = @{}) {
    if ($From -and $To -and $State.Edges.Add("$Relation|$($From.type):$($From.externalId)|$($To.type):$($To.namespace):$($To.externalId)")) {
        Write-DiscoveryRelationship -Package $State.Package -RelationType $Relation -From $From -To $To -Properties $Properties
    }
}

function Write-PbiSource($State, $Row, $Source, $Gateway, $Model) {
    if (-not $Source) { return }
    $fields = @{}
    if ($Row['datasourceType'] -cin $script:sourceTypes) { $fields.dataSourceType = $Row['datasourceType'] }
    else { $State.Unlinked = $true }
    Write-PbiItem $State $Source $fields
    Write-PbiEdge $State 'onPowerBIGateway' $Source $Gateway
    if ($Model) { Write-PbiEdge $State 'usesPowerBIDataSource' $Model $Source }
}

function Get-PbiUserRef($State, $Address) {
    if ($Address -is [string] -and $Address.Length -le 500 -and $Address -match '^[^@\s;|<>]+@[^@\s;|<>]+$') {
        return @{ type = 'user'; namespace = 'upn'; externalId = $Address.ToLowerInvariant() }
    }
    $State.Unlinked = $true
}

function Write-PbiSection($State, $Section) {
    if ($Section -in @('powerbi.gateways', 'powerbi.dataSources')) {
        Read-PbiList $State 'gateways' | ForEach-Object {
            $gateway = New-PbiRef $State 'powerBIGateway' $_['id']
            if (-not $gateway) { return }
            if ($Section -eq 'powerbi.gateways') { Write-PbiNamedItem $State $gateway $_['name']; return }
            Read-PbiList $State "gateways/$($gateway.externalId)/datasources" | ForEach-Object {
                if ((ConvertTo-PbiId $_['gatewayId']) -ne $gateway.externalId) { $State.Unlinked = $true; return }
                Write-PbiSource $State $_ (New-PbiRef $State 'powerBIDataSource' $_['id']) $gateway $null
            }
        }
        return
    }
    Read-PbiList $State 'groups' -Paged | ForEach-Object {
        $group = $_
        $workspace = New-PbiRef $State 'powerBIWorkspace' $group['id']
        if (-not $workspace) { return }
        $path = "groups/$($workspace.externalId)"
        switch ($Section) {
            'powerbi.workspaces' { Write-PbiNamedItem $State $workspace $group['name'] }
            'powerbi.reports' {
                Read-PbiList $State "$path/reports" | ForEach-Object {
                    $report = New-PbiRef $State 'powerBIReport' $_['id']
                    if (-not $report) { return }
                    Write-PbiNamedItem $State $report $_['name']
                    Write-PbiEdge $State 'inPowerBIWorkspace' $report $workspace
                    if ($_['datasetId']) { Write-PbiEdge $State 'usesSemanticModel' $report (New-PbiRef $State 'powerBISemanticModel' $_['datasetId']) }
                }
            }
            'powerbi.workspaceRoles' {
                Read-PbiList $State "$path/users" -Paged | ForEach-Object {
                    $principalRow = $_
                    $role = $principalRow['groupUserAccessRight']
                    if ($role -eq 'None') { return }
                    if ($role -cnotin @('Admin', 'Member', 'Contributor', 'Viewer')) { $State.Unlinked = $true; return }
                    $principal = switch ($principalRow['principalType']) {
                        'User' { Get-PbiUserRef $State $principalRow['identifier'] }
                        'Group' {
                            $id = ConvertTo-PbiId $principalRow['identifier']
                            if ($id) { @{ type = 'group'; namespace = 'microsoft.graph'; externalId = $id } }
                            else { $State.Unlinked = $true }
                        }
                        default { $State.Unlinked = $true }
                    }
                    Write-PbiEdge $State 'workspaceRoleAssignedTo' $workspace $principal @{ role = $role }
                }
            }
            default {
                Read-PbiList $State "$path/datasets" | ForEach-Object {
                    $dataset = $_
                    $model = New-PbiRef $State 'powerBISemanticModel' $dataset['id']
                    if (-not $model) { return }
                    switch ($Section) {
                        'powerbi.semanticModels' {
                            Write-PbiNamedItem $State $model $dataset['name']
                            Write-PbiEdge $State 'inPowerBIWorkspace' $model $workspace
                        }
                        'powerbi.owners' { Write-PbiEdge $State 'modelOwnedBy' $model (Get-PbiUserRef $State $dataset['configuredBy']) }
                        'powerbi.modelSources' {
                            Read-PbiList $State "$path/datasets/$($model.externalId)/datasources" | ForEach-Object {
                                $gateway = if ($_['gatewayId']) { New-PbiRef $State 'powerBIGateway' $_['gatewayId'] }
                                Write-PbiSource $State $_ (New-PbiRef $State 'powerBIDataSource' $_['datasourceId']) $gateway $model
                            }
                        }
                    }
                }
            }
        }
    }
}

function Invoke-PowerBIDiscovery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ParameterFile, [Parameter(Mandatory)][string]$OutputPath)
    $request = Read-DiscoveryParameters -Path $ParameterFile
    $expected = ConvertTo-PbiId $request.expectedEnvironmentExternalId
    if (-not $expected -or $request.environmentKind -cne 'M365Tenant' -or $request.scope.kind -cne 'tenant' -or
        $request.scope.value -ine $expected -or $request.producerVersion -cne '1.0.0' -or
        ($request.PSObject.Properties['domain'] -and $request.domain)) {
        throw 'Download a Power BI 1.0.0 request bound to the environment tenant.'
    }
    if (@($request.sections | Where-Object { $_ -cnotin $script:sections.Keys }).Count -or
        @($request.sections | Select-Object -Unique).Count -ne @($request.sections).Count) { throw 'Unknown or duplicate Power BI section.' }
    Assert-DiscoveryPrerequisites -MinimumPowerShell '7.2' -Modules @('MicrosoftPowerBIMgmt.Profile')
    try { $module = Import-Module MicrosoftPowerBIMgmt.Profile -RequiredVersion 1.3.84 -PassThru -ErrorAction Stop }
    catch { throw 'Install MicrosoftPowerBIMgmt.Profile 1.3.84 separately and rerun in a dedicated PowerShell window.' }
    try { $profile = Connect-PowerBIServiceAccount -Environment Public -ErrorAction Stop }
    catch { throw 'Interactive Power BI sign-in failed. Check the authorized account and relaunch.' }
    try {
        $tenant = ConvertTo-PbiId $profile.TenantId
        if ($tenant -ne $expected) { throw 'The authenticated Power BI tenant does not match the request.' }
        $package = New-DiscoveryPackage -Request $request -ProducerId 'clarity.powerbi' -ProducerVersion '1.0.0' `
            -EnvironmentExternalId $tenant -SourceId 'Power BI REST v1.0' `
            -ModuleVersions @{ 'MicrosoftPowerBIMgmt.Profile' = $module.Version.ToString(); PowerShell = $PSVersionTable.PSVersion.ToString() }
        foreach ($section in $request.sections) {
            $definition = $script:sections[$section]
            Start-DiscoverySection -Package $package -CapabilityId $section -Scope $request.scope -CoveredFields $definition.Fields -CoveredRelations $definition.Relations
            $state = @{ Package = $package; ReadFailed = $false; Unlinked = $false; Objects = [Collections.Generic.HashSet[string]]::new(); Edges = [Collections.Generic.HashSet[string]]::new() }
            Write-PbiSection $state $section
            $warnings = @(@{ code = 'PowerBIVisibility'; message = 'Only operator-visible resources are represented. Omission never establishes removal or effective access.' })
            if ($state.Unlinked) { $warnings += @{ code = 'PowerBIUnlinked'; message = 'A field, identifier or principal was unavailable or unsupported. No value or link was inferred.' } }
            $errors = @()
            $status = 'Partial'
            if ($state.ReadFailed) {
                $errors += @{ code = 'PowerBIReadFailed'; message = 'A list could not be fully read. Check permissions, throttling and the session before rerunning.' }
                if (-not $state.Objects.Count -and -not $state.Edges.Count) { $status = 'Failed' }
            }
            Complete-DiscoverySection -Package $package -Status $status -Warnings $warnings -Errors $errors
        }
        Complete-DiscoveryPackage -Package $package -OutputPath $OutputPath
    } finally {
        Disconnect-PowerBIServiceAccount -ErrorAction Stop | Out-Null
    }
}

Export-ModuleMember -Function Invoke-PowerBIDiscovery
