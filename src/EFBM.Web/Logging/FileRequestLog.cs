using System.Text;
using EFBM.Core;

namespace EFBM.Web.Logging;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>按天滚动的文本请求日志（JSONL）：Beta 阶段每条上报的磁盘留痕。</summary>
/// <remarks>
/// 为什么是文本文件而不是数据库：设备失败不重发，任何一条都不能丢；文件追加是最不依赖外部组件的落盘方式，
/// 机器只要能写磁盘就能收数据。数据库落库（P3）上线后它继续作为审计与人工补录源。
///
/// 设计要点：
/// ① 每天一个文件 <c>api-yyyyMMdd.log</c>，超过 <c>MaxFileMB</c> 当日切 <c>-002</c>；
/// ② 每次写入后 <see cref="RequestLogOptions.FlushToDisk"/> 为真则 <c>fsync</c>，断电也不丢；
/// ③ 单一信号量串行化写入，避免多请求交错写坏行；
/// ④ <b>任何异常都不得抛给调用方</b>——日志坏了不能反过来让接收链路失败，只累计失败计数供 <c>/health</c> 暴露；
/// ⑤ 只清理本目录、本前缀、超过保留期的 <c>*.log</c>，绝不碰其它文件。
/// </remarks>
public sealed class FileRequestLog : IRequestLog, IRequestLogProbe, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly RequestLogOptions _options;
    private readonly Int64 _maxBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private FileStream _stream;
    private StreamWriter _writer;
    private DateTime _fileDate;
    private Int32 _sequence;
    private Int64 _bytesInFile;
    private String _currentFile;

    private Int64 _written;
    private Int64 _failed;
    private String _lastError;
    private Int64 _lastWriteTicks;

    /// <summary>按宿主内容根目录解析日志目录，避免相对路径随进程工作目录漂移。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="environment">宿主环境，提供内容根目录。</param>
    public FileRequestLog(EfbmOptions options, IHostEnvironment environment)
        : this((options ?? throw new ArgumentNullException(nameof(options))).RequestLog ?? new RequestLogOptions(),
              (environment ?? throw new ArgumentNullException(nameof(environment))).ContentRootPath)
    {
    }

    /// <summary>以指定根目录构造，便于测试直接指向临时目录。</summary>
    /// <param name="options">请求日志配置。</param>
    /// <param name="rootPath">相对目录的基准路径（通常是内容根目录）。</param>
    public FileRequestLog(RequestLogOptions options, String rootPath)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (String.IsNullOrWhiteSpace(rootPath)) throw new ArgumentNullException(nameof(rootPath));

        Directory = Path.IsPathRooted(_options.Directory)
            ? _options.Directory
            : Path.GetFullPath(Path.Combine(rootPath, _options.Directory));
        _maxBytes = Math.Max(1, _options.MaxFileMB) * 1024L * 1024L;

        if (!_options.Enabled) return;

        // 目录建不出来（权限 / 磁盘）不阻断启动：上报仍可走 Redis / 兜底，状态由 /health 暴露
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            CleanupExpired();
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
    }

    /// <summary>日志目录绝对路径。</summary>
    public String Directory { get; }

    /// <summary>写入一条请求日志。</summary>
    /// <param name="entry">日志条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功落盘返回 <c>true</c>。</returns>
    public async Task<Boolean> WriteAsync(RequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || entry == null) return false;

        try
        {
            var line = entry.ToJson(_options.IncludeRaw);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                WriteLine(line);
            }
            finally
            {
                _gate.Release();
            }

            Interlocked.Increment(ref _written);
            Interlocked.Exchange(ref _lastWriteTicks, DateTime.UtcNow.Ticks);
            return true;
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
            return false;
        }
    }

    /// <summary>读取运行状态；不抛异常。</summary>
    /// <returns>状态快照。</returns>
    public RequestLogStatus GetStatus()
    {
        var ticks = Interlocked.Read(ref _lastWriteTicks);

        return new RequestLogStatus
        {
            Enabled = _options.Enabled,
            Directory = Directory,
            CurrentFile = _currentFile,
            Written = Interlocked.Read(ref _written),
            Failed = Interlocked.Read(ref _failed),
            LastError = _lastError,
            LastWriteAt = ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : default,
        };
    }

    /// <summary>写入一行并（可选）刷到磁盘。</summary>
    /// <param name="line">已序列化的 JSON 行。</param>
    private void WriteLine(String line)
    {
        EnsureWriter();

        _writer.Write(line);
        _writer.Write('\n');
        _writer.Flush();

        if (_options.FlushToDisk) _stream.Flush(true);

        _bytesInFile += Encoding.UTF8.GetByteCount(line) + 1;
    }

    /// <summary>按日期与大小切换文件；仅在写入锁内调用。</summary>
    private void EnsureWriter()
    {
        var today = DateTime.Now.Date;
        var rollByDate = _writer == null || today != _fileDate;
        var rollBySize = !rollByDate && _bytesInFile >= _maxBytes;

        if (!rollByDate && !rollBySize) return;

        if (rollByDate)
        {
            _fileDate = today;
            _sequence = 1;
        }
        else
        {
            _sequence++;
        }

        Open();
    }

    /// <summary>打开当前序号对应的文件。</summary>
    private void Open()
    {
        CloseWriter();

        System.IO.Directory.CreateDirectory(Directory);

        var name = _sequence <= 1
            ? $"{_options.FileNamePrefix}-{_fileDate:yyyyMMdd}.log"
            : $"{_options.FileNamePrefix}-{_fileDate:yyyyMMdd}-{_sequence:000}.log";
        var path = Path.Combine(Directory, name);

        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, FileOptions.None);
        _writer = new StreamWriter(_stream, Utf8NoBom) { AutoFlush = false };
        _bytesInFile = _stream.Length;
        _currentFile = path;

        CleanupExpired();
    }

    /// <summary>清理超过保留期的日志文件；只处理本目录、本前缀的 <c>.log</c>。</summary>
    private void CleanupExpired()
    {
        if (_options.RetentionDays <= 0) return;

        var deadline = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, $"{_options.FileNamePrefix}-*.log"))
        {
            if (String.Equals(file, _currentFile, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                if (File.GetLastWriteTimeUtc(file) < deadline) File.Delete(file);
            }
            catch
            {
                // 单个旧文件删不掉（被占用 / 权限）不影响写入，下次再试
            }
        }
    }

    /// <summary>记录一次写入失败，供健康检查与告警使用。</summary>
    /// <param name="ex">异常。</param>
    private void RecordFailure(Exception ex)
    {
        Interlocked.Increment(ref _failed);
        _lastError = $"{ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>关闭当前文件句柄。</summary>
    private void CloseWriter()
    {
        try { _writer?.Dispose(); } catch { /* 关闭失败不影响后续重建 */ }
        try { _stream?.Dispose(); } catch { /* 同上 */ }

        _writer = null;
        _stream = null;
    }

    /// <summary>释放文件句柄。</summary>
    public void Dispose()
    {
        _gate.Wait();
        try { CloseWriter(); }
        finally { _gate.Release(); }

        _gate.Dispose();
    }
}
