using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>原始报文管理的后端接口（FR-15）：检索、原文查看、手动标记、重新入队重放。</summary>
/// <remarks>
/// 页面（`WEB-6b`）将复用这组接口；接口只做参数校验与结果包装，过滤 / 分页 / 重放语义都在
/// <see cref="EventQueryService"/> 与 <see cref="EventReplayService"/>，前者可脱离 HTTP 单测。
///
/// 重放<b>不覆盖</b>任何已落库数据：新消息带 <c>replayOf</c>，<c>eventKey</c> 与原报文一致，
/// 消费端命中幂等只累加 <c>RepeatCount</c>（FR-24）。所有写操作都记审计日志并标注操作人。
/// </remarks>
[Route("api/admin/events")]
public sealed class AdminEventsController : AdminControllerBase
{
    private readonly EventQueryService _query;
    private readonly EventReplayService _replay;

    /// <summary>构造原始报文管理接口。</summary>
    /// <param name="query">只读检索服务。</param>
    /// <param name="replay">重放与标记服务。</param>
    public AdminEventsController(EventQueryService query, EventReplayService replay)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
    }

    /// <summary>按设备 / 事件 / 幂等键 / 处理状态 / 接收时间 / 关键词分页检索原始报文。</summary>
    /// <param name="query">检索条件，来自查询字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页列表（不含报文原文大字段）。</returns>
    [HttpGet]
    public async Task<ObjectResult> List([FromQuery] EventQuery query, CancellationToken cancellationToken)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Failure(400, "from 不能晚于 to");

        var data = await _query.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return Success(data);
    }

    /// <summary>读取一条原始报文的详情，含报文原文、<c>data</c>、扩展与未识别字段。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或 404。</returns>
    [HttpGet("{id:long}")]
    public async Task<ObjectResult> Get(Int64 id, CancellationToken cancellationToken)
    {
        var detail = await _query.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return detail == null ? Failure(404, "记录不存在") : Success(detail);
    }

    /// <summary>手动标记处理状态（0 未处理 / 1 已处理 / 2 忽略），不改动报文内容。</summary>
    /// <param name="id">主键。</param>
    /// <param name="request">标记内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标记结果或 404。</returns>
    [HttpPost("{id:long}/mark")]
    public async Task<ObjectResult> Mark(Int64 id, [FromBody] MarkRequest request, CancellationToken cancellationToken)
    {
        request ??= new MarkRequest();

        var ok = await _replay.MarkAsync(id, request.HandleStatus, request.Remark, OperatorName, cancellationToken).ConfigureAwait(false);
        return ok ? Success(new { id, request.HandleStatus }) : Failure(404, "记录不存在");
    }

    /// <summary>把一条原始报文重新写入共享流（广播所有消费组，带 <c>replayOf</c>）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新消息 ID 与 Stream 消息 ID，或失败原因。</returns>
    [HttpPost("{id:long}/replay")]
    public async Task<ObjectResult> Replay(Int64 id, CancellationToken cancellationToken)
    {
        var result = await _replay.ReplayEventAsync(id, OperatorName, cancellationToken).ConfigureAwait(false);
        return BuildResult(result);
    }

    /// <summary>把重放结果翻译成 HTTP 结果：不存在 404、前置条件不满足 409、队列异常 503。</summary>
    /// <param name="result">重放结果。</param>
    /// <returns>统一结构响应。</returns>
    private ObjectResult BuildResult(ReplayResult result)
    {
        if (result.Success) return Success(result);

        var status = result.Reason switch
        {
            "记录不存在" => 404,
            _ when result.Reason.StartsWith("Redis 未启用", StringComparison.Ordinal) => 409,
            _ when result.Reason.StartsWith("记录缺少报文原文", StringComparison.Ordinal) => 409,
            _ => 503,
        };

        return Failure(status, result.Reason);
    }
}

/// <summary>手动标记请求体。</summary>
public sealed class MarkRequest
{
    /// <summary>处理状态：0 未处理 / 1 已处理 / 2 忽略。</summary>
    public Int32 HandleStatus { get; set; }

    /// <summary>处理备注。</summary>
    public String Remark { get; set; }
}
