namespace Clarity.Contracts;

public sealed record ScriptParameters(
    string SchemaVersion, Guid RequestId, string EnvironmentKind, string ExpectedEnvironmentExternalId,
    CollectionScope Scope, IReadOnlyList<string> Sections, string ProducerVersion, string? Domain = null);
