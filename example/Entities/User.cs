namespace Atlas.Example.Entities;

/// <summary>
/// Example User entity with navigation properties.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }

    // Navigation properties
    public Balance? Balance { get; set; }
    public ICollection<Order> Orders { get; set; } = [];
}

/// <summary>
/// Example Balance entity (owned by User).
/// </summary>
public class Balance
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
