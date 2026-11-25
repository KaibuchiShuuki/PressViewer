using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class GamepadDatabaseService : IGamepadDatabaseService
{
    private static readonly string ConnectionString = InitializeConnectionString();

    private static string InitializeConnectionString()
    {
        string folder = AppDataPathHelper.GetAppDataFolder();
        Directory.CreateDirectory(folder);
        string dbPath = Path.Combine(folder, "gamepad_data.db");
        return $"Data Source={dbPath}";
    }

    public GamepadDatabaseService()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        // GamepadPressDaily / CompactedDays 供「压缩历史数据」使用：老记录按天汇总后明细删除。
        cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS GamepadPressRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Button TEXT NOT NULL,
                PressTime TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS GamepadPressDaily (
                Button TEXT NOT NULL,
                Day TEXT NOT NULL,
                Count INTEGER NOT NULL,
                PRIMARY KEY (Button, Day)
            );
            CREATE TABLE IF NOT EXISTS CompactedDays (
                Day TEXT PRIMARY KEY,
                CompactedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_GamepadPressRecords_Time ON GamepadPressRecords(PressTime);";
        cmd.ExecuteNonQuery();
    }

    public void SavePress(GamepadPressRecord record)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO GamepadPressRecords (Button, PressTime) VALUES (@b, @t)";
        cmd.Parameters.AddWithValue("@b", record.Button);
        cmd.Parameters.AddWithValue("@t", record.PressTime.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, int> GetPressCounts() => GetPressCounts(null, null);

    /// <summary>按范围统计每个手柄键的次数。明细与按天汇总两边相加，压缩过历史数据也能算对。</summary>
    public Dictionary<string, int> GetPressCounts(DateTime? from, DateTime? to)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in GamepadButtonIds.All)
            counts[id] = 0;

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        if (from.HasValue && to.HasValue)
        {
            cmd.CommandText =
                @"
                SELECT Button, COUNT(*)
                FROM GamepadPressRecords
                WHERE PressTime >= @from AND PressTime < @to
                GROUP BY Button";
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("o"));
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("o"));
        }
        else
        {
            cmd.CommandText = "SELECT Button, COUNT(*) FROM GamepadPressRecords GROUP BY Button";
        }

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        var daily = connection.CreateCommand();
        if (from.HasValue && to.HasValue)
        {
            daily.CommandText =
                @"
                SELECT Button, SUM(Count)
                FROM GamepadPressDaily
                WHERE Day >= @fromDay AND Day < @toDay
                GROUP BY Button";
            daily.Parameters.AddWithValue("@fromDay", from.Value.ToString("yyyy-MM-dd"));
            daily.Parameters.AddWithValue("@toDay", to.Value.ToString("yyyy-MM-dd"));
        }
        else
        {
            daily.CommandText = "SELECT Button, SUM(Count) FROM GamepadPressDaily GROUP BY Button";
        }

        using (var reader = daily.ExecuteReader())
        {
            while (reader.Read())
            {
                string button = reader.GetString(0);
                int value = (int)reader.GetInt64(1);
                counts[button] = counts.TryGetValue(button, out int old) ? old + value : value;
            }
        }

        return counts;
    }
}
