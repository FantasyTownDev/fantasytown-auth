using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;

    public RegisterModel(AuthDbContext db, IPasswordService passwordService)
    {
        _db = db;
        _passwordService = passwordService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.Password != Input.ConfirmPassword)
        {
            ErrorMessage = "两次输入的密码不一致";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var handler = new RegisterUserHandler(_db, _passwordService);
        var command = new RegisterUserCommand
        {
            Username = Input.Username,
            Password = Input.Password,
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email,
            ClientIp = clientIp
        };

        var result = await handler.HandleAsync(command);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // 注册成功后自动登录
        await CookieAuthService.SignInAsync(
            HttpContext,
            result.UserId!.Value,
            result.Username!,
            Modules.Accounts.Domain.UserPermission.NormalPlayer);

        return RedirectToPage("/Index");
    }
}
