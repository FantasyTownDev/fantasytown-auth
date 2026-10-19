using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ResetPasswordModel(AuthDbContext db, IPasswordService passwordService, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _passwordService = passwordService;
        _dataProtectionProvider = dataProtectionProvider;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            Input.Token = token;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ErrorMessage = "两次输入的密码不一致";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var handler = new PasswordResetHandler(_db, _passwordService, _dataProtectionProvider);
        var command = new ResetPasswordCommand
        {
            Token = Input.Token,
            NewPassword = Input.NewPassword
        };

        var result = await handler.HandleResetAsync(command);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        SuccessMessage = "密码重置成功！正在跳转到登录页面...";
        return RedirectToPage("/Account/Login");
    }
}
