namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>外部消费组的命名与登记规则（STR-2a、FR-21）。</summary>
/// <remarks>
/// 组名是<b>跨系统的长期标识</b>：一旦外部系统的组名变了，Redis 会把它当成全新消费组，
/// <c>FromLastOffset=false</c> 会让它从头重放全量历史；反过来，两个系统共用一个组名则会互相抢消息。
/// 因此组名必须满足固定格式、全局唯一、且<b>不允许删除后复用</b>（停用可以，改名视同新建）。
///
/// 规则刻意只允许小写：Redis 的组名大小写敏感，<c>efbm-MES</c> 与 <c>efbm-mes</c> 是两个组，
/// 现场只要有人手抖写错大小写就会得到「消息永远收不到」这种最难查的故障。
/// </remarks>
public static class ConsumerGroupRules
{
    /// <summary>组名最大长度；Redis 本身不限制，但太长的名字在日志与页面里都没法看。</summary>
    public const Int32 MaxNameLength = 64;

    /// <summary>本系统落库组名，属于内部组，不需要登记为外部系统。</summary>
    /// <param name="name">组名。</param>
    /// <returns>内部组返回 <c>true</c>。</returns>
    public static Boolean IsInternal(String name) =>
        String.Equals(name, EfbmDefaults.PersistGroup, StringComparison.Ordinal);

    /// <summary>从组名解析归属系统名。</summary>
    /// <param name="name">组名。</param>
    /// <returns>系统名；格式不合法时返回空串。</returns>
    public static String GetSystemName(String name)
    {
        if (!IsValidName(name)) return String.Empty;
        return name[EfbmDefaults.ExternalGroupPrefix.Length..];
    }

    /// <summary>校验组名格式。</summary>
    /// <param name="name">组名。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    public static Boolean IsValidName(String name)
    {
        if (String.IsNullOrWhiteSpace(name)) return false;

        var value = name.Trim();
        if (value.Length > MaxNameLength) return false;
        if (!value.StartsWith(EfbmDefaults.ExternalGroupPrefix, StringComparison.Ordinal)) return false;

        var system = value[EfbmDefaults.ExternalGroupPrefix.Length..];
        if (system.Length == 0) return false;

        // 允许小写字母、数字与 . _ -，但首字符必须是字母或数字
        if (!Char.IsAsciiLetterLower(system[0]) && !Char.IsAsciiDigit(system[0])) return false;

        foreach (var c in system)
        {
            var ok = Char.IsAsciiLetterLower(c) || Char.IsAsciiDigit(c) || c is '.' or '_' or '-';
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>校验组名并返回面向运维的错误说明。</summary>
    /// <param name="name">组名。</param>
    /// <returns>合法返回 <c>null</c>，否则返回原因。</returns>
    public static String Validate(String name)
    {
        if (String.IsNullOrWhiteSpace(name)) return "消费组名不能为空";
        if (name.Trim().Length > MaxNameLength) return $"消费组名不能超过 {MaxNameLength} 个字符";

        if (!name.Trim().StartsWith(EfbmDefaults.ExternalGroupPrefix, StringComparison.Ordinal))
            return $"外部消费组必须以 {EfbmDefaults.ExternalGroupPrefix} 开头，例如 efbm-mes";

        var system = name.Trim()[EfbmDefaults.ExternalGroupPrefix.Length..];
        if (system.Length == 0) return "组名缺少归属系统名，例如 efbm-mes";

        return IsValidName(name) ? null : "系统名只能用小写字母、数字、点、下划线、短横线，且首字符必须是字母或数字";
    }
}
