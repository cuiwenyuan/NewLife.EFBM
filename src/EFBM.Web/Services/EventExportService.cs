using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;
using XCode;

namespace EFBM.Web.Services;

/// <summary>导出凭证：证明「这批数据确实被导出过」，是清理操作的前置条件（FR-15）。</summary>
/// <remarks>
/// 凭证记录导出时的<b>时间范围 + 最大主键 + 条数 + 文件哈希</b>。清理必须凭凭证执行，因此
/// 「先导出再删」不是口头约定而是代码强制：没有凭证、凭证过期、文件丢失或哈希不符都会被拒绝。
/// 记录<b>最大主键</b>是为了防止导出后新到数据被顺手删掉——清理只删 <c>Id ≤ MaxId</c> 的部分。
/// </remarks>
public sealed class ExportTicket
{
    /// <summary>凭证 ID。</summary>
    public String Id { get; init; }

    /// <summary>导出范围下界（含），作用于服务端接收时间。</summary>
    public DateTime From { get; init; }

    /// <summary>导出范围上界（含）。</summary>
    public DateTime To { get; init; }

    /// <summary>导出时的最大主键；清理只删该主键及以前的数据。</summary>
    public Int64 MaxId { get; init; }

    /// <summary>导出条数。</summary>
    public Int32 RowCount { get; init; }

    /// <summary>导出文件路径。</summary>
    public String FilePath { get; init; }

    /// <summary>导出文件 SHA-256，清理前校验文件未被改动。</summary>
    public String Sha256 { get; init; }

    /// <summary>导出操作人。</summary>
    public String Operator { get; init; }

    /// <summary>导出时间。</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>凭证过期时间。</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>凭证是否已过期。</summary>
    /// <param name="now">当前时间。</param>
    /// <returns>已过期返回 <c>true</c>。</returns>
    public Boolean IsExpired(DateTime now) => now > ExpiresAt;
}

/// <summary>导出结果。</summary>
public sealed class ExportResult
{
    /// <summary>是否导出成功。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因；成功时为 <c>null</c>。</summary>
    public String Reason { get; init; }

    /// <summary>导出凭证 ID，清理时必填。</summary>
    public String TicketId { get; init; }

    /// <summary>导出文件名（下载接口用）。</summary>
    public String FileName { get; init; }

    /// <summary>导出文件绝对路径（运维直接拷走用）。</summary>
    public String FilePath { get; init; }

    /// <summary>导出条数。</summary>
    public Int32 RowCount { get; init; }

    /// <summary>文件 SHA-256。</summary>
    public String Sha256 { get; init; }

    /// <summary>凭证过期时间。</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="ticket">导出凭证。</param>
    /// <returns>成功结果。</returns>
    public static ExportResult Ok(ExportTicket ticket) => new()
    {
        Success = true,
        TicketId = ticket.Id,
        FileName = Path.GetFileName(ticket.FilePath),
        FilePath = ticket.FilePath,
        RowCount = ticket.RowCount,
        Sha256 = ticket.Sha256,
        ExpiresAt = ticket.ExpiresAt,
    };

