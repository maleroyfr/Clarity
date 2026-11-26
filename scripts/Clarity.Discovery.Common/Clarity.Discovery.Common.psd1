@{
    RootModule = 'Clarity.Discovery.Common.psm1'
    ModuleVersion = '1.0.0'
    GUID = 'c3c44129-140e-4c38-a2fb-307f02e4fc68'
    Author = 'Clarity contributors'
    Description = 'Canonical metadata-only Discovery ZIP v1 generation.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Assert-DiscoveryPrerequisites', 'Read-DiscoveryParameters', 'New-DiscoveryPackage',
        'Start-DiscoverySection', 'Write-DiscoveryObject', 'Write-DiscoveryRelationship',
        'Complete-DiscoverySection', 'Complete-DiscoveryPackage', 'ConvertTo-DiscoverySharePointUrl'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
