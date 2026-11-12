using System.Text.Json;
using EFBM.Core;
using EFBM.Web.Models;

namespace EFBM.Tests;

/// <summary>统一响应体契约测试：成功 / 失败 / 降级三种形态字段不变。</summary>
public class ApiResponseTests
{
    [Fact(DisplayName = "响应_成功_含messageId与eventKey且queued为true")]
    public void Accepted_HasFrozenDataShape()
    {
        var result = IngestResult.Accepted(new StreamMessage { MessageId = "m-1", EventKey = "k-1" }, true, false);

        var response = ApiResponse.From(result);
        var json = JsonSerializer.Serialize(response);

        Assert.Equal(0, response.Code);
        Assert.Contains("\"code\":0", json);
        Assert.Contains("\"message\":\"accepted\"", json);
        Assert.Contains("\"messageId\":\"m-1\"", json);
        Assert.Contains("\"eventKey\":\"k-1\"", json);
        Assert.Contains("\"queued\":true", json);
    }

    [Fact(DisplayName = "响应_降级_结构不变仅queued为false")]
    public void Fallback_KeepsSameShape()
    {
        var result = IngestResult.Accepted(new StreamMessage { MessageId = "m-1", EventKey = "k-1" }, false, true);

        var response = ApiResponse.From(result);
        var json = JsonSerializer.Serialize(response);

        Assert.Equal(0, response.Code);
        Assert.Equal("accepted-by-fallback", response.Message);
        Assert.Contains("\"queued\":false", json);
        Assert.Contains("\"messageId\"", json);
    }

    [Fact(DisplayName = "响应_仅日志兜住_queued为false且logged为true")]
    public void LogOnly_ReportsQueuedFalseAndLoggedTrue()
    {
        var result = IngestResult.Accepted(new StreamMessage { MessageId = "m-1", EventKey = "k-1" }, false, false, "redis down", true);

        var response = ApiResponse.From(result);
        var json = JsonSerializer.Serialize(response);

        Assert.Equal(0, response.Code);
        Assert.Equal("accepted-by-log", response.Message);
        Assert.Contains("\"queued\":false", json);
        Assert.Contains("\"logged\":true", json);
    }

    [Theory(DisplayName = "响应_各类失败_code等于HTTP码且data为null")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    public void Failures_HaveNullData(Int32 status)
    {
        var response = ApiResponse.From(IngestResult.Rejected(status, "rejected"));

        Assert.Equal(status, response.Code);
        Assert.Null(response.Data);
        Assert.DoesNotContain("\"data\":{", JsonSerializer.Serialize(response));
    }

    [Fact(DisplayName = "响应_空结果_抛出ArgumentNullException")]
    public void From_Null_Throws() => Assert.Throws<ArgumentNullException>(() => ApiResponse.From(null));
}
