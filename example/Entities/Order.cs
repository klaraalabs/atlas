namespace Atlas.Example.Entities;

/// <summary>
/// Example Order entity to demonstrate collection navigation.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
