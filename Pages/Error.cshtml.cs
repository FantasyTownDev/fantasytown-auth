using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages;

/// <summary>
/// 全局异常兜底页（M1）：匹配 UseExceptionHandler("/Error")。
/// 重执行保留原 HTTP method（文档实证）——POST 请求异常会打到本页 POST，
/// 故 GET/POST handler 都必须存在，否则 405 使兜底链二次失效。
/// </summary>
public class ErrorModel : PageModel
{
    public void OnGet()
    {
    }

    public void OnPost()
    {
    }
}
