// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using Microsoft.AspNetCore.Http;

namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 客户端 IP 提取抽象，支持受信任代理头解析。
/// 实现读取 HttpContext.Connection.RemoteIpAddress，
/// 在配置了 KnownProxies 的反向代理后获取真实客户端 IP。
/// </summary>
public interface IClientIp
{
    /// <summary>
    /// 获取当前 HTTP 上下文中的可信客户端 IP 地址。
    /// </summary>
    string? GetClientIp();
}

/// <summary>
/// 从 HttpContext 提取客户端 IP 的默认实现。
/// </summary>
public sealed class HttpContextClientIp : IClientIp
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextClientIp(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? GetClientIp()
    {
        return _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }
}
