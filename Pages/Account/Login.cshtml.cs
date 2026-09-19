using FantasyTown.Auth.Middleware;
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

    public void OnGet(string? banned = null)
    {
        if (banned == "true")
        {
            ErrorMessage = "您的账户已被封禁，请联系管理员";
        }
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

        // 设置认证 Cookie
        await CookieAuthService.SignInAsync(
            HttpContext,
            result.UserId!.Value,
            result.Username!,
            result.Permission!.Value);

        return RedirectToPage("/Index");
    }
}
