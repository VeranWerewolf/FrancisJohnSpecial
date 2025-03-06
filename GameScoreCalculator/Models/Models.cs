using System.Text.Json.Serialization;
using System.Windows.Media;

namespace GameScoreCalculator.Models;

public record OutputMessage(string Text, Color Color);

public class ProcessorSettings
{
    public int ReviewThreshold { get; set; }
    public int DaysIgnored { get; set; }
    public bool AddNew { get; set; }
    public bool UpdateExisting { get; set; }
    public bool UpdateExcludedByReviewThreshold { get; set; }
    public bool UpdateExcludedByAppDetails { get; set; }
    public bool CreateExport { get; set; }
}

public class SteamAppList
{
    [JsonPropertyName("applist")]
    public AppList AppList { get; set; } = new AppList();
}

public class AppList
{
    [JsonPropertyName("apps")]
    public List<SteamApp> Apps { get; set; } = [];
}

public class ReviewCounts
{
    [JsonPropertyName("total_positive")]
    public int TotalPositive { get; set; }
    [JsonPropertyName("total_negative")]
    public int TotalNegative { get; set; }
}

public class SteamApp
{
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class AppDetailsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("data")]
    public AppDetails Data { get; set; }
}

public class AppDetails
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("release_date")]
    public ReleaseDate ReleaseDate { get; set; }
    [JsonPropertyName("genres")]
    public List<Genre> Genres { get; set; }
    [JsonPropertyName("steam_appid")]
    public uint SteamAppId { get; set; }
}

public class ReleaseDate
{
    [JsonPropertyName("date")]
    public string Date { get; set; }
}

public class Genre
{
    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class ReviewResponse
{
    [JsonPropertyName("query_summary")]
    public ReviewCounts QuerySummary { get; set; }
}

public record ReviewData
{
    public int PositiveAllReviews { get; set; }
    public int NegativeAllReviews { get; set; }
    public ReviewSummary Recent { get; set; }
    public ReviewSummary All { get; set; }
}

public record Game
{
    public uint AppId { get; set; }
    public string Name { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string Genres { get; set; }
    public string RecentReviews { get; set; }
    public string AllReviews { get; set; }
    public int PositiveAllReviews { get; set; }
    public int NegativeAllReviews { get; set; }
    public DateTime LastFetched { get; set; }
}

public record ExcludedApp(uint AppId, DateTime ExcludedDate = default, bool IsNonGame = false, bool NoDetails = false, bool NotEnoughReviews = false);

public class ProcessedData
{
    public List<Game> GamesToAdd { get; } = [];
    public List<ExcludedApp> ExcludedToAdd { get; } = [];
    public List<ExcludedApp> ExcludedToRemove { get; } = [];
}

public class ScoredGame
{
    public uint AppId { get; set; }
    public string Name { get; set; }
    public string Genres { get; set; }
    public int RecentScore { get; set; }
    public int AllScore { get; set; }
    public int YearScore { get; set; }
    public int TotalReviewsScore { get; set; }
    public int TotalScore { get; set; }
}

public class ReviewSummary
{
    public string Text { get; set; }
    public int Total { get; set; }
}
