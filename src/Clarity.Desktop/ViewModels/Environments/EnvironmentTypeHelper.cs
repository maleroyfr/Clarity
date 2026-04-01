using Clarity.Domain.Environments;

namespace Clarity.Desktop.ViewModels.Environments;

/// <summary>Provides human-friendly display names for EnvironmentType enum values.</summary>
public static class EnvironmentTypeHelper
{
    public static string ToDisplayName(EnvironmentType type) => type switch
    {
        EnvironmentType.M365Tenant     => "Microsoft 365 Tenant",
        EnvironmentType.OnPremAD       => "On-Premises AD",
        EnvironmentType.HybridAD       => "Hybrid AD",
        EnvironmentType.ExchangeOnPrem => "Exchange On-Premises",
        EnvironmentType.Standalone     => "Standalone",
        _                              => type.ToString()
    };

    public static string ToShortName(EnvironmentType type) => type switch
    {
        EnvironmentType.M365Tenant     => "M365",
        EnvironmentType.OnPremAD       => "On-Prem AD",
        EnvironmentType.HybridAD       => "Hybrid AD",
        EnvironmentType.ExchangeOnPrem => "Exchange",
        EnvironmentType.Standalone     => "Standalone",
        _                              => type.ToString()
    };
}

/// <summary>Wraps EnvironmentType for display in ComboBox with friendly name.</summary>
public sealed class EnvironmentTypeOption
{
    public EnvironmentType Value { get; init; }
    public string DisplayName { get; init; } = string.Empty;

    public override string ToString() => DisplayName;

    public static IReadOnlyList<EnvironmentTypeOption> All { get; } =
        Enum.GetValues<EnvironmentType>()
            .Select(t => new EnvironmentTypeOption
            {
                Value = t,
                DisplayName = EnvironmentTypeHelper.ToDisplayName(t)
            })
            .ToList();
}
