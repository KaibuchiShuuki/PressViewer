using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class ClickDatabaseService : IClickDatabaseService
{
    private static readonly string ConnectionString = InitializeConnectionString();

    private static string InitializeConnectionString()
    {
        // 获取 AppData 目录，不存在则创建
        string appDataFolder = AppDataPathHelper.GetAppDataFolder();
        Directory.CreateDirectory(appDataFolder);

        string dbPath = Path.Combine(appDataFolder, "click_data.db");
        return $"Data Source={dbPath}";
    }

    public ClickDatabaseService()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        // ClickDaily / CompactedDays 供「压缩历史数据」使用：老记录按天汇总后明细删除。
        command.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS ClickRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Button TEXT NOT NULL,
                ClickTime TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ClickDaily (
                Button TEXT NOT NULL,
                Day TEXT NOT NULL,
                Count INTEGER NOT NULL,
                PRIMARY KEY (Button, Day)
            );
            CREATE TABLE IF NOT EXISTS CompactedDays (
                Day TEXT PRIMARY KEY,
                CompactedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ClickRecords_Time ON ClickRecords(ClickTime);
        ";
        command.ExecuteNonQuery();
    }

    public void SaveClick(MouseClickRecord record)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ClickRecords (Button, ClickTime) VALUES (@b, @t)";
        command.Parameters.AddWithValue("@b", record.Button);
        command.Parameters.AddWithValue("@t", record.ClickTime.ToString("o")); // ISO 8601
        command.ExecuteNonQuery();
    }

    /// <summary>某一天的按键次数（明细 + 按天汇总）。</summary>
    public Dictionary<string, int> GetClickCountsByDate(DateTime date)
    {
        return GetClickCounts(date.Date, date.Date.AddDays(1));
    }

    public Dictionary<string, int> GetClickCounts(DateTime from, DateTime toExclusive)
    {
        var counts = NewCounts();

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                @"
        SELECT Button, COUNT(*)
        FROM ClickRecords
        WHERE ClickTime >= @from AND ClickTime < @to
        GROUP BY Button";
            cmd.Parameters.AddWithValue("@from", from.ToString("o"));
            cmd.Parameters.AddWithValue("@to", toExclusive.ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        MergeDaily(connection, counts, from.ToString("yyyy-MM-dd"), toExclusive.ToString("yyyy-MM-dd"));
        return counts;
    }

    // 后续分析用：获取所有记录或聚合数据（压缩过的历史不再有明细）
    public List<MouseClickRecord> GetAllRecords()
    {
        var records = new List<MouseClickRecord>();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Button, ClickTime FROM ClickRecords ORDER BY ClickTime";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(
                new MouseClickRecord
                {
                    Id = reader.GetInt64(0),
                    Button = reader.GetString(1),
                    ClickTime = DateTime.Parse(reader.GetString(2)),
                }
            );
        }
        return records;
    }

    public Dictionary<string, int> GetClickCounts()
    {
        var counts = NewCounts();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Button, COUNT(*) FROM ClickRecords GROUP BY Button";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        MergeDaily(connection, counts, null, null);
        return counts;
    }

    private static Dictionary<string, int> NewCounts() =>
        new()
        {
            { "Left", 0 },
            { "Middle", 0 },
            { "Right", 0 },
        };

    /// <summary>把按天汇总的那部分加进结果；fromDay / toDay 为空表示全部时间。</summary>
    private static void MergeDaily(
        SqliteConnection connection,
        Dictionary<string, int> counts,
        string? fromDay,
        string? toDay
    )
    {
        using var cmd = connection.CreateCommand();
        if (fromDay is not null && toDay is not null)
        {
            cmd.CommandText =
                @"
        SELECT Button, SUM(Count)
        FROM ClickDaily
        WHERE Day >= @fromDay AND Day < @toDay
        GROUP BY Button";
            cmd.Parameters.AddWithValue("@fromDay", fromDay);
            cmd.Parameters.AddWithValue("@toDay", toDay);
        }
        else
        {
            cmd.CommandText = "SELECT Button, SUM(Count) FROM ClickDaily GROUP BY Button";
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string button = reader.GetString(0);
            int value = (int)reader.GetInt64(1);
            counts[button] = counts.TryGetValue(button, out int old) ? old + value : value;
        }
    }
}
