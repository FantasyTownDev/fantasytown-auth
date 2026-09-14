using FantasyTown.Auth.Modules.Accounts.Application.Queries;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class LoginModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionSnapshot _permissionSnapshot;

    public LoginModel(
        AuthDbContext db,
        IPasswordService passwordService,
        IPermissionSnapshot permissionSnapshot)
    {
        _db = db;
        _passwordService = passwordService;
        _permissionSnapshot = permissionSnapshot;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var handler = new LoginHandler(_db, _passwordService, _permissionSnapshot);
        var query = new LoginQuery
        {
            Username = Input.Username,
            Password = Input.Password,
            ClientIp = clientIp
        };

        var result = await handler.HandleAsync(query);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // TODO: 设置认证 Cookie
        return RedirectToPage("/Index");
    }
}
