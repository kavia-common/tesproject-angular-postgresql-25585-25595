using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public sealed class CreateItemDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public sealed class UpdateItemDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
}
