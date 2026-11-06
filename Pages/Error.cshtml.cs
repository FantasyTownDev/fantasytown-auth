// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

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
