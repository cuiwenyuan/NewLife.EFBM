namespace EFBM.Core;

/// <summary>接入层交给鉴权器的原始输入，不依赖 <c>HttpContext</c>。</summary>
/// <remarks>控制器负责从请求头 / 报文提取这些值；Core 只做纯逻辑校验，便于单测与换宿主（FR-20）。</remarks>
public sealed class AuthRequest
{
    /// <summary>当前生效的认证模式，见 <see cref="AuthModes"/>。</summary>
    public String AuthMode { get; set; }

    /// <summary>是否外网部署；外网匿名一律拒绝（启动自检之外的第二道兜底）。</summary>
    public Boolean IsInternet { get; set; }

    /// <summary><c>X-App-Key</c> 请求头的值；Header 名由配置决定。</summary>
    public String AppKey { get; set; }

    /// <summary><c>X-App-Secret</c> 请求头的值。</summary>
    public String AppSecret { get; set; }

    /// <summary>原始 <c>Authorization</c> 请求头，Basic 模式使用。</summary>
    public String Authorization { get; set; }

    /// <summary>报文里的 <c>deviceId</c>；必须与凭据绑定设备一致。</summary>
    public String DeviceId { get; set; }
}

/// <summary>鉴权结果；<see cref="StatusCode"/> 为 0 表示通过，否则直接作为 HTTP 状态码返回。</summary>
public sealed class AuthResult
{
    /// <summary>HTTP 状态码：0 通过 / 401 凭据无效 / 403 凭据禁用或设备串用。</summary>
    public Int32 StatusCode { get; set; }

    /// <summary>结果描述，可写入响应与日志；<b>不得包含密钥</b>。</summary>
    public String Message { get; set; }

    /// <summary>是否通过。</summary>
    public Boolean IsSuccess => StatusCode == 0;

    /// <summary>构造通过结果。</summary>
    /// <returns>通过结果。</returns>
    public static AuthResult Ok() => new() { StatusCode = 0, Message = "ok" };

    /// <summary>构造 401：凭据缺失、格式非法、AppKey 不存在或密钥不匹配（统一文案，避免探测）。</summary>
    /// <param name="message">不含密钥的原因描述。</param>
    /// <returns>401 结果。</returns>
    public static AuthResult Unauthorized(String message) => new() { StatusCode = 401, Message = message ?? "invalid credential" };

    /// <summary>构造 403：凭据停用、设备串用或外网匿名。</summary>
    /// <param name="message">不含密钥的原因描述。</param>
    /// <returns>403 结果。</returns>
    public static AuthResult Forbidden(String message) => new() { StatusCode = 403, Message = message ?? "forbidden" };
}
