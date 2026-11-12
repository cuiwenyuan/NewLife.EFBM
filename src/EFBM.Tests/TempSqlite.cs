namespace EFBM.Tests;

/// <summary>测试临时 SQLite 库的路径分配与清理登记。</summary>
/// <remarks>
/// 测试类的临时库是<b>类级静态字段</b>：xUnit 每个测试方法都会重建类实例，若在实例
/// <see cref="IDisposable.Dispose"/> 里删文件，会把同类的后续用例一起打断（XCode 还有进程级
/// 建表缓存，删掉后新文件里没有表，表现为 "no such table"）。因此清理只能放在<b>进程退出</b>，
/// 由本类统一登记，一次运行结束后把本次产生的所有临时库删掉，避免每跑一次测试就在临时目录
/// 里多留几个几十 KB 到几 MB 的 .db 文件。
/// </remarks>
internal static class TempSqlite
{
    private static readonly HashSet<String> _registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Object _lock = new();

    /// <summary>分配一个临时库路径，并登记为进程退出时删除。</summary>
    /// <param name="prefix">文件名前缀，便于按测试类区分。</param>
    /// <returns>临时库完整路径。</returns>
    public static String CreatePath(String prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
        Register(path);
        return path;
    }

    private static void Register(String path)
    {
        lock (_lock)
        {
            if (!_registered.Add(path)) return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(path);
    }

    /// <summary>分配一个临时目录（已创建），并登记为进程退出时递归删除。</summary>
    /// <param name="prefix">目录名前缀。</param>
    /// <returns>临时目录完整路径。</returns>
    public static String CreateDirectory(String prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteDirectory(dir);

        return dir;
    }

    /// <summary>尽力删除临时目录。</summary>
    /// <param name="dir">目录路径。</param>
    public static void TryDeleteDirectory(String dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 文件仍被占用时保留，下次运行不影响
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>尽力删除临时库：连接池未及时释放时保留文件，不影响测试结论。</summary>
    /// <param name="path">临时库路径。</param>
    public static void TryDelete(String path)
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 连接池未及时释放时保留临时文件，不影响测试结论
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }
}
