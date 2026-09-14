using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;

    public ForgotPasswordModel(AuthDbContext db, IPasswordService passwordService)
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
        public string Email { get; set; } = string.Empty;
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

        var handler = new PasswordResetHandler(_db, _passwordService);
        var command = new RequestPasswordResetCommand
        {
            Email = Input.Email
        };

        var result = await handler.HandleRequestAsync(command);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        // 安全考虑：无论邮箱是否存在都显示成功消息
        SuccessMessage = "如果该邮箱已注册，您将收到一封密码重置邮件";
        return Page();
    }
}
