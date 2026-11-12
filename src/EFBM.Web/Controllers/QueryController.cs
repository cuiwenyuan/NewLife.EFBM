using EFBM.Core;
using EFBM.Web.Models;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using XCode.Membership;

namespace EFBM.Web.Controllers;

/// <summary>统一查询统计 API（FR-16、WEB-11a）：设备状态、工单进度、逐件明细、产量、故障与 Stream 积压。</summary>
/// <remarks>
/// 与上报端点分开鉴权：设备用 <c>AppKey/AppSecret</c> 证明「我是这台设备」，
/// 查询方用 <c>X-Query-Key</c> 证明「我被允许看生产数据」。两者都不是后台登录体系的一部分，
/// 但后台已登录用户可以直接调用，便于运维在浏览器里核对同一份数据。
///
/// 未配置 <see cref="EfbmOptions.QueryApiKey"/> 时<b>不放开</b>：只允许后台登录用户访问，
/// 外部调用得到 503 与明确提示——裸奔的查询接口等于把全量报文交给互联网。
/// 返回体沿用冻结的 <c>{code,message,data}</c>，字段名用 <c>camelCase</c>。
/// </remarks>
/// <example>
/// <code>
/// curl "http://127.0.0.1:5000/api/query/devices?page=1&amp;pageSize=20" -H "X-Query-Key: 少于16位会被外网自检拒绝"
/// </code>
/// </example>
[ApiController]
[AllowAnonymous]
[Route(EfbmDefaults.QueryRoute)]
public sealed class QueryController : ControllerBase
{
    private readonly QueryApiService _service;
    private readonly EfbmOptions _options;
    private readonly IQueueMetricsProbe _probe;

    /// <summary>构造查询控制器。</summary>
    /// <param name="service">查询数据层。</param>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="probe">消费组指标探测；纯日志模式下可为 <c>null</c>。</param>
    public QueryController(QueryApiService service, EfbmOptions options, IQueueMetricsProbe probe = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probe = probe;
    }

