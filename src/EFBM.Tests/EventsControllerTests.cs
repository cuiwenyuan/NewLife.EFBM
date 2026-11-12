using System.Net;
using System.Text;
using System.Text.Json;
using EFBM.Core;
using EFBM.Web.Controllers;
using EFBM.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Tests;

/// <summary>唯一上报端点的 HTTP 适配测试（需求说明 §3.1、FR-1 / FR-20、验收用例 C1 / C2 / C3）。</summary>
public class EventsControllerTests
{
    private const String Piece = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":1}}";

    /// <summary>构造控制器与可观测的队列替身。</summary>
    /// <param name="options">EFBM 配置。</param>
    /// <param name="log">请求日志替身；为空时新建一个。</param>
    /// <returns>控制器、队列、配置。</returns>
    private static (EventsController Controller, FakeEventQueue Queue, EfbmOptions Options) Create(EfbmOptions options = null, FakeRequestLog log = null)
    {
        options ??= new EfbmOptions();
        var credentials = new FakeCredentialStore().With(FakeCredentialStore.Valid());
        var queue = new FakeEventQueue();
        log ??= new FakeRequestLog();
        var ingest = new EventIngestService(options, queue, credentials, new FakeFallbackStore(), log);

        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/events";
        http.Request.Scheme = "http";
        http.Request.Headers.UserAgent = "FBox-Firmware/1.0";
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");

        var controller = new EventsController(ingest, options, log)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return (controller, queue, options);
    }

    /// <summary>填充请求体与默认凭据头。</summary>
    /// <param name="controller">控制器。</param>
    /// <param name="raw">报文原文。</param>
    /// <param name="key">AppKey 请求头名。</param>
    /// <param name="secret">AppSecret 请求头名。</param>
    private static void Fill(EventsController controller, String raw = Piece, String key = EfbmDefaults.AppKeyHeader, String secret = EfbmDefaults.AppSecretHeader)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        controller.Request.Headers[key] = "key-1";
        controller.Request.Headers[secret] = "secret-1";
    }

    [Fact(DisplayName = "端点_合法报文_返回200与冻结的统一响应体")]
    public async Task Post_Valid_ReturnsFrozenResponse()
    {
        var (controller, queue, _) = Create();
        Fill(controller);

        var result = await controller.Post(CancellationToken.None);
        var body = Assert.IsType<ApiResponse>(result.Value);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(0, body.Code);
        Assert.Equal("accepted", body.Message);
        var data = JsonSerializer.Serialize(body.Data);
        Assert.Contains("\"messageId\"", data);
        Assert.Contains("\"eventKey\"", data);
        Assert.Contains("\"queued\":true", data);
        Assert.Single(queue.Added);
    }

    [Fact(DisplayName = "端点_非法JSON_返回400且data为null")]
    public async Task Post_InvalidJson_Returns400()
    {
        var (controller, queue, _) = Create();
        Fill(controller, "{\"event\":\"ORDER_STARTED\",}");

        var result = await controller.Post(CancellationToken.None);
        var body = Assert.IsType<ApiResponse>(result.Value);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(400, body.Code);
        Assert.Null(body.Data);
        Assert.Empty(queue.Added);
    }

    [Fact(DisplayName = "端点_请求头改名_仍可正常鉴权")]
    public async Task Post_RenamedHeaders_StillAuthenticates()
    {
        var options = new EfbmOptions { AppKeyHeader = "X-Dev-Key", AppSecretHeader = "X-Dev-Secret" };
        var (controller, queue, _) = Create(options);
        Fill(controller, Piece, "X-Dev-Key", "X-Dev-Secret");

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(queue.Added);
    }

    [Fact(DisplayName = "端点_可信反代_来源IP取X-Forwarded-For首段")]
    public async Task Post_ForwardedFor_UsesFirstIp()
    {
        var (controller, queue, _) = Create();
        Fill(controller);
        controller.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.1";

        await controller.Post(CancellationToken.None);

        Assert.Equal("203.0.113.7", queue.Added[0].SourceIp);
    }

    [Fact(DisplayName = "端点_不信任反代_来源IP取TCP对端地址")]
    public async Task Post_UntrustedProxy_UsesRemoteIp()
    {
        var (controller, queue, _) = Create(new EfbmOptions { TrustForwardedHeaders = false });
        Fill(controller);
        controller.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        await controller.Post(CancellationToken.None);

        Assert.Equal("192.168.1.10", queue.Added[0].SourceIp);
    }

    [Fact(DisplayName = "端点_超过请求体上限_返回400并丢弃")]
    public async Task Post_TooLarge_Returns400()
    {
        var (controller, queue, _) = Create(new EfbmOptions { MaxBodyBytes = 16 });
        Fill(controller);

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Empty(queue.Added);
    }

    [Fact(DisplayName = "端点_Basic模式401_回送WWW-Authenticate质询头")]
    public async Task Post_BasicUnauthorized_SendsChallenge()
    {
        var (controller, _, _) = Create(new EfbmOptions { AuthMode = AuthModes.Basic });
        Fill(controller);

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        Assert.Contains("Basic", controller.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact(DisplayName = "端点_外网收到明文上报_仍正常接收不重定向")]
    public async Task Post_PlainHttpOnInternet_IsNotRejected()
    {
        var (controller, queue, _) = Create(new EfbmOptions { Deployment = "Internet", RequireHttps = true });
        Fill(controller);
        controller.Request.Headers["X-Forwarded-Proto"] = "http";

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(queue.Added);
    }

    [Fact(DisplayName = "端点_请求日志_记录方法路径来源IP协议与原文")]
    public async Task Post_WritesRequestLogWithHttpFields()
    {
        var log = new FakeRequestLog();
        var (controller, _, _) = Create(null, log);
        Fill(controller);

        await controller.Post(CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal("POST", entry.Method);
        Assert.Equal("/api/events", entry.Path);
        Assert.Equal("http", entry.Scheme);
        Assert.Equal("192.168.1.10", entry.SourceIp);
        Assert.Equal("FBox-Firmware/1.0", entry.UserAgent);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(Piece, entry.RawJson);
        Assert.False(String.IsNullOrEmpty(entry.TraceId));
    }

    [Fact(DisplayName = "端点_请求体超限_也写一条请求日志")]
    public async Task Post_TooLarge_IsStillLogged()
    {
        var log = new FakeRequestLog();
        var (controller, _, _) = Create(new EfbmOptions { MaxBodyBytes = 16 }, log);
        Fill(controller);

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(400, entry.StatusCode);
        Assert.Contains("omitted", entry.RawJson);
    }
}
