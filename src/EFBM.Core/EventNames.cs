namespace EFBM.Core;

/// <summary>当前版本已知的 6 类上报事件。</summary>
/// <remarks>
/// 事件名来自设备侧报文，接收端不得改名。此表只用于注册 handler 与展示，<b>不是白名单</b>：
/// 未知事件必须原样入流并由兜底 handler 只存原始报文（需求说明 §3.5、FR-26）。
/// </remarks>
public static class EventNames
{
    /// <summary>工单开始生产。</summary>
    public const String OrderStarted = "ORDER_STARTED";

    /// <summary>产量里程碑，阈值由设备端控制。</summary>
    public const String OrderProgress = "ORDER_PROGRESS";

    /// <summary>逐件完成：每折好一个盒子上报一次，事件名沿用对方报文。</summary>
    public const String OrderCompleted = "ORDER_COMPLETED";

    /// <summary>设备状态切换。</summary>
    public const String DeviceStatusChanged = "DEVICE_STATUS_CHANGED";

    /// <summary>故障发生。</summary>
    public const String FaultOccurred = "FAULT_OCCURRED";

    /// <summary>故障清除，通常无 <c>data</c>。</summary>
    public const String FaultCleared = "FAULT_CLEARED";

    /// <summary>当前版本全部已知事件名。</summary>
    public static readonly String[] All = new[]
    {
        OrderStarted,
        OrderProgress,
        OrderCompleted,
        DeviceStatusChanged,
        FaultOccurred,
        FaultCleared,
    };

    /// <summary>判断事件名是否属于当前版本已知事件。</summary>
    /// <param name="eventName">报文中的 <c>event</c> 值。</param>
    /// <returns>已知返回 <c>true</c>；未知返回 <c>false</c>，但不得据此拒绝报文。</returns>
    public static Boolean IsKnown(String eventName) =>
        !String.IsNullOrEmpty(eventName) && Array.IndexOf(All, eventName) >= 0;
}
