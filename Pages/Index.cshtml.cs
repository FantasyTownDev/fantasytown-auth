// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyTown.Auth.Pages;

public class IndexModel : PageModel
{
    private readonly IPermissionSnapshot _permissionSnapshot;

    public IndexModel(IPermissionSnapshot permissionSnapshot)
        => _permissionSnapshot = permissionSnapshot;

    /// <summary>特权用户显示 /admin 跳转入口（V5 §7.0：普通页面不内嵌任何管理表单）</summary>
    public bool ShowAdminEntry { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // PERM 快照实时判定：撤权/封禁/软删即刻反映；封禁哨兵不渲染入口
        var snapshot = User.GetUid() is { } uid
            ? await _permissionSnapshot.GetAsync(uid, cancellationToken)
            : null;
        ShowAdminEntry = snapshot is { IsBanned: false }
            && snapshot.Permission is UserPermission.ServerModerator
                or UserPermission.ServerOwner
                or UserPermission.PlatformAdmin;
    }
}
