using System.Runtime.InteropServices;

namespace PressViewer.Service;

/// <summary>
/// 读取当前控制台会话的「最后一次输入」时间，用来判断用户是否真的在电脑前。
/// 服务跑在 Session 0，GetLastInputInfo 只能拿到服务自己的输入状态（恒为 0），
/// 所以必须走 WTS 查询活动会话的 WTSINFOW.LastInputTime。
/// </summary>
internal static class UserIdleDetector
{
    private const int WTSSessionInfo = 24;

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        int infoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned
    );

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WTSINFOW
    {
        public int State;
        public int SessionId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string WinStationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
        public string UserName;

        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long LogonTime;
        public long CurrentTime;
    }

    /// <summary>距离最后一次键盘/鼠标输入的秒数；拿不到时返回 -1（调用方应视为「未知」）。</summary>
    public static long IdleSeconds()
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId < 0)
                return -1;

            if (
                !WTSQuerySessionInformation(
                    IntPtr.Zero,
                    sessionId,
                    WTSSessionInfo,
                    out var buffer,
                    out _
                )
            )
                return -1;

            try
            {
                var info = Marshal.PtrToStructure<WTSINFOW>(buffer);
                if (info.LastInputTime <= 0)
                    return -1;

                var lastInput = DateTime.FromFileTimeUtc(info.LastInputTime);
                var idle = (DateTime.UtcNow - lastInput).TotalSeconds;
                return idle < 0 ? 0 : (long)idle;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        catch
        {
            return -1;
        }
    }
}
