using System.Text;
using System.Text.Json;
using EFBM.Core;
using EFBM.Web.Logging;

namespace EFBM.Tests;

/// <summary>文本请求日志测试：单行 JSON、原文完整、脱敏、按日 / 按大小滚动、失败不抛出。</summary>
/// <remarks>
/// 直接写临时目录里的真实文件，不用内存替身——Beta 阶段这条链路是数据的唯一留痕，必须验证真实 IO。
/// 每个用例独立目录，结束时整体删除，不影响仓库与其它日志。
/// </remarks>
public class FileRequestLogTests
{
    /// <summary>创建独立临时目录。</summary>
    /// <returns>目录绝对路径。</returns>
    private static String NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "efbm-log-" + Guid.NewGuid().ToString("N"));

    /// <summary>构造一条日志条目。</summary>
    /// <param name="raw">请求体原文。</param>
    /// <param name="status">HTTP 状态码。</param>
    /// <returns>日志条目。</returns>
    private static RequestLogEntry Entry(String raw = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\"}", Int32 status = 200) => new()
    {
        ReceivedAt = DateTime.UtcNow,
        TraceId = "trace-1",
        Method = "POST",
        Path = "/api/events",
        Scheme = "http",
        ContentType = "application/json",
        UserAgent = "FBox-Firmware/1.0",
        SourceIp = "10.0.0.9",
        DeviceId = "FBOX-A01",
        Event = "ORDER_COMPLETED",
        MessageId = "m-1",
        EventKey = new String('a', 40),
        AuthMode = "AppKeyAppSecret",
        AppKey = "beta****01",
        StatusCode = status,
        Code = status == 200 ? 0 : status,
        Message = status == 200 ? "accepted" : "rejected",
        Queued = status == 200,
        ElapsedMs = 3,
        RawJson = raw,
    };

    [Fact(DisplayName = "请求日志_写入一行JSON_原文逐字保留且可被解析")]
    public async Task Write_SingleJsonLine_WithVerbatimRaw()
    {
        var dir = NewDirectory();
        var raw = "{\"event\":\"ORDER_COMPLETED\",\n\"deviceId\":\"FBOX-A01\"}";
        using (var log = new FileRequestLog(new RequestLogOptions { Directory = dir, FlushToDisk = false }, Path.GetTempPath()))
        {
            Assert.True(await log.WriteAsync(Entry(raw)));
        }

        var file = Assert.Single(Directory.GetFiles(dir, "api-*.log"));
        var lines = File.ReadAllLines(file, Encoding.UTF8);
        var line = Assert.Single(lines);

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("/api/events", root.GetProperty("path").GetString());
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.True(root.GetProperty("queued").GetBoolean());
        // 原文里的换行被 JSON 转义成 \n，因此一条记录仍是一行，且反解后逐字一致
        Assert.Equal(raw, root.GetProperty("raw").GetString());

        Directory.Delete(dir, true);
    }

    [Fact(DisplayName = "请求日志_关闭时不写文件且返回false")]
    public async Task Disabled_ReturnsFalseAndWritesNothing()
    {
        var dir = NewDirectory();
        using (var log = new FileRequestLog(new RequestLogOptions { Enabled = false, Directory = dir }, Path.GetTempPath()))
        {
            Assert.False(await log.WriteAsync(Entry()));
            Assert.False(log.GetStatus().Enabled);
        }

        Assert.False(Directory.Exists(dir));
    }

    [Fact(DisplayName = "请求日志_目录不可写_不抛异常且计入失败")]
    public async Task WriteFailure_DoesNotThrow_AndIsCounted()
    {
        var root = NewDirectory();
        Directory.CreateDirectory(root);

        // 用一个普通文件占住目录名，CreateDirectory 必然失败
        var blocker = Path.Combine(root, "blocker");
        File.WriteAllText(blocker, "x");

        using var log = new FileRequestLog(new RequestLogOptions { Directory = Path.Combine(blocker, "api") }, root);
        Assert.False(await log.WriteAsync(Entry()));

        var status = log.GetStatus();
        Assert.True(status.Failed > 0);
        Assert.False(String.IsNullOrEmpty(status.LastError));

        Directory.Delete(root, true);
    }

    [Fact(DisplayName = "请求日志_超过单文件上限_当日切序号文件")]
    public async Task Rotation_BySize_CreatesSecondFile()
    {
        var dir = NewDirectory();
        var big = new String('x', 400 * 1024);

        using (var log = new FileRequestLog(new RequestLogOptions { Directory = dir, MaxFileMB = 1, FlushToDisk = false }, Path.GetTempPath()))
        {
            for (var i = 0; i < 4; i++) Assert.True(await log.WriteAsync(Entry(big)));
        }

        var files = Directory.GetFiles(dir, "api-*.log");
        Assert.Equal(2, files.Length);

        Directory.Delete(dir, true);
    }

    [Fact(DisplayName = "请求日志_只清理过期且同前缀的文件")]
    public async Task Retention_DeletesOnlyExpiredMatchingFiles()
    {
        var dir = NewDirectory();
        Directory.CreateDirectory(dir);

        var expired = Path.Combine(dir, "api-20200101.log");
        var other = Path.Combine(dir, "other-20200101.log");
        File.WriteAllText(expired, "{}\n");
        File.WriteAllText(other, "{}\n");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-60));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-60));

        using (var log = new FileRequestLog(new RequestLogOptions { Directory = dir, RetentionDays = 30, FlushToDisk = false }, Path.GetTempPath()))
        {
            Assert.True(await log.WriteAsync(Entry()));
        }

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(other));      // 非本前缀的文件绝不删除

        Directory.Delete(dir, true);
    }

    [Fact(DisplayName = "请求日志_状态_报告条数当前文件与最后写入时间")]
    public async Task Status_ReportsCountsAndCurrentFile()
    {
        var dir = NewDirectory();
        using (var log = new FileRequestLog(new RequestLogOptions { Directory = dir, FlushToDisk = false }, Path.GetTempPath()))
        {
            await log.WriteAsync(Entry());
            await log.WriteAsync(Entry(status: 401));

            var status = log.GetStatus();
            Assert.True(status.Enabled);
            Assert.Equal(2, status.Written);
            Assert.Equal(0, status.Failed);
            Assert.True(File.Exists(status.CurrentFile));
            Assert.True(status.LastWriteAt > DateTime.UtcNow.AddMinutes(-1));
        }

        Directory.Delete(dir, true);
    }
}
