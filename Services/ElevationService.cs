using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PressViewer.Services;

/// <summary>
/// 以管理员身份重新拉起自己。
///
/// 1.1.2 起程序本体以普通权限运行（manifest 写 asInvoker），这样桌面快捷方式不会带上
/// Windows 自动加的 UAC 盾徽；启动后第一件事检查权限，不够就通过 runas 重启自己。
/// 开机自启走的是「最高权限」计划任务，进程起来时已经是管理员，这里会直接跳过，不弹提示。
/// </summary>
public static class ElevationService
{
    private const uint SeeMaskNoCloseProcess = 0x00000040;
    private const int SwShownormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>当前进程是否以管理员身份运行。</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// 用 runas 重新启动当前程序。用户在 UAC 里点「否」时返回 false。
    /// </summary>
    public static bool RelaunchElevated(IEnumerable<string> args)
    {
        string exe =
            Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? string.Empty;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskNoCloseProcess,
            lpVerb = "runas",
            lpFile = exe,
            lpParameters = string.Join(" ", args.Select(Quote)),
            lpDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            nShow = SwShownormal,
        };

        return ShellExecuteEx(ref info);
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
