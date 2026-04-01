using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Onboarding;

public interface IAzureCliSetupScriptGenerator
{
    string GenerateScript(
        string tenantId,
        string appDisplayName,
        int secretLifetimeYears,
        IReadOnlyList<WorkloadArea> workloads);

    string ComputeSha256(string scriptText);

    IReadOnlyList<string> GetRequiredPermissions(IEnumerable<WorkloadArea> workloads);

    IReadOnlyList<string> GetOptionalPermissions(IEnumerable<WorkloadArea> workloads);
}
