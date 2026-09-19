namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class Player
{
    public int Pid { get; set; }
    
    public int Uid { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    public string Uuid { get; set; } = string.Empty;
    
    public bool IsBanned { get; set; }
    
    public int? TidSkin { get; set; }
    
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    
    public User User { get; set; } = null!;
    
    public ICollection<PlayerBan> PlayerBans { get; set; } = new List<PlayerBan>();
}
