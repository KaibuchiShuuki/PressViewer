namespace PressViewer.Models;

/// <summary>手柄按键 id，数据库与图形控件共用这一套名字。</summary>
public static class GamepadButtonIds
{
    public const string DPadUp = "DPadUp";
    public const string DPadDown = "DPadDown";
    public const string DPadLeft = "DPadLeft";
    public const string DPadRight = "DPadRight";
    public const string A = "A";
    public const string B = "B";
    public const string X = "X";
    public const string Y = "Y";
    public const string LB = "LB";
    public const string RB = "RB";
    public const string LT = "LT";
    public const string RT = "RT";
    public const string Start = "Start";
    public const string Back = "Back";
    public const string LStick = "LStick";
    public const string RStick = "RStick";

    /// <summary>Share 键（Xbox Series 手柄）XInput 不暴露，固定 0 次。</summary>
    public const string Share = "Share";

    public static readonly string[] All =
    [
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight,
        A,
        B,
        X,
        Y,
        LB,
        RB,
        LT,
        RT,
        Start,
        Back,
        LStick,
        RStick,
        Share,
    ];

    /// <summary>数据库/图形里的英文 id 到界面上给人看的中文名。</summary>
    public static string Display(string id) =>
        id switch
        {
            DPadUp => "十字键 上",
            DPadDown => "十字键 下",
            DPadLeft => "十字键 左",
            DPadRight => "十字键 右",
            LStick => "左摇杆按下",
            RStick => "右摇杆按下",
            Start => "Start / Menu",
            Back => "Back / View",
            Share => "Share",
            _ => id,
        };
}

public class GamepadPressRecord
{
    public int Id { get; set; }
    public string Button { get; set; } = string.Empty;
    public DateTime PressTime { get; set; }
}
