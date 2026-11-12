using EFBM.Core.Security;
using EFBM.Web.Security;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>后台操作审计查询（FR-15 / AUTH-3b）：谁在什么时候对哪条数据做了什么、成了没有。</summary>
/// <remarks>
/// 审计写在 Cube 的 <c>Log</c> 表（<see cref="CubeAuditStore"/>），所以后台「日志」页也能看到，
/// 这里是给页面用的结构化查询入口：按动作 / 操作人 / 时间区间 / 成功与否过滤 + 分页。
/// 只读与无关账号看不到审计（最低运维），避免「查别人干过什么」变成信息泄露。
/// </remarks>
[Route("api/admin/audits")]
public sealed class AdminAuditsController : AdminControllerBase
{
    private readonly IAuditStore _store;

    /// <summary>构造审计查询接口。</summary>
    /// <param name="store">审计存储。</param>
    /// <param name="identity">当前请求身份。</param>
    public AdminAuditsController(IAuditStore store, IAdminIdentityAccessor identity)
        : base(identity)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>分页查询后台操作审计。</summary>
    /// <param name="query">查询条件，来自查询字符串。</param>
    /// <returns>分页审计记录。</returns>
    [HttpGet]
    [RequireAdminOperation(AdminOperation.QueryAudits)]
    public ObjectResult List([FromQuery] AuditQuery query)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Failure(400, "from 不能晚于 to");

        return Success(_store.Query(query));
    }
}
