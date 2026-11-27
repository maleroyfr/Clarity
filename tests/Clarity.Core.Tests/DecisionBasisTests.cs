using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Clarity.Core.Tests;

public class DecisionBasisTests
{
    [Fact]
    public void SerializationKeepsLegacyFingerprintsValid()
    {
        BasisValue[] values = [new("userPrincipalName", "\"alice@example.test\"", false), new("mail", "\"b@example.test\"", true),
            new("mail", "\"a@example.test\"", true)];
        var legacyFields = values.OrderBy(v => v.Field, StringComparer.Ordinal).ThenBy(v => v.ValueJson, StringComparer.Ordinal)
            .Select(v => new { v.Field, v.ValueJson, v.Conflicting }).ToList();
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Presence = InventoryPresence.Observed, fields = legacyFields }))));

        DecisionBasis.Fingerprint(DecisionBasis.Serialize(InventoryPresence.Observed, values.Reverse())).ShouldBe(legacy);
    }

    [Fact]
    public void ChangesExplainFieldAndPresenceDifferencesAndUnknownLegacyBases()
    {
        var recorded = DecisionBasis.Serialize(InventoryPresence.Observed,
            [new("userPrincipalName", "\"alice@example.test\"", false), new("enabled", "true", false), new("mail", "\"alice@example.test\"", false)]);
        var current = DecisionBasis.Serialize(InventoryPresence.AbsentFromScope,
            [new("userPrincipalName", "\"alice.renamed@example.test\"", false), new("enabled", "true", false),
                new("proxyAddresses", "\"SMTP:a@example.test\"", true), new("proxyAddresses", "\"SMTP:b@example.test\"", true)]);

        DecisionBasis.Changes(recorded, current).ShouldBe([
            new BasisChange(DecisionBasis.PresenceField, nameof(InventoryPresence.Observed), nameof(InventoryPresence.AbsentFromScope)),
            new BasisChange("mail", "\"alice@example.test\"", null),
            new BasisChange("proxyAddresses", null, "⚠ \"SMTP:a@example.test\" | ⚠ \"SMTP:b@example.test\""),
            new BasisChange("userPrincipalName", "\"alice@example.test\"", "\"alice.renamed@example.test\"")]);
        DecisionBasis.Changes(recorded, recorded).ShouldBeEmpty();
        DecisionBasis.Changes(null, current).ShouldBeNull();
    }
}
