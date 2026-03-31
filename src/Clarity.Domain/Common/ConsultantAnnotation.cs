using Clarity.Domain.Common;

namespace Clarity.Domain.Common;

/// <summary>
/// Cross-cutting consultant annotation that can be attached to any entity.
/// Used to add free-text notes and observations during audits.
/// </summary>
public sealed class ConsultantAnnotation : Entity
{
    public Guid EntityId { get; private set; }
    public string EntityType { get; private set; } = default!;
    public string Text { get; private set; } = default!;
    public string? Category { get; private set; }
    public string? CreatedBy { get; private set; }

    private ConsultantAnnotation() { } // EF Core

    public static ConsultantAnnotation Create(
        Guid entityId,
        string entityType,
        string text,
        string? category = null,
        string? createdBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new ConsultantAnnotation
        {
            EntityId = entityId,
            EntityType = entityType,
            Text = text.Trim(),
            Category = category?.Trim(),
            CreatedBy = createdBy
        };
    }

    public void Update(string text, string? category)
    {
        Text = text.Trim();
        Category = category?.Trim();
        MarkUpdated();
    }
}
