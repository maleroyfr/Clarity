using System.Security.Cryptography;
using System.Text;
using Clarity.Application.Onboarding;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Infrastructure.Onboarding;

public sealed class AzureCliSetupScriptGenerator : IAzureCliSetupScriptGenerator
{
    public IReadOnlyList<string> GetRequiredPermissions(IEnumerable<WorkloadArea> workloads) =>
        WorkloadCapabilityCatalog.GetRequiredPermissions(workloads)
            .Select(permission => permission.Name)
            .ToList();

    public IReadOnlyList<string> GetOptionalPermissions(IEnumerable<WorkloadArea> workloads) =>
        WorkloadCapabilityCatalog.GetOptionalPermissions(workloads)
            .Select(permission => permission.Name)
            .ToList();

    public string GenerateScript(
        string tenantId,
        string appDisplayName,
        int secretLifetimeYears,
        IReadOnlyList<WorkloadArea> workloads)
    {
        var permissions = GetRequiredPermissions(workloads);
        var permissionsArrayLiteral = string.Join(",\n        ", permissions.Select(permission => $"\"{permission}\""));

        const string template = """
            # Clarity Setup Script (Azure CLI)
            # Prerequisites:
            #   - Azure CLI installed
            #   - Rights to create app registrations
            #   - Application Administrator or Global Administrator for admin consent

            param(
                [string]$TenantId = "{{TenantId}}",
                [string]$AppName = "{{AppName}}",
                [int]$SecretYears = {{SecretLifetimeYears}}
            )

            $ErrorActionPreference = "Stop"
            $graphAppId = "00000003-0000-0000-c000-000000000000"
            $env:AZURE_CORE_OUTPUT = "json"

            $selectedPermissions = @(
                {{PermissionsArray}}
            )

            $azCmd = Get-Command az -ErrorAction SilentlyContinue
            if (-not $azCmd) {
                Write-Host "Azure CLI is not installed." -ForegroundColor Red
                exit 1
            }

            Write-Host ""
            Write-Host "========================================" -ForegroundColor Cyan
            Write-Host "  Clarity Entra App Setup (Azure CLI)" -ForegroundColor Cyan
            Write-Host "========================================" -ForegroundColor Cyan
            Write-Host "Tenant ID   : $TenantId"
            Write-Host "App Name    : $AppName"
            Write-Host "Permissions : $($selectedPermissions -join ', ')"
            Write-Host ""

            $currentAccount = $null
            try {
                $currentAccountJson = az account show --only-show-errors -o json 2>$null
                if ($LASTEXITCODE -eq 0 -and $currentAccountJson) {
                    $currentAccount = $currentAccountJson | ConvertFrom-Json
                }
            } catch { }

            if (-not $currentAccount -or $currentAccount.tenantId -ne $TenantId) {
                az login --tenant $TenantId --allow-no-subscriptions --only-show-errors | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "Failed to login to Azure." -ForegroundColor Red
                    exit 1
                }
            }

            $graphSpJson = az ad sp show --id $graphAppId --only-show-errors -o json 2>$null
            if ($LASTEXITCODE -ne 0 -or -not $graphSpJson) {
                Write-Host "Could not resolve the Microsoft Graph service principal." -ForegroundColor Red
                exit 1
            }

            $graphSp = $graphSpJson | ConvertFrom-Json
            $permissionEntries = @()
            foreach ($permName in $selectedPermissions) {
                $appRole = $graphSp.appRoles | Where-Object {
                    $_.value -eq $permName -and $_.allowedMemberTypes -contains "Application"
                }
                if ($appRole) {
                    $permissionEntries += "$($appRole.id)=Role"
                }
            }

            if ($permissionEntries.Count -eq 0) {
                Write-Host "No valid Microsoft Graph application permissions were resolved." -ForegroundColor Red
                exit 1
            }

            $appJson = az ad app create `
                --display-name $AppName `
                --sign-in-audience "AzureADMyOrg" `
                --only-show-errors -o json 2>$null

            if ($LASTEXITCODE -ne 0 -or -not $appJson) {
                Write-Host "Failed to create the app registration." -ForegroundColor Red
                exit 1
            }

            $app = $appJson | ConvertFrom-Json
            $clientId = $app.appId

            Start-Sleep -Seconds 2
            az ad sp create --id $clientId --only-show-errors -o json 2>$null | Out-Null

            $permArgs = $permissionEntries -join " "
            Invoke-Expression "az ad app permission add --id $clientId --api $graphAppId --api-permissions $permArgs --only-show-errors" | Out-Null

            $adminConsentAttempted = $true
            $adminConsentGranted = $false
            $adminConsentError = ""

            $consentResult = az ad app permission admin-consent --id $clientId --only-show-errors 2>&1
            if ($LASTEXITCODE -eq 0) {
                $adminConsentGranted = $true
            } else {
                $adminConsentError = "$consentResult"
            }

            $endDate = (Get-Date).AddYears($SecretYears).ToString("yyyy-MM-dd")
            $secretDisplayName = "Clarity-" + (Get-Date).ToString("yyyyMMdd-HHmmss")

            $credJson = az ad app credential reset `
                --id $clientId `
                --append `
                --display-name $secretDisplayName `
                --end-date $endDate `
                --query "{password:password}" `
                --only-show-errors -o json 2>$null

            if ($LASTEXITCODE -ne 0 -or -not $credJson) {
                Write-Host "Failed to create the client secret." -ForegroundColor Red
                exit 1
            }

            $cred = $credJson | ConvertFrom-Json
            $result = [ordered]@{
                CreatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
                SecretExpiresAtUtc = $endDate
                AppDisplayName = $AppName
                GraphScope = "https://graph.microsoft.com/.default"
                Permissions = $selectedPermissions
                ClientSecret = $cred.password
                ClientId = $clientId
                TenantId = $TenantId
                AdminConsentAttempted = $adminConsentAttempted
                AdminConsentGranted = $adminConsentGranted
                AdminConsentError = $adminConsentError
            }

            $jsonOutput = $result | ConvertTo-Json -Depth 10
            Write-Host "--- BEGIN JSON ---" -ForegroundColor Yellow
            Write-Host $jsonOutput
            Write-Host "--- END JSON ---" -ForegroundColor Yellow
            """;

        return template
            .Replace("{{TenantId}}", tenantId)
            .Replace("{{AppName}}", appDisplayName)
            .Replace("{{SecretLifetimeYears}}", secretLifetimeYears.ToString())
            .Replace("{{PermissionsArray}}", permissionsArrayLiteral);
    }

    public string ComputeSha256(string scriptText)
    {
        var bytes = Encoding.UTF8.GetBytes(scriptText);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
