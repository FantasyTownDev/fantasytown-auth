namespace FantasyTown.Auth.Modules.Accounts.Domain;

public class Report
{
    public int Id { get; set; }
    
    public int ReporterId { get; set; }
    
    public string TargetType { get; set; } = string.Empty;
    
    public int TargetId { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    
    public int Status { get; set; }
    
    public DateTime ReportAt { get; set; } = DateTime.UtcNow;
}
