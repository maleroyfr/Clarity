using System.Text.RegularExpressions;
using Clarity.Contracts;
using Clarity.Core;

namespace Clarity.Infrastructure;

public static partial class ResourceScriptScope
{
    internal static bool RequiresRequest(string producer) => producer is "clarity.azure" or "clarity.infrastructure";

    public static bool Allows(EnvironmentKind kind, string environmentId, CollectionScope scope)
    {
        if (scope.Kind != "resource" || string.IsNullOrEmpty(scope.Value)) { return false; }
        if (kind == EnvironmentKind.AzureScope)
        {
            var environment = environmentId.Split('/');
            var resource = scope.Value.Split('/');
            return environment is [var tenant, "subscriptions", var subscription]
                && NonEmptyGuid(tenant) && NonEmptyGuid(subscription)
                && resource is ["", "subscriptions", _, ..]
                && string.Equals(subscription, resource[2], StringComparison.OrdinalIgnoreCase)
                && (resource.Length == 3 || resource is [_, _, _, "resourceGroups", var group] && GroupName().IsMatch(group));
        }
        if (kind != EnvironmentKind.Other || scope.Value.Length > 253) { return false; }
        var split = scope.Value.IndexOf('.');
        return split > 0 && HostLabel().IsMatch(scope.Value[..split])
            && environmentId.Split('.').All(HostLabel().IsMatch)
            && string.Equals(scope.Value[(split + 1)..], environmentId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesEvidence(EnvironmentKind kind, DiscoveryPackage package)
    {
        var manifest = package.Manifest;
        if (!Allows(kind, manifest.EnvironmentExternalId, manifest.Scope)) { return false; }
        var azure = kind == EnvironmentKind.AzureScope;
        var ns = azure ? "azure.armId" : "windows.hostResource";
        var root = manifest.Scope.Value.ToLowerInvariant();
        var prefix = root + "/";
        var subscription = azure ? manifest.EnvironmentExternalId[manifest.EnvironmentExternalId.IndexOf('/')..].ToLowerInvariant() : "";
        bool Inside(string keyNamespace, string id) => keyNamespace == ns && (id == root || id.StartsWith(prefix, StringComparison.Ordinal));
        // A group points to its actual subscription, but cannot publish the parent as an in-scope object.
        return package.Objects.All(o => Inside(o.Namespace, o.ExternalId) && o.AlternativeKeys is null or { Count: 0 })
            && package.Relationships.All(r => Inside(r.From.Namespace, r.From.ExternalId) && (Inside(r.To.Namespace, r.To.ExternalId)
                || azure && r.RelationType == "resourceGroupInSubscription" && r.From.Type == "azureResourceGroup"
                && r.To.Type == "azureSubscription" && r.To.Namespace == ns && r.To.ExternalId == subscription));
    }

    private static bool NonEmptyGuid(string value) => value.Length == 36 && Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;

    [GeneratedRegex(@"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z")]
    private static partial Regex HostLabel();

    [GeneratedRegex(@"\A[a-zA-Z0-9_.()-]{0,89}[a-zA-Z0-9_()-]\z")]
    private static partial Regex GroupName();
}
