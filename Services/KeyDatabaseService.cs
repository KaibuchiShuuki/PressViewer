using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class KeyDatabaseService : IKeyDatabaseService
{
    private static readonly string ConnectionString = InitializeConnectionString();

    private static string InitializeConnectionString()
    {
        string folder = AppDataPathHelper.GetAppDataFolder();
        Directory.CreateDirectory(folder);
        string dbPath = Path.Combine(folder, "key_data.db");
        return $"Data Source={dbPath}";
    }

    public KeyDatabaseService()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        // KeyPressDaily / CompactedDays 供「压缩历史数据」使用：老记录按天汇总后明细删除。
        cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS KeyPressRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                PressTime TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS KeyPressDaily (
                Key TEXT NOT NULL,
                Day TEXT NOT NULL,
                Count INTEGER NOT NULL,
                PRIMARY KEY (Key, Day)
            );
            CREATE TABLE IF NOT EXISTS CompactedDays (
                Day TEXT PRIMARY KEY,
                CompactedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_KeyPressRecords_Time ON KeyPressRecords(PressTime);";
        cmd.ExecuteNonQuery();
    }

    public void SaveKeyPress(KeyPressRecord record)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO KeyPressRecords (Key, PressTime) VALUES (@k, @t)";
        cmd.Parameters.AddWithValue("@k", record.Key);
        cmd.Parameters.AddWithValue("@t", record.PressTime.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, int> GetKeyCounts()
    {
        return GetKeyCounts(null, null);
    }

    /// <summary>按范围统计每个键的次数。范围为空表示全部时间。
    /// 明细（KeyPressRecords）与按天汇总（KeyPressDaily）两边的数相加，压缩过历史数据也能算对。</summary>
    public Dictionary<string, int> GetKeyCounts(DateTime? from, DateTime? to)
    {
        var counts = new Dictionary<string, int>();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        if (from.HasValue && to.HasValue)
        {
            cmd.CommandText =
                @"
                SELECT Key, COUNT(*)
                FROM KeyPressRecords
                WHERE PressTime >= @from AND PressTime < @to
                GROUP BY Key";
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("o"));
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("o"));
        }
        else
        {
            cmd.CommandText = "SELECT Key, COUNT(*) FROM KeyPressRecords GROUP BY Key";
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
                SELECT Key, SUM(Count)
                FROM KeyPressDaily
                WHERE Day >= @fromDay AND Day < @toDay
                GROUP BY Key";
            daily.Parameters.AddWithValue("@fromDay", from.Value.ToString("yyyy-MM-dd"));
            daily.Parameters.AddWithValue("@toDay", to.Value.ToString("yyyy-MM-dd"));
        }
        else
        {
            daily.CommandText = "SELECT Key, SUM(Count) FROM KeyPressDaily GROUP BY Key";
        }

        using (var reader = daily.ExecuteReader())
        {
            while (reader.Read())
            {
                string key = reader.GetString(0);
                int value = (int)reader.GetInt64(1);
                counts[key] = counts.TryGetValue(key, out int old) ? old + value : value;
            }
        }

        return counts;
    }
}
