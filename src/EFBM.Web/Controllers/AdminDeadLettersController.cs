using EFBM.Core.Security;
using EFBM.Web.Security;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>死信管理的后端接口（FR-12）：列表、详情、手动重放。</summary>
/// <remarks>
/// 死信<b>不会被自动丢弃</b>：消费者重试超限后先写死信再 <c>XACK</c>，由这里人工确认与重放。
/// 重放成功后回写 <c>ReplayStatus=1</c> / <c>ReplayAt</c> / <c>ReplayBy</c>；
/// 失败则保持未重放，运维可再次发起（重复重放的收敛由 <c>EventKey</c> 幂等兜住）。
/// </remarks>
[Route("api/admin/deadletters")]
public sealed class AdminDeadLettersController : AdminControllerBase
{
    private readonly EventQueryService _query;
    private readonly EventReplayService _replay;

    /// <summary>构造死信管理接口。</summary>
    /// <param name="query">只读检索服务。</param>
    /// <param name="replay">重放服务。</param>
    /// <param name="identity">当前请求身份。</param>
    public AdminDeadLettersController(EventQueryService query, EventReplayService replay, IAdminIdentityAccessor identity)
        : base(identity)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
    }

    /// <summary>按幂等键 / 消息 ID / 重放状态 / 创建时间分页查询死信。</summary>
    /// <param name="query">检索条件，来自查询字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页列表，含错误摘要与重放状态。</returns>
    [HttpGet]
    [RequireAdminOperation(AdminOperation.QueryDeadLetters)]
    public async Task<ObjectResult> List([FromQuery] DeadLetterQuery query, CancellationToken cancellationToken)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Failure(400, "from 不能晚于 to");

        var data = await _query.QueryDeadLettersAsync(query, cancellationToken).ConfigureAwait(false);
        return Success(data);
    }

    /// <summary>读取一条死信的完整记录（含消息体 JSON 与最后一次错误）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>死信详情或 404。</returns>
    [HttpGet("{id:long}")]
    [RequireAdminOperation(AdminOperation.ViewDeadLetterDetail)]
    public async Task<ObjectResult> Get(Int64 id, CancellationToken cancellationToken)
    {
        var entity = await _query.GetDeadLetterAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity == null) return Failure(404, "死信记录不存在");

        return Success(new
        {
            entity.Id,
            entity.MessageId,
            entity.StreamId,
            entity.EventKey,
            entity.RetryCount,
            entity.LastError,
            entity.ReplayStatus,
            entity.ReplayAt,
            entity.ReplayBy,
            entity.CreateTime,
            entity.RawJson,
        });
    }

    /// <summary>把一条死信重新写入共享流，并回写重放留痕。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新消息 ID 与 Stream 消息 ID，或失败原因。</returns>
    [HttpPost("{id:long}/replay")]
    [RequireAdminOperation(AdminOperation.ReplayDeadLetter)]
    public async Task<ObjectResult> Replay(Int64 id, CancellationToken cancellationToken)
    {
        var result = await _replay.ReplayDeadLetterAsync(id, OperatorName, cancellationToken).ConfigureAwait(false);
        if (result.Success) return Success(result);

        var status = result.Reason switch
        {
            "死信记录不存在" => 404,
            _ when result.Reason.StartsWith("Redis 未启用", StringComparison.Ordinal) => 409,
            _ when result.Reason.StartsWith("死信消息体已损坏", StringComparison.Ordinal) => 409,
            _ => 503,
        };

        return Failure(status, result.Reason);
    }
}
