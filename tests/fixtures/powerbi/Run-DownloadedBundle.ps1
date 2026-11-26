param([Parameter(Mandatory)][string]$BundleDir, [ValidateSet('visible', 'empty')][string]$Case)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = "$PSScriptRoot$([IO.Path]::PathSeparator)$(Join-Path $PSHOME 'Modules')"
$env:CLARITY_POWERBI_TEST_CASE = $Case
& (Join-Path $BundleDir 'powerbi/Collect-PowerBI.ps1') -ParameterFile (Join-Path $BundleDir 'parameters.json') -OutputPath (Join-Path $BundleDir 'result.zip')
