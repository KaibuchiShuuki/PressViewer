using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PressViewer.Services;

/// <summary>历史数据压缩结果。</summary>
public sealed record CompactionResult(
    int DaysCompacted,
    long RecordsRemoved,
    long BytesBefore,
    long BytesAfter,
    string Message
)
{
    public long BytesSaved => Math.Max(0, BytesBefore - BytesAfter);
    public bool HasChanges => DaysCompacted > 0;
}

/// <summary>
/// 把「今天之前」的按键明细（每次按键一行）压缩成「某天某键按了几次」。
/// 三个输入设备各有一个库，表结构一致，只是列名不同。
/// 压缩过的日期记进 CompactedDays，下次不再扫描。
/// 压缩后统计查询会同时读明细表和按天汇总表，界面看到的数字不变。
/// </summary>
public static class DataCompactionService
{
    private sealed record Store(
        string FileName,
        string Table,
        string KeyColumn,
        string TimeColumn,
        string DailyTable,
        string DisplayName
    );

    private static readonly Store[] Stores =
    {
        new("key_data.db", "KeyPressRecords", "Key", "PressTime", "KeyPressDaily", "键盘"),
        new(
            "click_data.db",
            "ClickRecords",
            "Button",
            "ClickTime",
            "ClickDaily",
            "鼠标"
        ),
        new(
            "gamepad_data.db",
            "GamepadPressRecords",
            "Button",
            "PressTime",
            "GamepadPressDaily",
            "手柄"
        ),
    };

    /// <summary>压缩今天之前的所有明细记录。report 用来回报进度。</summary>
    public static CompactionResult Compact(Action<string>? report = null)
    {
        string folder = AppDataPathHelper.GetAppDataFolder();
        Directory.CreateDirectory(folder);

        string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        long totalRemoved = 0;
        var allDays = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();
        long bytesBefore = 0;
        long bytesAfter = 0;

        foreach (var store in Stores)
        {
            string path = Path.Combine(folder, store.FileName);
            if (!File.Exists(path))
                continue;

            bytesBefore += new FileInfo(path).Length;
            report?.Invoke($"正在压缩{store.DisplayName}数据…");

            var (days, removed) = CompactStore(path, store, today, report);
            foreach (string day in days)
                allDays.Add(day);
            totalRemoved += removed;
            if (days.Count > 0)
                parts.Add($"{store.DisplayName} {days.Count} 天");

            // 压缩会让文件出现空洞，VACUUM 把空间真正还给磁盘。
            if (days.Count > 0)
                TryVacuum(path);

            bytesAfter += new FileInfo(path).Length;
        }

        int dayCount = allDays.Count;
        string message =
            dayCount == 0
                ? "没有需要压缩的历史数据。"
                : $"已压缩 {dayCount} 天（{string.Join("、", parts)}），共合并 {totalRemoved:N0} 条明细。";

        return new CompactionResult(dayCount, totalRemoved, bytesBefore, bytesAfter, message);
    }

    private static (List<string> Days, long Removed) CompactStore(
        string path,
        Store store,
        string today,
        Action<string>? report
    )
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        var days = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                $@"
                SELECT DISTINCT substr({store.TimeColumn}, 1, 10) AS Day
                FROM {store.Table}
                WHERE substr({store.TimeColumn}, 1, 10) < @today
                  AND substr({store.TimeColumn}, 1, 10) NOT IN (SELECT Day FROM CompactedDays)
                ORDER BY Day";
            cmd.Parameters.AddWithValue("@today", today);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string? day = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(day))
                    days.Add(day);
            }
        }

        if (days.Count == 0)
            return (days, 0);

        long removed = 0;
        string now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);

        using var transaction = connection.BeginTransaction();
        foreach (string day in days)
        {
            report?.Invoke($"正在合并 {day} 的{store.DisplayName}记录…");

            using (var sum = connection.CreateCommand())
            {
                sum.Transaction = transaction;
                sum.CommandText =
                    $@"
                    INSERT INTO {store.DailyTable} ({store.KeyColumn}, Day, Count)
                    SELECT {store.KeyColumn}, @day, COUNT(*)
                    FROM {store.Table}
                    WHERE substr({store.TimeColumn}, 1, 10) = @day
                    GROUP BY {store.KeyColumn}
                    ON CONFLICT({store.KeyColumn}, Day) DO UPDATE SET Count = Count + excluded.Count";
                sum.Parameters.AddWithValue("@day", day);
                sum.ExecuteNonQuery();
            }

            using (var del = connection.CreateCommand())
            {
                del.Transaction = transaction;
                del.CommandText =
                    $@"DELETE FROM {store.Table} WHERE substr({store.TimeColumn}, 1, 10) = @day";
                del.Parameters.AddWithValue("@day", day);
                removed += del.ExecuteNonQuery();
            }

            using (var mark = connection.CreateCommand())
            {
                mark.Transaction = transaction;
                mark.CommandText =
                    "INSERT OR REPLACE INTO CompactedDays (Day, CompactedAt) VALUES (@day, @at)";
                mark.Parameters.AddWithValue("@day", day);
                mark.Parameters.AddWithValue("@at", now);
                mark.ExecuteNonQuery();
            }
        }
        transaction.Commit();

        return (days, removed);
    }

    private static void TryVacuum(string path)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // VACUUM 失败（比如库被占用）不影响压缩结果，文件只是没缩小而已。
        }
    }
}
