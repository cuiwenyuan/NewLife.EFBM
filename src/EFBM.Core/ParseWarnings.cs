namespace EFBM.Core;

/// <summary>解析告警编码，见《Doc/上报字段字典.md》§5。</summary>
/// <remarks>
/// 告警只写入 <c>ParseWarning</c> 字段供后台检索，<b>绝不改变 HTTP 响应码、不阻断入流、不进死信</b>。
/// 设备失败不重发，多拒绝一条即永久少一条数据。
/// </remarks>
public static class ParseWarnings
{
    /// <summary>某事件业务上应有的已识别字段缺失。</summary>
    public const String MissingField = "W100";

    /// <summary>已识别字段类型不符。</summary>
    public const String TypeMismatch = "W101";

    /// <summary>出现未登记字段（含顶层字段、嵌套对象、数组）。</summary>
    public const String UnknownField = "W102";

    /// <summary>未知事件类型。</summary>
    public const String UnknownEvent = "W103";

    /// <summary>未知或空的状态值。</summary>
    public const String UnknownStatus = "W104";

    /// <summary>时间格式异常或无法转换为 UTC。</summary>
    public const String BadTimestamp = "W105";

    /// <summary><c>data</c> 不是 JSON 对象。</summary>
    public const String DataNotObject = "W106";

    /// <summary>数量为负、超出 Int32 等数值异常。</summary>
    public const String BadNumber = "W107";

    /// <summary>报文含重复 JSON 键。</summary>
    public const String DuplicateKey = "W108";

    /// <summary>把多个告警编码合成落库用的逗号分隔字符串。</summary>
    /// <param name="codes">告警编码集合。</param>
    /// <returns>逗号分隔的编码串；无告警返回空字符串。</returns>
    public static String Join(params String[] codes) =>
        codes == null ? String.Empty : String.Join(",", codes.Where(e => !String.IsNullOrEmpty(e)).Distinct());
}
