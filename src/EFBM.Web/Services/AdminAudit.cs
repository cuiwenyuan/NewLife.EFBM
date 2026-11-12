using NewLife.Cube;
using NewLife.Log;
using XCode.Membership;

namespace EFBM.Web.Services;

/// <summary>后台操作审计：写进魔方的 <c>Log</c> 表，后台「日志」页可直接查看。</summary>
/// <remarks>
/// 不另建表：<c>Log</c> 已经是「操作留痕」的既成标准，<c>Category</c> 区分来源、<c>Action</c> 存动作、
/// <c>LinkID</c> 存目标主键、<c>Success</c> 存结果、<c>Ex4</c> 存结果摘要、<c>Remark</c> 存参数摘要。
/// 写日志失败只落应用日志，绝不让已经执行完的清理 / 重放回滚成「不知道做没做」。
/// </remarks>
public static class AdminAudit
{
    /// <summary>审计分类，固定写进 <c>Category</c>，便于与系统其他日志区分。</summary>
    public const String Category = "EFBM后台";

    /// <summary>Ex4 字段的最大长度（取自实体元数据，取不到时按 200 保守处理）。</summary>
    private static readonly Int32 Ex4Length = FieldLength("Ex4", 200);

    /// <summary>Remark 字段的最大长度。</summary>
    private static readonly Int32 RemarkLength = FieldLength("Remark", 500);

    /// <summary>写一条审计。</summary>
    /// <param name="controller">当前控制器（取来源 IP 与追踪 ID）。</param>
    /// <param name="action">动作中文名。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="summary">结果摘要，例如「删除 1234 条」「新消息 xxx」。</param>
    /// <param name="targetId">目标主键（报文 / 死信 ID），无则省略。</param>
    public static void Write(ControllerBaseX controller, String action, Boolean success, String summary, Int64? targetId = null)
        => Write(action, success, summary, ManageProvider.User?.Name, targetId, controller?.HttpContext?.TraceIdentifier, controller?.UserHost);

    /// <summary>写一条审计（服务层 / 命令行调用，没有控制器上下文）。</summary>
    /// <param name="action">动作中文名。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="summary">结果摘要。</param>
    /// <param name="operatorName">操作人；为空时记 <c>system</c>。</param>
    /// <param name="targetId">目标主键，无则省略。</param>
    public static void Write(String action, Boolean success, String summary, String operatorName, Int64? targetId = null)
        => Write(action, success, summary, operatorName, targetId, null, null);

    /// <summary>实际写入，集中处理长度截断与异常吞掉。</summary>
    private static void Write(String action, Boolean success, String summary, String operatorName, Int64? targetId, String traceId, String userHost)
    {
        try
        {
            var user = String.IsNullOrWhiteSpace(operatorName) ? "system" : operatorName;

            new Log
            {
                Category = Category,
                Action = action,
                LinkID = targetId ?? 0,
                Success = success,
                UserName = user,
                Ex4 = Truncate(summary, Ex4Length),
                TraceId = traceId,
                CreateUser = user,
                CreateIP = userHost,
                CreateTime = DateTime.Now,
            }.Insert();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>从实体元数据取字段长度；框架表结构变化时退回保守值。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="fallback">取不到时的默认长度。</param>
    /// <returns>字段长度。</returns>
    private static Int32 FieldLength(String name, Int32 fallback)
    {
        try
        {
            var field = Log.Meta.Fields.FirstOrDefault(f => String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            return field is { Length: > 0 } ? field.Length : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>按字段长度截断，超长会被 XCode 判为写入失败。</summary>
    /// <param name="value">原文。</param>
    /// <param name="length">最大长度。</param>
    /// <returns>截断后的文本。</returns>
    private static String Truncate(String value, Int32 length)
        => String.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];
}
