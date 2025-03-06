using GameScoreCalculator.Models;
using Microsoft.Data.Sqlite;

namespace GameScoreCalculator.Helpers;

public static class DBHelper
{
    private const string _dbConnectionString = "Data Source=steam_data.db";

    public static void Initialize()
    {
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Games (
                AppId INTEGER PRIMARY KEY,
                Name TEXT,
                ReleaseDate TEXT,
                Genres TEXT,
                RecentReviews TEXT,
                AllReviews TEXT,
                PositiveAllReviews INTEGER,
                NegativeAllReviews INTEGER,
                LastFetched TEXT
            );

            CREATE TABLE IF NOT EXISTS ExcludedApps (
                AppId INTEGER PRIMARY KEY,
                IsNonGame INTEGER,
                NoDetails INTEGER DEFAULT 0,
                NotEnoughReviews INTEGER DEFAULT 0,
                ExcludedDate TEXT
            );";
        cmd.ExecuteNonQuery();
    }

    public static (HashSet<ExcludedApp> ExcludedApps, HashSet<Game> ExistingGames) LoadExistingData()
    {
        var excluded = new HashSet<ExcludedApp>();
        var games = new HashSet<Game>();

        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT AppId, ExcludedDate, IsNonGame, NoDetails, NotEnoughReviews FROM ExcludedApps";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (DateTime.TryParse(reader["ExcludedDate"].ToString(), out var date))
                {
                    excluded.Add(new ExcludedApp(
                        Convert.ToUInt32(reader["AppId"]),
                        date,
                        Convert.ToUInt32(reader["IsNonGame"]) == 1,
                        Convert.ToUInt32(reader["NoDetails"]) == 1,
                        Convert.ToUInt32(reader["NotEnoughReviews"]) == 1
                    ));
                }
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT AppId, LastFetched FROM Games";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (DateTime.TryParse(reader["LastFetched"].ToString(), out var date))
                {
                    games.Add(new Game()
                    {
                        AppId = Convert.ToUInt32(reader["AppId"]),
                        LastFetched = date
                    });
                }
            }
        }

        return (excluded, games);
    }

    public static void SaveProcessedData(ProcessedData data)
    {
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            //Remove excluded apps
            foreach (var app in data.ExcludedToRemove)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM ExcludedApps 
                    WHERE AppId = $id";
                cmd.Parameters.AddWithValue("$id", app.AppId);
                cmd.ExecuteNonQuery();
            }

            // Save excluded apps
            foreach (var app in data.ExcludedToAdd)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ExcludedApps 
                    (AppId, IsNonGame, NoDetails, NotEnoughReviews, ExcludedDate)
                    VALUES ($id, $nong, $det, $rev, $date)
                    ON CONFLICT(AppId) DO UPDATE SET
                        IsNonGame = excluded.IsNonGame,
                        NoDetails = excluded.NoDetails,
                        NotEnoughReviews = excluded.NotEnoughReviews,
                        ExcludedDate = excluded.ExcludedDate";

                cmd.Parameters.AddWithValue("$id", app.AppId);
                cmd.Parameters.AddWithValue("$nong", app.IsNonGame);
                cmd.Parameters.AddWithValue("$det", app.NoDetails);
                cmd.Parameters.AddWithValue("$rev", app.NotEnoughReviews);
                cmd.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("o"));

                cmd.ExecuteNonQuery();
            }

            // Save games
            foreach (var game in data.GamesToAdd)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Games (
                        AppId, Name, ReleaseDate, Genres, 
                        RecentReviews, AllReviews, 
                        PositiveAllReviews, NegativeAllReviews, 
                        LastFetched
                    )
                    VALUES (
                        $id, $name, $date, $genres, 
                        $recent, $all, 
                        $positiveAll, $negativeAll, 
                        $fetched
                    )
                    ON CONFLICT(AppId) DO UPDATE SET
                        Name = excluded.Name,
                        ReleaseDate = excluded.ReleaseDate,
                        Genres = excluded.Genres,
                        RecentReviews = excluded.RecentReviews,
                        AllReviews = excluded.AllReviews,
                        PositiveAllReviews = excluded.PositiveAllReviews,
                        NegativeAllReviews = excluded.NegativeAllReviews,
                        LastFetched = excluded.LastFetched";

                cmd.Parameters.AddWithValue("$id", game.AppId);
                cmd.Parameters.AddWithValue("$name", game.Name);
                cmd.Parameters.AddWithValue("$date", game.ReleaseDate.ToString("o"));
                cmd.Parameters.AddWithValue("$genres", game.Genres);
                cmd.Parameters.AddWithValue("$recent", game.RecentReviews);
                cmd.Parameters.AddWithValue("$all", game.AllReviews);
                cmd.Parameters.AddWithValue("$positiveAll", game.PositiveAllReviews);
                cmd.Parameters.AddWithValue("$negativeAll", game.NegativeAllReviews);
                cmd.Parameters.AddWithValue("$fetched", game.LastFetched.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static List<Game> LoadGames()
    {
        var games = new List<Game>();

        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Games";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            games.Add(new Game
            {
                AppId = Convert.ToUInt32(reader["AppId"]),
                Name = reader["Name"]?.ToString() ?? string.Empty,
                Genres = reader["Genres"]?.ToString() ?? string.Empty,
                ReleaseDate = DateTime.Parse(reader["ReleaseDate"]?.ToString() ?? string.Empty),
                RecentReviews = reader["RecentReviews"]?.ToString() ?? string.Empty,
                AllReviews = reader["AllReviews"]?.ToString() ?? string.Empty,
                PositiveAllReviews = Convert.ToInt32(reader["PositiveAllReviews"]),
                NegativeAllReviews = Convert.ToInt32(reader["NegativeAllReviews"]),
                LastFetched = DateTime.Parse(reader["LastFetched"]?.ToString() ?? string.Empty)
            });
        }

        return games;
    }
}
