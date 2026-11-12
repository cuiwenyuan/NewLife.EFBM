namespace EFBM.Tests;

/// <summary>从测试输出目录向上定位仓库内文件，避免依赖当前工作目录。</summary>
internal static class RepoPaths
{
    /// <summary>查找仓库内指定相对路径的文件。</summary>
    /// <param name="relativePath">相对仓库根目录的路径，如 <c>src/EFBM.Data/Model.xml</c>。</param>
    /// <returns>文件绝对路径。</returns>
    /// <exception cref="FileNotFoundException">向上遍历到盘符仍未找到时抛出。</exception>
    public static String Find(String relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"未找到仓库文件：{relativePath}", relativePath);
    }
}
