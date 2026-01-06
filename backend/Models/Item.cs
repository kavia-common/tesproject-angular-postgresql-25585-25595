namespace Backend.Models;

public sealed class Item
{
    public Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
