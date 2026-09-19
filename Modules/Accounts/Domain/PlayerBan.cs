namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class PlayerBan
{
    public int BanId { get; set; }
    
    public int Pid { get; set; }
    
    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
    
    public int BannedBy { get; set; }
    
    public string BannedReason { get; set; } = string.Empty;
    
    public DateTime? BannedUntil { get; set; }
    
    public bool IsActive { get; set; }
    
    public Player Player { get; set; } = null!;
}
