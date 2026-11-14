namespace HomeStack.Core.Data;

public class Household
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class User
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
    public required string Name { get; set; }
    public required string Username { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
