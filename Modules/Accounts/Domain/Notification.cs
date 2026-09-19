namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class Notification
{
    public int Id { get; set; }
    
    public string Type { get; set; } = string.Empty;
    
    public string NotifiableType { get; set; } = string.Empty;
    
    public int NotifiableId { get; set; }
    
    public string? Data { get; set; }
    
    public DateTime? ReadAt { get; set; }
}
