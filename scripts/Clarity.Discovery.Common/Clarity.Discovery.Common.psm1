Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-DiscoveryPrerequisites {
    [CmdletBinding()]
    param([version]$MinimumPowerShell = '5.1', [string[]]$Modules = @())
    if ($PSVersionTable.PSVersion -lt $MinimumPowerShell) {
        throw "Use PowerShell $MinimumPowerShell or newer in an authorized local session. No installation was performed."
    }
    foreach ($name in $Modules) {
        if (-not (Get-Module -ListAvailable -Name $name)) {
            throw "Required module $name is missing. Ask your administrator to install the documented module separately, then rerun Discovery."
        }
    }
}

function Read-DiscoveryParameters {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $data = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $allowed = @('schemaVersion', 'requestId', 'environmentKind', 'expectedEnvironmentExternalId', 'scope', 'sections', 'producerVersion', 'domain')
    foreach ($property in $data.PSObject.Properties.Name) {
        if ($property -notin $allowed) { throw 'Unknown request parameter. Do not place credentials or tokens in parameter files.' }
    }
    foreach ($required in $allowed | Where-Object { $_ -ne 'domain' }) {
        if ($required -notin $data.PSObject.Properties.Name) { throw "Missing request metadata: $required." }
    }
    $id = [guid]::Empty
    if ($data.schemaVersion -ne '1.0' -or -not [guid]::TryParse($data.requestId, [ref]$id) -or $id -eq [guid]::Empty) {
        throw 'Unsupported parameter version or invalid request ID. Download the parameters again.'
    }
    if ([string]::IsNullOrWhiteSpace($data.expectedEnvironmentExternalId) -or
        $data.scope.kind -notin @('tenant', 'forest', 'domain', 'ou', 'resource') -or
        [string]::IsNullOrWhiteSpace($data.scope.value) -or @($data.sections).Count -eq 0) {
        throw 'Request environment, scope and sections are required.'
    }
    foreach ($property in $data.scope.PSObject.Properties.Name) {
        if ($property -notin @('kind', 'value')) { throw 'Unknown scope metadata.' }
    }
    return $data
}

function New-DiscoveryPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Request,
        [Parameter(Mandatory)][string]$ProducerId,
        [Parameter(Mandatory)][string]$ProducerVersion,
        [Parameter(Mandatory)][string]$EnvironmentExternalId,
        [hashtable]$ModuleVersions = @{},
        [Parameter(Mandatory)][string]$SourceId
    )
    $runId = [guid]::NewGuid().ToString()
    $directory = Join-Path ([IO.Path]::GetTempPath()) "clarity-discovery-$runId"
    [void][IO.Directory]::CreateDirectory($directory)
    $utf8 = New-Object Text.UTF8Encoding($false)
    return @{
        WorkingDirectory = $directory
        Manifest = [ordered]@{
            schemaVersion = '1.0'; runId = $runId; requestId = [string]$Request.requestId
            producerId = $ProducerId; producerVersion = $ProducerVersion
            environmentKind = [string]$Request.environmentKind; environmentExternalId = $EnvironmentExternalId
            scope = $Request.scope; startedAtUtc = [DateTime]::UtcNow.ToString('o'); completedAtUtc = $null
            moduleVersions = $ModuleVersions; sourceId = $SourceId
        }
        ObjectsWriter = New-Object IO.StreamWriter((Join-Path $directory 'objects.jsonl'), $false, $utf8)
        RelationshipsWriter = New-Object IO.StreamWriter((Join-Path $directory 'relationships.jsonl'), $false, $utf8)
        Results = New-Object 'Collections.Generic.List[object]'
        SectionIds = New-Object 'Collections.Generic.HashSet[string]'
        ActiveSection = $null
    }
}

function Start-DiscoverySection {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package, [Parameter(Mandatory)][string]$CapabilityId,
        [Parameter(Mandatory)]$Scope, [string[]]$CoveredFields = @(), [string[]]$CoveredRelations = @())
    if ($null -ne $Package.ActiveSection) { throw 'Complete the active section first.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($Scope | ConvertTo-Json -Compress))))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $sectionId = "$($CapabilityId):$hash"
    if (-not $Package.SectionIds.Add($sectionId)) { throw 'A capability/scope can appear only once per package.' }
    $Package.ActiveSection = [ordered]@{
        sectionId = $sectionId; capabilityId = $CapabilityId; status = 'Complete'; scope = $Scope
        coveredFields = @($CoveredFields); coveredRelations = @($CoveredRelations)
        objectCount = 0; relationshipCount = 0; warnings = @(); errors = @()
    }
    Write-Progress -Activity 'Discovery' -Status $CapabilityId -PercentComplete 0
}

