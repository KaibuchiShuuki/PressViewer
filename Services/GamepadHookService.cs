using System;
using System.Runtime.InteropServices;
using System.Threading;
using Timer = System.Threading.Timer;
using Microsoft.Extensions.Logging;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

/// <summary>
/// 手柄监听：用 XInput 轮询已连接的手柄，按下沿（从松开到按下）记一次。
/// XInput 不提供 Share 键与 Guide 键，这两类键值固定为 0。
/// </summary>
public sealed class GamepadHookService : IGamepadHookService
{
    // XInput 按键位
    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort BtnStart = 0x0010;
    private const ushort BtnBack = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort BtnA = 0x1000;
    private const ushort BtnB = 0x2000;
    private const ushort BtnX = 0x4000;
    private const ushort BtnY = 0x8000;

    private const byte TriggerThreshold = 30;
    private const int Success = 0;

    private Timer? _timer;
    private ushort _lastButtons;
    private bool _lastLeftTrigger;
    private bool _lastRightTrigger;
    private int _activeIndex = -1;
    private int _polling; // 防重入
    private readonly ILogger<GamepadHookService> _logger;

    public GamepadHookService(ILogger<GamepadHookService> logger) => _logger = logger;

    public bool IsRunning => _timer != null;

    public event Action<string>? GamepadPressed;

    public void Start()
    {
        if (_timer != null)
            return;

        _lastButtons = 0;
        _lastLeftTrigger = false;
        _lastRightTrigger = false;
        _activeIndex = -1;
        _timer = new Timer(_ => Poll(), null, 0, 16);
        _logger.LogInformation("GamepadHookService: 手柄采集已启动");
    }

    public void Stop()
    {
        if (_timer == null)
            return;

        _timer.Dispose();
        _timer = null;
        _logger.LogInformation("GamepadHookService: 手柄采集已停止");
    }

    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return;

        try
        {
            // 只跟踪第一个已连接的手柄；断开后允许重新扫描
            for (int i = 0; i < 4; i++)
            {
                if (TryGetState(i, out var state))
                {
                    if (_activeIndex != i)
                    {
                        _activeIndex = i;
                        _lastButtons = 0;
                        _lastLeftTrigger = false;
                        _lastRightTrigger = false;
                        _logger.LogInformation("GamepadHookService: 已连接手柄 #{Index}", i);
                    }

                    Process(state.Gamepad);
                    return;
                }
            }

            if (_activeIndex >= 0)
            {
                _logger.LogInformation("GamepadHookService: 手柄已断开");
                _activeIndex = -1;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Process(XInputGamepad pad)
    {
        ushort buttons = pad.wButtons;

        RaiseIfPressed(buttons, DPadUp, GamepadButtonIds.DPadUp);
        RaiseIfPressed(buttons, DPadDown, GamepadButtonIds.DPadDown);
        RaiseIfPressed(buttons, DPadLeft, GamepadButtonIds.DPadLeft);
        RaiseIfPressed(buttons, DPadRight, GamepadButtonIds.DPadRight);
        RaiseIfPressed(buttons, BtnA, GamepadButtonIds.A);
        RaiseIfPressed(buttons, BtnB, GamepadButtonIds.B);
        RaiseIfPressed(buttons, BtnX, GamepadButtonIds.X);
        RaiseIfPressed(buttons, BtnY, GamepadButtonIds.Y);
        RaiseIfPressed(buttons, LeftShoulder, GamepadButtonIds.LB);
        RaiseIfPressed(buttons, RightShoulder, GamepadButtonIds.RB);
        RaiseIfPressed(buttons, BtnStart, GamepadButtonIds.Start);
        RaiseIfPressed(buttons, BtnBack, GamepadButtonIds.Back);
        RaiseIfPressed(buttons, LeftThumb, GamepadButtonIds.LStick);
        RaiseIfPressed(buttons, RightThumb, GamepadButtonIds.RStick);

        bool leftTrigger = pad.bLeftTrigger > TriggerThreshold;
        bool rightTrigger = pad.bRightTrigger > TriggerThreshold;

        if (leftTrigger && !_lastLeftTrigger)
            Raise(GamepadButtonIds.LT);
        if (rightTrigger && !_lastRightTrigger)
            Raise(GamepadButtonIds.RT);

        _lastButtons = buttons;
        _lastLeftTrigger = leftTrigger;
        _lastRightTrigger = rightTrigger;
    }

    private void RaiseIfPressed(ushort current, ushort mask, string id)
    {
        if ((current & mask) != 0 && (_lastButtons & mask) == 0)
            Raise(id);
    }

    private void Raise(string id)
    {
        try
        {
            GamepadPressed?.Invoke(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GamepadHookService: 手柄按键回调异常");
        }
    }

    private static bool TryGetState(int index, out XInputState state)
    {
        try
        {
            return XInputGetState(index, out state) == Success;
        }
        catch
        {
            state = default;
            return false;
        }
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);
}
