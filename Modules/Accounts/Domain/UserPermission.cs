namespace FantasyTown.Auth.Modules.Accounts.Domain;

public enum UserPermission : byte
{
    NormalPlayer = 0,
    ServerModerator = 1,
    ServerOwner = 2,
    PlatformAdmin = 3
}
