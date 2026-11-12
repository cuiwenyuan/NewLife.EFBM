using System.Security.Cryptography;
using System.Text;

namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>三模式设备凭据鉴权（需求说明 §3.1、FR-2）。</summary>
/// <remarks>
/// 认证发生在 Cube 权限体系之外：上报端点标记 <c>[AllowAnonymous]</c>，由本类独立校验设备凭据，
/// 并强制校验凭据绑定的 <c>DeviceId</c> 与报文一致，防止一台设备冒用另一台的凭据。
///
/// Basic 模式复用同一张 <c>AppCredential</c>：用户名 = AppKey、密码 = AppSecret，
/// 因此「凭据按设备发放」在三种模式下语义一致，后台只需维护一套凭据。
///
/// 错误分类：401 = 凭据缺失 / 格式非法 / 不存在 / 密钥不匹配（统一文案，避免被探测）；
/// 403 = 凭据停用 / 设备串用 / 外网匿名。设备失败不重发，宿主必须对 401/403 记高优告警。
/// </remarks>
/// <example>
/// <code>
/// var authenticator = new RequestAuthenticator(credentialStore);
/// var result = await authenticator.AuthenticateAsync(new AuthRequest
/// {
///     AuthMode = AuthModes.AppKeyAppSecret,
///     AppKey = "FBOX-A01-KEY", AppSecret = "secret", DeviceId = "FBOX-A01",
/// });
/// if (!result.IsSuccess) return StatusCode(result.StatusCode, ...);
/// </code>
/// </example>
public sealed class RequestAuthenticator
{
    private readonly IDeviceCredentialStore _store;

    /// <summary>构造鉴权器。</summary>
    /// <param name="store">凭据读取实现；匿名模式下不会被调用。</param>
    public RequestAuthenticator(IDeviceCredentialStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>按配置的认证模式校验一次上报请求。</summary>
    /// <param name="request">从 HTTP 请求提取出的鉴权输入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>鉴权结果；通过时 <see cref="AuthResult.IsSuccess"/> 为 <c>true</c>。</returns>
    public async Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var mode = AuthModes.Normalize(request.AuthMode);

        // 匿名模式不查库：内网联调 / 灰度用。外网即使配置被绕过也在这里二次拦截
        if (String.Equals(mode, AuthModes.Anonymous, StringComparison.OrdinalIgnoreCase))
        {
            if (request.IsInternet) return AuthResult.Forbidden("外网部署禁止匿名上报");
            return AuthResult.Ok();
        }

        String appKey;
        String appSecret;
        if (String.Equals(mode, AuthModes.Basic, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseBasic(request.Authorization, out appKey, out appSecret))
                return AuthResult.Unauthorized("Basic 凭据缺失或格式非法");
        }
        else if (String.Equals(mode, AuthModes.AppKeyAppSecret, StringComparison.OrdinalIgnoreCase))
        {
            appKey = request.AppKey;
            appSecret = request.AppSecret;
        }
        else
        {
            // 未知模式由 EfbmOptions.ValidateOrThrow 在启动时拦截；这里兜底，不暴露配置细节
            return AuthResult.Unauthorized("认证模式配置非法");
        }

        if (String.IsNullOrWhiteSpace(appKey) || String.IsNullOrEmpty(appSecret))
            return AuthResult.Unauthorized("设备凭据缺失");

        DeviceCredential credential = null;
        try
        {
            credential = await _store.FindByAppKeyAsync(appKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // 凭据库不可用不能当成「凭据错误」：设备不重发，必须留高优告警线索
            return AuthResult.Unauthorized("凭据服务不可用");
        }

        // 统一 401 文案，避免通过错误差异枚举出有效 AppKey
        if (credential == null || !SecureEquals(credential.AppSecret, appSecret))
            return AuthResult.Unauthorized("设备凭据无效");

        if (!credential.Enabled)
            return AuthResult.Forbidden("设备凭据已停用");

        if (!String.Equals(credential.DeviceId, request.DeviceId, StringComparison.Ordinal))
            return AuthResult.Forbidden("凭据与报文 deviceId 不匹配");

        return AuthResult.Ok();
    }

    /// <summary>解析 HTTP Basic 的 <c>Authorization</c> 头。</summary>
    /// <param name="header">原始请求头值。</param>
    /// <param name="appKey">输出：用户名，作为 AppKey。</param>
    /// <param name="appSecret">输出：密码，作为 AppSecret。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    private static Boolean TryParseBasic(String header, out String appKey, out String appSecret)
    {
        appKey = null;
        appSecret = null;

        const String prefix = "Basic ";
        if (String.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var token = header[prefix.Length..].Trim();
        if (token.Length == 0) return false;

        String text;
        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException) { return false; }

        var idx = text.IndexOf(':');
        if (idx <= 0) return false;

        appKey = text[..idx];
        appSecret = text[(idx + 1)..];
        return true;
    }

    /// <summary>定长时间比较，避免通过响应耗时逐字节猜测 AppSecret。</summary>
    /// <param name="expected">库中密钥。</param>
    /// <param name="actual">请求携带密钥。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    private static Boolean SecureEquals(String expected, String actual)
    {
        if (expected == null || actual == null) return false;

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        if (left.Length != right.Length) return false;

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