    /// <summary>构造失败结果。</summary>
    /// <param name="reason">失败原因（面向运维，不含密钥）。</param>
    /// <returns>失败结果。</returns>
    public static ExportResult Fail(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>清理结果。</summary>
public sealed class CleanupResult
{
    /// <summary>是否执行成功。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因。</summary>
    public String Reason { get; init; }

    /// <summary>实际删除条数。</summary>
    public Int32 DeletedCount { get; init; }

    /// <summary>导出凭证记录的条数，与 <see cref="DeletedCount"/> 不一致说明期间已有数据被处理掉。</summary>
    public Int32 ExpectedCount { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="deleted">实际删除条数。</param>
    /// <param name="expected">凭证记录的条数。</param>
    /// <returns>成功结果。</returns>
    public static CleanupResult Ok(Int32 deleted, Int32 expected) => new()
    {
        Success = true,
        DeletedCount = deleted,
        ExpectedCount = expected,
    };

    /// <summary>构造失败结果。</summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>失败结果。</returns>
    public static CleanupResult Fail(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>原始报文的导出与手动清理（FR-15）。</summary>
/// <remarks>
/// <b>没有任何自动清理任务</b>：清理只能由后台凭导出凭证手动触发，避免「配额到了自动删数据」这类
/// 事故。导出为 CSV（UTF-8 带 BOM，Excel 直接打开不乱码），原文列完整保留，可用于离线审计与补录。
///
/// 凭证保存在<b>进程内存</b>：单实例 Beta 足够，应用重启后旧凭证失效（清理会被拒绝，重新导出即可）。
/// 这条保守策略是刻意的——宁可直接失败，也不让「凭证丢了还能删」发生。
/// </remarks>
public sealed class EventExportService
{
    /// <summary>单次导出最多条数，超过请缩小时间范围（避免一次导出拖垮进程内存与大文件）。</summary>
    public const Int32 MaxExportRows = 200_000;

    /// <summary>分页读取的批大小。</summary>
    private const Int32 ReadBatch = 1000;

    private static readonly String[] _columns =
    {
        "Id", "EventKey", "MessageId", "StreamId", "Event", "DeviceId",
        "EventTimeRaw", "EventTime", "ReceivedAt", "PersistedAt", "ProcessStatus",
        "RepeatCount", "ReplayOf", "SourceIp", "ParseVersion",
        "HandleStatus", "HandleBy", "HandleAt", "RawJson", "DataJson", "ExtJson", "UnknownFields",
    };

    private readonly EfbmOptions _options;
    private readonly Func<DateTime> _clock;
    private readonly ConcurrentDictionary<String, ExportTicket> _tickets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构造导出服务。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="clock">时间来源；默认 <see cref="DateTime.Now"/>，测试可注入以验证凭证过期。</param>
    public EventExportService(EfbmOptions options, Func<DateTime> clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>导出目录的绝对路径；相对路径以应用基目录为基准（与文本日志一致，避免受进程工作目录影响）。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <returns>绝对目录路径。</returns>
    public static String ResolveDirectory(EfbmOptions options)
    {
        var dir = options?.ExportDirectory;
        if (String.IsNullOrWhiteSpace(dir)) dir = "logs/export";

        return Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir);
    }

    /// <summary>按接收时间范围导出原始报文为 CSV。</summary>
    /// <param name="from">下界（含）。</param>
    /// <param name="to">上界（含）。</param>
    /// <param name="operatorName">操作人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出结果，含凭证 ID 与文件哈希。</returns>
    public async Task<ExportResult> ExportAsync(DateTime from, DateTime to, String operatorName, CancellationToken cancellationToken = default)
    {
        if (from > to) return ExportResult.Fail("from 不能晚于 to");

        var total = DeviceEventRaw.FindCount(DeviceEventRaw._.ReceivedAt >= from & DeviceEventRaw._.ReceivedAt <= to, null, null, 0, 0);
        if (total <= 0) return ExportResult.Fail("该时间范围内没有数据可导出");
        if (total > MaxExportRows) return ExportResult.Fail($"范围内 {total} 条超过单次导出上限 {MaxExportRows}，请缩小时间范围");

        var dir = ResolveDirectory(_options);
        Directory.CreateDirectory(dir);

        var stamp = _clock().ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(dir, $"events-{stamp}-{Guid.NewGuid():N}.csv");

        // UTF-8 带 BOM：Excel 双击打开中文与 JSON 不乱码
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var rows = 0;
        var maxId = 0L;

        using (var writer = new StreamWriter(path, append: false, encoding))
        {
            await writer.WriteLineAsync(String.Join(',', _columns)).ConfigureAwait(false);

            var offset = 0L;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = DeviceEventRaw.FindAll(
                    DeviceEventRaw._.ReceivedAt >= from & DeviceEventRaw._.ReceivedAt <= to,
                    DeviceEventRaw._.Id.Asc(), null, offset, ReadBatch);
                if (batch.Count == 0) break;

                foreach (var entity in batch)
                {
                    await writer.WriteLineAsync(ToCsvLine(entity)).ConfigureAwait(false);
                    rows++;
                    if (entity.Id > maxId) maxId = entity.Id;
                }

                offset += batch.Count;
            }
        }

        var sha = ComputeSha256(path);
        var now = _clock();
        var ticket = new ExportTicket
        {
            Id = Guid.NewGuid().ToString("N"),
            From = from,
            To = to,
            MaxId = maxId,
            RowCount = rows,
            FilePath = path,
            Sha256 = sha,
            Operator = operatorName,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Max(1, _options.ExportTicketMinutes)),
        };
        _tickets[ticket.Id] = ticket;

        XTrace.WriteLine("后台导出：范围={0:yyyy-MM-dd HH:mm:ss}~{1:yyyy-MM-dd HH:mm:ss} 条数={2} 最大Id={3} 凭证={4} 操作人={5} 文件={6}",
            from, to, rows, maxId, ticket.Id, operatorName, path);

        return ExportResult.Ok(ticket);
    }

    /// <summary>按凭证 ID 查找有效凭证（已过期视为不存在）。</summary>
    /// <param name="ticketId">凭证 ID。</param>
    /// <returns>凭证；不存在或已过期返回 <c>null</c>。</returns>
    public ExportTicket FindTicket(String ticketId)
    {
        if (String.IsNullOrWhiteSpace(ticketId)) return null;
        if (!_tickets.TryGetValue(ticketId, out var ticket)) return null;

        return ticket.IsExpired(_clock()) ? null : ticket;
    }

    /// <summary>凭导出凭证删除该范围已导出的原始报文（FR-15「先导出再删」）。</summary>
    /// <param name="ticketId">导出凭证 ID。</param>
    /// <param name="operatorName">操作人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清理结果。</returns>
    public Task<CleanupResult> CleanupAsync(String ticketId, String operatorName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ticket = FindTicket(ticketId);
        if (ticket == null) return Task.FromResult(CleanupResult.Fail("导出凭证不存在或已过期：请先导出再清理"));

        // 文件是「已备份」的唯一凭据：被挪走或改动过就不允许删库
        if (!File.Exists(ticket.FilePath))
            return Task.FromResult(CleanupResult.Fail("导出文件已丢失，拒绝清理：请重新导出"));
        if (!String.Equals(ComputeSha256(ticket.FilePath), ticket.Sha256, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(CleanupResult.Fail("导出文件已被修改，拒绝清理：请重新导出"));

        // 只删导出过的部分：Id ≤ 导出时最大主键，避免把导出后新到的数据一起删掉
        var exp = DeviceEventRaw._.ReceivedAt >= ticket.From
                  & DeviceEventRaw._.ReceivedAt <= ticket.To
                  & DeviceEventRaw._.Id <= ticket.MaxId;

        var deleted = DeviceEventRaw.Delete(exp);

        XTrace.WriteLine("后台清理：凭证={0} 范围={1:yyyy-MM-dd HH:mm:ss}~{2:yyyy-MM-dd HH:mm:ss} 最大Id={3} 导出条数={4} 实际删除={5} 操作人={6}",
            ticket.Id, ticket.From, ticket.To, ticket.MaxId, ticket.RowCount, deleted, operatorName);

        _tickets.TryRemove(ticket.Id, out _);

        return Task.FromResult(CleanupResult.Ok(deleted, ticket.RowCount));
    }

    /// <summary>实体转 CSV 行；所有字段按 RFC 4180 加引号转义。</summary>
    private static String ToCsvLine(DeviceEventRaw entity)
    {
        var values = new Object[]
        {
            entity.Id, entity.EventKey, entity.MessageId, entity.StreamId, entity.Event, entity.DeviceId,
            entity.EventTimeRaw, entity.EventTime, entity.ReceivedAt, entity.PersistedAt, entity.ProcessStatus,
            entity.RepeatCount, entity.ReplayOf, entity.SourceIp, entity.ParseVersion,
            entity.HandleStatus, entity.HandleBy, entity.HandleAt, entity.RawJson, entity.DataJson, entity.ExtJson, entity.UnknownFields,
        };

        var sb = new StringBuilder(256);
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');

            var text = values[i] switch
            {
                null => String.Empty,
                DateTime dt => dt == DateTime.MinValue ? String.Empty : dt.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => values[i].ToString(),
            };

            sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        }

        return sb.ToString();
    }

    /// <summary>计算文件 SHA-256（小写十六进制）。</summary>
    private static String ComputeSha256(String path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
