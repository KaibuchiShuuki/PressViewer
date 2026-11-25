using System;

namespace PressViewer.Services;

/// <summary>
/// 手柄热力图的两种画法：
/// <list type="bullet">
/// <item>Form —— 手柄形：保留手柄轮廓做底，按键按真实手柄的相对位置摆，写实、有质感。</item>
/// <item>Tile —— 键帽式：放弃轮廓，把按键排成大键帽分区，每个键都有足够面积写完整数字。</item>
/// </list>
/// 当前固定用 Tile（键帽式）：按键更大、长数字放得下，1.1.1 起不再提供切换入口。
/// Form 的分支代码完整保留着，需要时把 <see cref="Current"/> 改成 Form 即可恢复，见 AGENT.md「手柄热力图样式」。
/// </summary>
public static class GamepadLayoutService
{
    public const string Form = "Form";
    public const string Tile = "Tile";

    /// <summary>当前生效的画法。默认键帽式；改成 Form 并触发 <see cref="Changed"/> 可切回手柄形。</summary>
    public static string Current { get; private set; } = Tile;

    public static event Action? Changed;

    public static void Set(string? layout)
    {
        string next = string.Equals(layout, Form, StringComparison.OrdinalIgnoreCase) ? Form : Tile;
        if (Current == next)
            return;

        Current = next;
        Changed?.Invoke();
    }
}
