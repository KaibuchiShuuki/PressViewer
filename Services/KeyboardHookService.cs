using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class KeyboardHookService : IKeyboardHookService, IDisposable
{
    public event Action<string>? KeyPressed;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly ILogger<KeyboardHookService> _logger;
    private LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // 记录当前正处于按下状态的键（名称）
    private readonly HashSet<string> _pressedKeys = new(StringComparer.Ordinal);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);

    public KeyboardHookService(ILogger<KeyboardHookService> logger)
    {
        _logger = logger;
        _proc = HookCallback;
    }

    public void Start()
    {
        // 每调一次 SetWindowsHookEx 就多一个钩子，旧句柄还会丢
        if (_hookId != IntPtr.Zero)
            return;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _proc,
                GetModuleHandle(curModule.ModuleName),
                0
            );

            if (_hookId != IntPtr.Zero)
            {
                _logger.LogInformation(
                    "键盘采集已启动（钩子句柄 0x{HookId:X}）",
                    _hookId.ToInt64()
                );
                return;
            }

            _logger.LogWarning(
                "键盘钩子安装失败（第 {Attempt}/2 次），Win32 错误码 {Error}",
                attempt,
                Marshal.GetLastWin32Error()
            );
            if (attempt < 2)
                Thread.Sleep(200);
        }

        _logger.LogError("键盘采集未能启动：SetWindowsHookEx 连续两次失败");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                string keyName = GetKeyNameFromScanCode(kb.vkCode, kb.scanCode, kb.flags);

                if (isKeyDown)
                {
                    // 如果该键已经处于按下状态，则为长按重复，直接忽略
                    if (_pressedKeys.Contains(keyName))
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);

                    _pressedKeys.Add(keyName);
                    KeyPressed?.Invoke(keyName);
                }
                else // isKeyUp
                {
                    _pressedKeys.Remove(keyName);
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // 左右修饰键固定映射：GetKeyNameText 对左/右 Shift、Ctrl、Alt、Win 的命名依赖键盘布局
    // （部分布局自带 “Left/Right” 方位词，多数不区分），统一走这里保证左右稳定可区分。
    // Win 用 “Left Windows/Right Windows” 与正式版历史数据保持一致
    private static readonly Dictionary<int, string> ModifierKeyNames = new()
    {
        [0x5B] = "Left Windows", // VK_LWIN
        [0x5C] = "Right Windows", // VK_RWIN
        [0xA0] = "Shift", // VK_LSHIFT
        [0xA1] = "Right Shift", // VK_RSHIFT
        [0xA2] = "Ctrl", // VK_LCONTROL
        [0xA3] = "Right Ctrl", // VK_RCONTROL
        [0xA4] = "Alt", // VK_LMENU
        [0xA5] = "Right Alt", // VK_RMENU
    };

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // 扫描码缺失时（注入键、虚拟键、部分固件）用虚拟键码兜底的功能键名
    private static readonly Dictionary<int, string> VkNames = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0x6A] = "Multiply",
        [0x6B] = "Add",
        [0x6D] = "Subtract",
        [0x6E] = "Decimal",
        [0x6F] = "Divide",
        [0xAD] = "Volume Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Next Track",
        [0xB1] = "Previous Track",
        [0xB2] = "Stop Media",
        [0xB3] = "Play/Pause",
    };

    // 把扫描码 + 扩展标志合成 GetKeyNameText 所需的参数
    private string GetKeyNameFromScanCode(int vkCode, int scanCode, int flags)
    {
        // 左右修饰键优先走固定映射，避免布局差异
        if (ModifierKeyNames.TryGetValue(vkCode, out var modifierName))
            return modifierName;

        bool isExtended = (flags & 1) != 0;

        // 1. 正常路径：有扫描码时交给系统取名
        if (scanCode > 0)
        {
            int lParamValue = (scanCode << 16) | (isExtended ? 0x1000000 : 0);
            var sb = new StringBuilder(256);
            if (GetKeyNameText(lParamValue, sb, sb.Capacity) > 0)
                return sb.ToString();
        }

        // 笔记本 Fn 的常见扫描码
        if (scanCode == 0x63)
            return "Fn";

        // 2. 扫描码缺失：用虚拟键码反查扫描码，再请系统取名
        uint virtualScan = MapVirtualKey((uint)vkCode, 0);
        if (virtualScan > 0)
        {
            int lParamValue = ((int)virtualScan << 16) | (isExtended ? 0x1000000 : 0);
            var sb = new StringBuilder(256);
            if (GetKeyNameText(lParamValue, sb, sb.Capacity) > 0)
                return sb.ToString();
        }

        // 3. 可打印字符（字母、数字、标点）
        uint ch = MapVirtualKey((uint)vkCode, 2);
        if (ch > 0 && ch < 0x10000)
            return ((char)ch).ToString();

        // 4. 功能键名表
        if (VkNames.TryGetValue(vkCode, out var vkName))
            return vkName;

        // 5. F1–F24
        if (vkCode >= 0x70 && vkCode <= 0x87)
            return $"F{vkCode - 0x70 + 1}";

        _logger.LogWarning(
            "按键无法识别，扫描码 0x{Scan:X2}，虚拟键码 0x{Vk:X2}，标志 0x{Flags:X2}",
            scanCode,
            vkCode,
            flags
        );
        return $"VK 0x{vkCode:X2}";
    }

    // 结构体定义
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    // P/Invoke
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public void Dispose() => Stop();
}
