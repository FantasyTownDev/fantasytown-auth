using FantasyTown.Auth.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Persistence;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }
    
    public DbSet<User> Users => Set<User>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerBan> PlayerBans => Set<PlayerBan>();
    public DbSet<AuthLog> AuthLogs => Set<AuthLog>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Notification> Notifications => Set<Notification>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Uid);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.Ip).HasMaxLength(45);
            entity.Property(e => e.BannedReason).HasMaxLength(255);
            entity.Property(e => e.SecurityStamp).HasMaxLength(64);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });
        
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Pid);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.Uid).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.Uuid).HasMaxLength(32);
            entity.HasOne(e => e.User)
                .WithOne(u => u.Player)
                .HasForeignKey<Player>(e => e.Uid)
                .OnDelete(DeleteBehavior.Cascade);
            // 匹配 User 的查询过滤器，确保 Player 也不包含已删除用户的记录
            entity.HasQueryFilter(e => EF.Property<User>(e, "User") == null || !EF.Property<User>(e, "User").IsDeleted);
        });
        
        modelBuilder.Entity<PlayerBan>(entity =>
        {
            entity.HasKey(e => e.BanId);
            entity.HasIndex(e => e.Pid);
            entity.Property(e => e.BannedReason).HasMaxLength(255);
            entity.HasOne(e => e.Player)
                .WithMany(p => p.PlayerBans)
                .HasForeignKey(e => e.Pid)
                .OnDelete(DeleteBehavior.Cascade);
            // 匹配 Player 的查询过滤器
            entity.HasQueryFilter(e => EF.Property<Player>(e, "Player") == null || EF.Property<User>(EF.Property<Player>(e, "Player"), "User") == null || !EF.Property<User>(EF.Property<Player>(e, "Player"), "User").IsDeleted);
        });
        
        modelBuilder.Entity<AuthLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SubjectId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Action).HasMaxLength(50);
            entity.Property(e => e.Ip).HasMaxLength(45);
        });
        
        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TargetType).HasMaxLength(20);
        });
        
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.NotifiableType).HasMaxLength(50);
        });
    }
}
