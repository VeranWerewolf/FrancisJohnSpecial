using GameScoreCalculator.Models;
using System.IO;

namespace GameScoreCalculator.Helpers;

public static class ExportHelper
{
    public static ScoredGame CalculateScores(Game game)
    {
        var scoredGame = new ScoredGame
        {
            AppId = game.AppId,
            Name = game.Name,
            Genres = game.Genres,
            YearScore = CalculateYearScore(game.ReleaseDate),
            TotalReviewsScore = CalculateTotalReviewsScore(game.PositiveAllReviews + game.NegativeAllReviews),
            RecentScore = CalculateReviewScore(
                game.RecentReviews),
            AllScore = CalculateReviewScore(
                game.AllReviews)
        };

        scoredGame.TotalScore = scoredGame.YearScore + scoredGame.TotalReviewsScore +
                               scoredGame.RecentScore + scoredGame.AllScore;

        return scoredGame;
    }

    public static void ExportToCsv(List<ScoredGame> games)
    {
        var csvLines = new List<string>
    {
        "AppId,Name,Genres,RecentReviews score,AllReviews score,Year score,Total reviews score,Score"
    };

        foreach (var game in games)
        {
            csvLines.Add(
                $"{game.AppId}," +
                $"\"{EscapeCsv(game.Name)}\"," +
                $"\"{EscapeCsv(game.Genres)}\"," +
                $"{game.RecentScore}," +
                $"{game.AllScore}," +
                $"{game.YearScore}," +
                $"{game.TotalReviewsScore}," +
                $"{game.TotalScore}"
            );
        }

        File.WriteAllLines("game_scores.csv", csvLines);
    }

    private static string EscapeCsv(string input)
    {
        return input?.Replace("\"", "\"\"") ?? string.Empty;
    }

    private static int CalculateYearScore(DateTime releaseDate)
    {
        int yearsSinceRelease = DateTime.Now.Year - releaseDate.Year;
        int score = 8 - yearsSinceRelease;
        return Math.Max(score, 0);
    }

    private static int CalculateTotalReviewsScore(int totalReviews)
    {
        return totalReviews switch
        {
            < 2500 => 0,
            < 5000 => 1,
            < 10000 => 4,
            < 20000 => 6,
            < 30000 => 8,
            _ => 10 + Math.Min((totalReviews - 30000) / 10000, 7)
        };
    }

    private static int CalculateReviewScore(string reviews)
    {
        if (string.IsNullOrEmpty(reviews)) return 0;

        var text = reviews.ToLowerInvariant();
        return text switch
        {
            var t when string.Equals(text, "overwhelmingly positive") => 10,
            var t when string.Equals(text, "very positive") => 9,
            var t when string.Equals(text, "positive") => 8,
            var t when string.Equals(text, "mostly positive") => 6,
            var t when string.Equals(text, "mixed") => 4,
            var t when string.Equals(text, "mostly negative") => 3,
            var t when string.Equals(text, "negative") => 2,
            var t when string.Equals(text, "very negative ") => 1,
            var t when string.Equals(text, "overwhelmingly negative") => 0,
            _ => 0
        };
    }
}
