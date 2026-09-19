namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class User
{
    public int Uid { get; set; }
    
    public string Email { get; set; } = string.Empty;
    
    public string Password { get; set; } = string.Empty;
    
    public string? Ip { get; set; }
    
    public UserPermission Permission { get; set; } = UserPermission.NormalPlayer;
    
    public bool IsBanned { get; set; }
    
    public DateTime? BannedUntil { get; set; }
    
    public string? BannedReason { get; set; }
    
    public string SecurityStamp { get; set; } = string.Empty;
    
    public bool Verified { get; set; }
    
    public bool IsDeleted { get; set; }
    
    public DateTime RegisterAt { get; set; } = DateTime.UtcNow;
    
    public Player? Player { get; set; }
}
