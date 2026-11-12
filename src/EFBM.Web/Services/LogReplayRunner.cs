using System.Text.Json;
using EFBM.Core;
using NewLife.Log;

namespace EFBM.Web.Services;

/// <summary>Beta 文本请求日志的回放补录工具（需求说明 §3.5、FR-11）。</summary>
/// <remarks>
/// 纯日志模式（<c>RedisEnabled=false</c>）期间收到的数据只在 <c>logs/api/api-yyyyMMdd.log</c> 里；
/// 本工具把其中 <c>status=200</c> 的请求原文逐条送进 <see cref="EventPersistService"/> 直接落库，
/// 不经过 Redis。幂等由 <c>EventKey</c> 兜住：已落库的行只累加 <c>RepeatCount</c>，重复回放安全。
///
/// 用法：<c>dotnet EFBM.Web.dll --replay &lt;日志文件或目录&gt;</c>；目录时按文件名顺序处理全部
/// <c>api-*.log</c>。单条失败不中断整批，结束后输出统计。
/// </remarks>
public static class LogReplayRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>执行回放并把统计写到控制台。</summary>
    /// <param name="serviceProvider">宿主服务提供者，须已注册消费链路组件。</param>
    /// <param name="path">日志文件或目录。</param>
    /// <returns>异步任务。</returns>
    public static async Task RunAsync(IServiceProvider serviceProvider, String path)
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("必须提供日志文件或目录", nameof(path));

        var persist = serviceProvider.GetRequiredService<EventPersistService>();
        var files = ResolveFiles(path);
        if (files.Count == 0)
        {
            Console.WriteLine($"未找到可回放的日志文件：{path}");
            return;
        }

        Int64 total = 0, persisted = 0, duplicates = 0, skipped = 0, failed = 0;

        foreach (var file in files)
        {
            XTrace.WriteLine("回放开始：{0}", file);
            String line;
            Int32 lineNo = 0;

            using var reader = new StreamReader(file, new System.Text.UTF8Encoding(false));
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lineNo++;
                if (String.IsNullOrWhiteSpace(line)) continue;

                // 一行一条 JSONL；坏行（手工编辑等）跳过不中断
                RequestLogLine entry;
                try { entry = JsonSerializer.Deserialize<RequestLogLine>(line, JsonOptions); }
                catch (JsonException) { skipped++; continue; }

                // 只回放接收成功的请求：400/401/403 的原文没有入流语义（401 行也没有 eventKey）
                if (entry == null || entry.Status != 200 || String.IsNullOrWhiteSpace(entry.Raw)) continue;

                total++;
                var message = BuildMessage(entry);
                if (message == null) { skipped++; continue; }

                try
                {
                    var outcome = await persist.PersistAsync(message, null, CancellationToken.None).ConfigureAwait(false);
                    switch (outcome)
                    {
                        case PersistOutcome.Persisted: persisted++; break;
                        case PersistOutcome.Duplicate: duplicates++; break;
                        default: skipped++; break;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    XTrace.WriteLine("回放失败 {0} 第 {1} 行 eventKey={2}: {3}: {4}",
                        Path.GetFileName(file), lineNo, message.EventKey, ex.GetType().Name, ex.Message);
                }
            }

            XTrace.WriteLine("回放完成：{0}", file);
        }

        Console.WriteLine();
        Console.WriteLine("回放统计：读取成功行 {0}，新落库 {1}，已存在跳过 {2}，无效行 {3}，失败 {4}",
            total, persisted, duplicates, skipped, failed);
        if (failed > 0) Console.WriteLine("存在失败行，处理完后可再次运行本工具（已落库的会自动跳过）");
    }

    /// <summary>把日志行还原为 Stream 消息体；解析失败返回 <c>null</c>。</summary>
    private static StreamMessage BuildMessage(RequestLogLine entry)
    {
        var context = new ParseContext
        {
            MessageId = entry.MessageId,
            ReceivedAt = entry.Ts == DateTime.MinValue ? DateTime.UtcNow : entry.Ts,
            SourceIp = entry.Ip,
        };

        if (!EventEnvelopeParser.TryParse(entry.Raw, context, out var envelope, out _)) return null;

        return StreamMessage.FromEnvelope(envelope);
    }

    /// <summary>解析文件参数：单文件直接用，目录按名排序取全部 api-*.log。</summary>
    private static IReadOnlyList<String> ResolveFiles(String path)
    {
        if (File.Exists(path)) return [path];

        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "api-*.log")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<String>();
    }

    /// <summary>请求日志行中回放需要的字段子集。</summary>
    private sealed class RequestLogLine
    {
        /// <summary>HTTP 状态码。</summary>
        public Int32 Status { get; set; }

        /// <summary>请求体原文。</summary>
        public String Raw { get; set; }

        /// <summary>消息追踪 ID。</summary>
        public String MessageId { get; set; }

        /// <summary>服务端接收时间。</summary>
        public DateTime Ts { get; set; }

        /// <summary>来源 IP。</summary>
        public String Ip { get; set; }
    }
}