function Write-DiscoveryObject {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package, [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Namespace, [Parameter(Mandatory)][string]$ExternalId,
        [Parameter(Mandatory)][hashtable]$Attributes, [object[]]$AlternativeKeys = @())
    if ($null -eq $Package.ActiveSection) { throw 'Start a section before writing observations.' }
    foreach ($field in $Attributes.Keys) {
        if ($field -notin $Package.ActiveSection.coveredFields) { throw 'An observation contains an undeclared covered field.' }
    }
    $record = [ordered]@{
        sectionId = $Package.ActiveSection.sectionId; type = $Type; namespace = $Namespace; externalId = $ExternalId
        observedAtUtc = [DateTime]::UtcNow.ToString('o'); attributes = $Attributes; alternativeKeys = @($AlternativeKeys)
    }
    $Package.ObjectsWriter.WriteLine(($record | ConvertTo-Json -Depth 20 -Compress))
    $Package.ActiveSection.objectCount++
}

function Write-DiscoveryRelationship {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package, [Parameter(Mandatory)][string]$RelationType,
        [Parameter(Mandatory)][hashtable]$From, [Parameter(Mandatory)][hashtable]$To, [hashtable]$Properties = @{})
    if ($null -eq $Package.ActiveSection -or $RelationType -notin $Package.ActiveSection.coveredRelations) {
        throw 'The relationship type must be declared in the active section.'
    }
    $record = [ordered]@{
        sectionId = $Package.ActiveSection.sectionId; relationType = $RelationType; from = $From; to = $To
        observedAtUtc = [DateTime]::UtcNow.ToString('o'); properties = $Properties
    }
    $Package.RelationshipsWriter.WriteLine(($record | ConvertTo-Json -Depth 20 -Compress))
    $Package.ActiveSection.relationshipCount++
}

function Complete-DiscoverySection {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package, [ValidateSet('Complete', 'Partial', 'Failed', 'NotApplicable')][string]$Status = 'Complete',
        [object[]]$Warnings = @(), [object[]]$Errors = @())
    $section = $Package.ActiveSection
    if ($null -eq $section) { throw 'No active section.' }
    if ($Status -eq 'Failed' -and ($section.objectCount -gt 0 -or $section.relationshipCount -gt 0)) { $Status = 'Partial' }
    if ($Status -eq 'Complete' -and $Errors.Count -gt 0) { throw 'A complete section cannot contain errors.' }
    if ($Status -in @('Partial', 'Failed') -and $Warnings.Count -eq 0 -and $Errors.Count -eq 0) {
        throw 'Partial/failed sections require a sanitized reason.'
    }
    if ($Status -in @('Failed', 'NotApplicable')) {
        if ($section.objectCount -gt 0 -or $section.relationshipCount -gt 0) { throw 'Non-applicable sections cannot contain observations.' }
        $section.objectCount = $null; $section.relationshipCount = $null
    }
    $section.status = $Status; $section.warnings = @($Warnings); $section.errors = @($Errors)
    $Package.Results.Add($section)
    $Package.ActiveSection = $null
    Write-Progress -Activity 'Discovery' -Status $section.capabilityId -Completed
}

function Complete-DiscoveryPackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package, [Parameter(Mandatory)][string]$OutputPath)
    if ($null -ne $Package.ActiveSection -or $Package.Results.Count -eq 0) { throw 'Complete at least one section before packaging.' }
    $Package.ObjectsWriter.Dispose()
    $Package.RelationshipsWriter.Dispose()
    $Package.Manifest.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $Package.WorkingDirectory 'manifest.json'), ($Package.Manifest | ConvertTo-Json -Depth 20), $utf8)
    [IO.File]::WriteAllText((Join-Path $Package.WorkingDirectory 'results.json'), (ConvertTo-Json -InputObject @($Package.Results.ToArray()) -Depth 20), $utf8)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($Package.WorkingDirectory, [IO.Path]::GetFullPath($OutputPath))
    Remove-Item -LiteralPath $Package.WorkingDirectory -Recurse -Force
    return [IO.Path]::GetFullPath($OutputPath)
}
