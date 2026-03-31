namespace Clarity.Domain.Common;

/// <summary>
/// Represents a tag attached to any entity. Tags are free-form key/value metadata.
/// </summary>
public sealed class Tag : ValueObject
{
    public string Key { get; }
    public string? Value { get; }

    public Tag(string key, string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key.Trim().ToLowerInvariant();
        Value = value?.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return Value;
    }

    public override string ToString() => Value is null ? Key : $"{Key}:{Value}";
}
