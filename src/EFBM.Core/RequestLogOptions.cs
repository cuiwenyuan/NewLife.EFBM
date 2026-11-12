namespace EFBM.Core;

/// <summary>请求日志配置（<c>EFBM:RequestLog</c>）：Beta 阶段以文本日志记录每一条上报。</summary>
/// <remarks>
/// 每行一条 JSON（JSONL），字段固定、可直接 <c>grep</c>；按天滚动，超过 <see cref="MaxFileMB"/> 再按序号切分。
/// 目录使用相对路径时以<b>宿主内容根目录</b>为基准，不随进程工作目录漂移（SQLite 曾因此踩坑）。
/// </remarks>
public class RequestLogOptions
{
    /// <summary>是否启用请求日志。关闭后 Redis 与本地兜底同时失效将直接返回 503。</summary>
    public Boolean Enabled { get; set; } = true;

    /// <summary>日志目录；相对路径以内容根目录为基准。</summary>
    public String Directory { get; set; } = EfbmDefaults.RequestLogDirectory;

    /// <summary>文件名前缀，最终形如 <c>api-20261112.log</c> / <c>api-20261112-002.log</c>。</summary>
    public String FileNamePrefix { get; set; } = EfbmDefaults.RequestLogPrefix;

    /// <summary>单文件大小上限（MB），超过后同日切分新序号文件。</summary>
    public Int32 MaxFileMB { get; set; } = EfbmDefaults.RequestLogMaxFileMB;

    /// <summary>日志保留天数；<c>0</c> 表示永不清理。只清理本前缀、本目录下的 <c>*.log</c>。</summary>
    public Int32 RetentionDays { get; set; } = EfbmDefaults.RequestLogRetentionDays;

    /// <summary>是否记录请求体原文。关闭后只留元数据，丢失后无法补录，仅在报文含敏感数据时关闭。</summary>
    public Boolean IncludeRaw { get; set; } = true;

    /// <summary>是否每次写入都 <c>fsync</c> 到磁盘。开启可抵御断电，代价约 1ms/条；高并发可关闭。</summary>
    public Boolean FlushToDisk { get; set; } = true;

    /// <summary>启动自检：目录名与前缀不能为空，数值必须为正。</summary>
    /// <exception cref="InvalidOperationException">配置非法时抛出，避免启动后才发现日志写不进去。</exception>
    public void ValidateOrThrow()
    {
        if (!Enabled) return;

        if (String.IsNullOrWhiteSpace(Directory))
            throw new InvalidOperationException("请求日志目录不能为空（EFBM:RequestLog:Directory）");
        if (String.IsNullOrWhiteSpace(FileNamePrefix))
            throw new InvalidOperationException("请求日志文件名前缀不能为空（EFBM:RequestLog:FileNamePrefix）");
        if (MaxFileMB <= 0)
            throw new InvalidOperationException("请求日志单文件上限必须为正数（EFBM:RequestLog:MaxFileMB>0）");
        if (RetentionDays < 0)
            throw new InvalidOperationException("请求日志保留天数不得为负数（EFBM:RequestLog:RetentionDays>=0）");
    }
}
