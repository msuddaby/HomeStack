namespace HomeStack.Core.Data;

public class Household
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// The instance admin shares their household's AI with this one. Used only while the household has no AI of its own.
    /// </summary>
    public bool UseSharedAi { get; set; }
}

public class User
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
    public required string Name { get; set; }
    public required string Username { get; set; }
    public string PasswordHash { get; set; } = "";
    /// <summary>Admins can make other members admin and mark split payments as received. The household's first user is one.</summary>
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
