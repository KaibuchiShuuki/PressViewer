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
        cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS GamepadPressRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Button TEXT NOT NULL,
                PressTime TEXT NOT NULL
            );";
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

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }
}
