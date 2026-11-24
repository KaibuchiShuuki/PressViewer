using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace PressViewer.Services
{
    public static class AppDataPathHelper
    {
        /// <summary>
        /// 数据目录固定在程序目录下的 data（开发版 data-dev），
        /// 不再写用户 Roaming 目录；首次启动会把旧位置（%APPDATA%）的数据搬过来。
        /// </summary>
        public static string GetAppDataFolder()
        {
#if DEBUG
            string folderName = "data-dev";
#else
            string folderName = "data";
#endif
            string path = Path.Combine(AppContext.BaseDirectory, folderName);
            Directory.CreateDirectory(path);

            // 先搬改名后的 PressViewer，再搬改名前的 XAssistant；只复制、不删除。
            MigrateFromRoaming(path, "PressViewer");
            MigrateFromRoaming(path, "PressViewer_Dev");
            MigrateFromRoaming(path, "XAssistant");
            MigrateFromRoaming(path, "XAssistant_Dev");
            return path;
        }

        /// <summary>
        /// 从 %APPDATA%\&lt;folder&gt; 复制历史数据到新目录。
        /// 目标已存在则跳过；失败只是新建空库，不会动旧目录。
        /// </summary>
        private static void MigrateFromRoaming(string targetPath, string folderName)
        {
            try
            {
                string sourceDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    folderName
                );
                if (
                    !Directory.Exists(sourceDir)
                    || string.Equals(
                        Path.GetFullPath(sourceDir),
                        Path.GetFullPath(targetPath),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return;

                foreach (string source in Directory.EnumerateFiles(sourceDir))
                {
                    string dest = Path.Combine(targetPath, Path.GetFileName(source));
                    if (!File.Exists(dest))
                        File.Copy(source, dest, overwrite: false);
                }
            }
            catch
            {
                // 迁移失败不影响启动：旧数据仍在原目录，程序会新建空库
            }
        }

        /// <summary>
        /// 登录后计划任务会立即拉起进程；此时用户配置单元可能还没挂载完，
        /// <c>%APPDATA%</c> 可能指向临时目录。等到真实的 ApplicationData 路径出现再开始写日志，
        /// 替代原来在计划任务上写死的 8 秒延时。
        /// </summary>
        public static bool WaitForApplicationDataReady(
            TimeSpan timeout,
            out long waitedMilliseconds
        )
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (IsApplicationDataReady())
                {
                    waitedMilliseconds = stopwatch.ElapsedMilliseconds;
                    return true;
                }
                Thread.Sleep(100);
            }

            waitedMilliseconds = stopwatch.ElapsedMilliseconds;
            return false;
        }

        /// <summary>当前进程看到的 ApplicationData 是否已经是真实用户目录。</summary>
        public static bool IsApplicationDataReady()
        {
            try
            {
                string current = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData
                );
                if (string.IsNullOrWhiteSpace(current) || !Directory.Exists(current))
                    return false;

                string normalized = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
                if (IsUnderTemp(normalized))
                    return false;

                // HKCU 的 User Shell Folders 是配置单元已经挂载的直接证据，
                // 也能正确处理被重定向到其它磁盘的 AppData。
                string? registered = GetRegisteredApplicationData();
                if (registered is not null)
                {
                    return string.Equals(
                        normalized,
                        registered.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                // 读不到 User Shell Folders 时退回 ProfileList（本地用户）
                string? profile = GetProfileApplicationData();
                if (profile is not null)
                {
                    return string.Equals(
                        normalized,
                        profile.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUnderTemp(string normalizedPath)
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(normalizedPath, temp, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(
                    temp + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static string? GetRegisteredApplicationData()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
                );
                if (
                    key?.GetValue("AppData") is not string value
                    || string.IsNullOrWhiteSpace(value)
                )
                    return null;

                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
            }
            catch
            {
                return null;
            }
        }

        private static string? GetProfileApplicationData()
        {
            try
            {
                string? sid = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(sid))
                    return null;

                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}"
                );
                if (key?.GetValue("ProfileImagePath") is not string profilePath)
                    return null;

                return Path.GetFullPath(
                    Path.Combine(
                        Environment.ExpandEnvironmentVariables(profilePath),
                        "AppData",
                        "Roaming"
                    )
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
