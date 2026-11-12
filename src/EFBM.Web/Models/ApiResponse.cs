using System.Text.Json.Serialization;
using EFBM.Core;

namespace EFBM.Web.Models;

/// <summary>冻结的统一响应体 <c>{code, message, data}</c>（需求说明 §3.1）。</summary>
/// <remarks>
/// 用 <see cref="JsonPropertyNameAttribute"/> 显式固定小写字段名，不依赖宿主的 camelCase 策略：
/// 无论 MVC、Minimal API 还是二期换轻量宿主，设备看到的字段名都一致。
/// 成功：<c>code=0</c>、<c>message=accepted</c>、<c>data={messageId,eventKey,queued,logged}</c>；
/// 失败：<c>code</c> 等于 HTTP 状态码、<c>data=null</c>。降级时结构不变，仅 <c>queued=false</c>。
/// 设备只按 HTTP code 判定成功，因此任何情况下都不新增<b>必读</b>字段；<c>logged</c> 是给运维看的附加信息。
/// </remarks>
public sealed class ApiResponse
{
    /// <summary>业务码：成功为 0，失败与 HTTP 状态码一致。</summary>
    [JsonPropertyName("code")]
    public Int32 Code { get; set; }

    /// <summary>结果文案，不含密钥与内部路径。</summary>
    [JsonPropertyName("message")]
    public String Message { get; set; }

    /// <summary>成功时的 <c>messageId</c> / <c>eventKey</c> / <c>queued</c> / <c>logged</c>；失败为 <c>null</c>。</summary>
    [JsonPropertyName("data")]
    public Object Data { get; set; }

    /// <summary>由接收链路结果组装响应。</summary>
    /// <param name="result">接收结果。</param>
    /// <returns>可序列化的统一响应体。</returns>
    public static ApiResponse From(IngestResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return new ApiResponse
        {
            Code = result.Code,
            Message = result.Message,
            Data = result.IsSuccess
                ? new { messageId = result.MessageId, eventKey = result.EventKey, queued = result.Queued, logged = result.Logged }
                : null,
        };
    }
}
