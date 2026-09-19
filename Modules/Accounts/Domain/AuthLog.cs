namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class AuthLog
{
    public int Id { get; set; }
    
    public string Action { get; set; } = string.Empty;
    
    public int? ActorId { get; set; }
    
    public int SubjectId { get; set; }
    
    public string? Context { get; set; }
    
    public string? Ip { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