    /// <summary>分页查询设备状态（原值 + 字典中文 + 最后上报时间）。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="enabled">是否启用。</param>
    /// <param name="statusRaw">状态原值。</param>
    /// <param name="from">最后上报时间下界（UTC）。</param>
    /// <param name="to">最后上报时间上界（UTC）。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，最大 200。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备状态分页。</returns>
    [HttpGet("devices")]
    public async Task<ObjectResult> GetDevices(String deviceId, Boolean? enabled, String statusRaw, DateTime? from, DateTime? to, Int32 page = 1, Int32 pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.QueryDevicesAsync(new DeviceQuery
        {
            DeviceId = deviceId,
            Enabled = enabled,
            StatusRaw = statusRaw,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Success(data);
    }

    /// <summary>按设备编号读取单台设备状态。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备状态；不存在返回 404。</returns>
    [HttpGet("devices/{deviceId}")]
    public async Task<ObjectResult> GetDevice(String deviceId, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        return data == null ? Fail(404, "设备不存在") : Success(data);
    }

    /// <summary>分页查询工单进度。</summary>
    /// <param name="orderId">工单号。</param>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="status">派生状态。</param>
    /// <param name="from">开工时间下界（UTC）。</param>
    /// <param name="to">开工时间上界（UTC）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工单分页。</returns>
    [HttpGet("orders")]
    public async Task<ObjectResult> GetOrders(String orderId, String deviceId, String status, DateTime? from, DateTime? to, Int32 page = 1, Int32 pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.QueryOrdersAsync(new WorkOrderQuery
        {
            OrderId = orderId,
            DeviceId = deviceId,
            Status = status,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Success(data);
    }

    /// <summary>按工单号读取单个工单进度。</summary>
    /// <param name="orderId">工单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工单进度；不存在返回 404。</returns>
    [HttpGet("orders/{orderId}")]
    public async Task<ObjectResult> GetOrder(String orderId, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.GetOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        return data == null ? Fail(404, "工单不存在") : Success(data);
    }

    /// <summary>分页查询逐件完成明细。</summary>
    /// <param name="orderId">工单号。</param>
    /// <param name="boxId">盒型 / 盒号。</param>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="from">事件时间下界（UTC）。</param>
    /// <param name="to">事件时间上界（UTC）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>明细分页。</returns>
    [HttpGet("production")]
    public async Task<ObjectResult> GetProduction(String orderId, String boxId, String deviceId, DateTime? from, DateTime? to, Int32 page = 1, Int32 pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.QueryProductionAsync(new ProductionQuery
        {
            OrderId = orderId,
            BoxId = boxId,
            DeviceId = deviceId,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Success(data);
    }

    /// <summary>按工单或设备汇总产量。</summary>
    /// <param name="groupBy">统计维度：<c>order</c> 或 <c>device</c>。</param>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="orderId">工单号。</param>
    /// <param name="from">事件时间下界（UTC）。</param>
    /// <param name="to">事件时间上界（UTC）。</param>
    /// <param name="scanLimit">扫描行数上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>产量汇总；被截断时 <c>truncated=true</c>，数值只是下限。</returns>
    [HttpGet("output")]
    public async Task<ObjectResult> GetOutput(String groupBy, String deviceId, String orderId, DateTime? from, DateTime? to, Int32 scanLimit = 0, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.QueryOutputAsync(new OutputQuery
        {
            GroupBy = groupBy,
            DeviceId = deviceId,
            OrderId = orderId,
            From = from,
            To = to,
            ScanLimit = scanLimit,
        }, cancellationToken).ConfigureAwait(false);

        return Success(data);
    }

    /// <summary>分页查询故障记录，含持续时长。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="status">状态：0 未清除 / 1 已清除 / 2 手动关闭。</param>
    /// <param name="openOnly">是否只看未清除。</param>
    /// <param name="from">发生时间下界（UTC）。</param>
    /// <param name="to">发生时间上界（UTC）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>故障分页。</returns>
    [HttpGet("faults")]
    public async Task<ObjectResult> GetFaults(String deviceId, Int32? status, Boolean openOnly = false, DateTime? from = null, DateTime? to = null, Int32 page = 1, Int32 pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.QueryFaultsAsync(new FaultQuery
        {
            DeviceId = deviceId,
            Status = status,
            OpenOnly = openOnly,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Success(data);
    }

    /// <summary>读取共享流积压：各组 lag / pending 与僵尸组判定。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>积压视图；Redis 不可用时 <c>connected=false</c> 并带原因。</returns>
    [HttpGet("stream")]
    public async Task<ObjectResult> GetStream(CancellationToken cancellationToken = default)
    {
        if (!Authorize(out var denied)) return denied;

        var data = await _service.GetStreamBacklogAsync(_probe, _options, cancellationToken).ConfigureAwait(false);
        return Success(data);
    }

    /// <summary>校验访问权限：IP 白名单 → 查询密钥 → 后台登录态。</summary>
    /// <param name="denied">拒绝时返回的响应。</param>
    /// <returns>放行返回 <c>true</c>。</returns>
    private Boolean Authorize(out ObjectResult denied)
    {
        denied = null;

        if (!AccessPolicy.IsIpAllowed(ResolveSourceIp(), AccessPolicy.ParseList(_options.AllowedIps)))
        {
            denied = Fail(403, "来源 IP 不在白名单内");
            return false;
        }

        var expected = _options.QueryApiKey?.Trim();
        if (!String.IsNullOrEmpty(expected))
        {
            var actual = Request.Headers[_options.QueryApiKeyHeader].FirstOrDefault();
            if (!AccessPolicy.SecureEquals(expected, actual))
            {
                denied = Fail(401, $"查询密钥缺失或不正确（请求头 {_options.QueryApiKeyHeader}）");
                return false;
            }

            return true;
        }

        // 没配密钥时只放行后台登录用户：宁可让外部系统报错，也不裸露生产数据
        if (ManageProvider.User == null)
        {
            denied = Fail(503, "查询接口未配置访问密钥（EFBM:QueryApiKey），当前仅限后台登录用户调用");
            return false;
        }

        return true;
    }

    /// <summary>成功响应。</summary>
    /// <param name="data">数据。</param>
    /// <returns>统一响应体。</returns>
    private ObjectResult Success(Object data) => Ok(new ApiResponse { Code = 0, Message = "ok", Data = data });

    /// <summary>失败响应。</summary>
    /// <param name="statusCode">HTTP 状态码，同时作为业务码。</param>
    /// <param name="message">文案。</param>
    /// <returns>统一响应体。</returns>
    private ObjectResult Fail(Int32 statusCode, String message) =>
        StatusCode(statusCode, new ApiResponse { Code = statusCode, Message = message, Data = null });

    /// <summary>解析来源 IP：可信反代时优先 <c>X-Forwarded-For</c> 首段。</summary>
    /// <returns>来源 IP。</returns>
    private String ResolveSourceIp()
    {
        if (_options.TrustForwardedHeaders)
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!String.IsNullOrEmpty(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (first.Length > 0) return first;
            }
        }

        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null) return String.Empty;
        return remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4().ToString() : remote.ToString();
    }
}
