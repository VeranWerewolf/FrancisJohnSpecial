using GameScoreCalculator.Helpers;
using GameScoreCalculator.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace GameScoreCalculator;

public static class Processor
{
    public static async Task Start(ProcessorSettings settings, CancellationToken ct)
    {
        PushMessage("[START] Steam Data Processing");
        try
        {
            DBHelper.Initialize();
            PushMessage("[DATABASE] Initialized successfully");

            if (settings.AddNew || settings.UpdateExisting || settings.UpdateExcludedByAppDetails || settings.UpdateExcludedByReviewThreshold)
            {
                await ProcessSteamApps(settings, ct);
                PushMessage($"[PROCESS] Processed successfully");
            }

            if (settings.CreateExport)
            {
                CalculateAndExportScores();

                PushMessage($"[EXPORT] Exported successfully");
            }

            PushMessage($"[PROCESS] Finished.", Colors.Green);
        }
        catch (OperationCanceledException)
        {
            PushMessage("[CANCELLED] Processing was stopped by user.", Colors.Orange);
        }
        catch (Exception ex)
        {
            PushMessage($"[ERROR] {ex.Message}", Colors.Red);
        }
    }

    private static async Task ProcessSteamApps(ProcessorSettings settings, CancellationToken ct)
    {
        PushMessage("[FETCH] Retrieving all Steam apps...");
        var allApps = (await SteamRequestHelper.FetchAllSteamApps())
            .GroupBy(app => app.AppId)
            .Select(g => g.First())
            .OrderBy(app => app.AppId)
            .ToList();
        PushMessage($"[DATA] Found {allApps.Count} total apps in Steam catalog");

        var (excludedApps, existingGames) = DBHelper.LoadExistingData();
        PushMessage($"[CACHE] Loaded {existingGames.Count} existing games and {excludedApps.Count} excluded apps");

        var result = new ProcessedData();
        var totalProcessed = 0;
        var processStartTime = DateTime.Now;

        var filteredApps = FilterApps(settings, allApps, excludedApps, existingGames);
        PushMessage($"[DATA] Filtered {filteredApps.Count} total apps to process");

        //ToDo: only update reviews for previously processed apps
        foreach (var app in filteredApps)
        {
            ct.ThrowIfCancellationRequested();

            if (totalProcessed % 10 == 0 && totalProcessed > 0)
            {
                DBHelper.SaveProcessedData(result);
                result = new ProcessedData();
                PushMessage($"[PROGRESS] Processed {totalProcessed}/{filteredApps.Count} apps ({DateTime.Now - processStartTime:hh\\:mm\\:ss})", Colors.Cyan);
            }

            totalProcessed++;

            PushMessage($"[PROCESS] AppID {app.AppId} ({app.Name})");
            var details = await SteamRequestHelper.FetchAppDetails(app.AppId, ct);
            ct.ThrowIfCancellationRequested();

            if (details == null)
            {
                PushMessage($"[WARNING] Failed to fetch details for AppID {app.AppId}", Colors.Yellow);
                PushMessage($"[EXCLUDE] AppID {app.AppId} (no details)", Colors.Yellow);

                result.ExcludedToAdd.Add(new ExcludedApp(app.AppId, NoDetails: true));
                continue;
            }

            if (!IsValidGame(details))
            {
                var reason = details?.Type != "game" ? "non-game" : "invalid release date";
                PushMessage($"[EXCLUDE] AppID {app.AppId} ({reason})");
                var isNonGame = details?.Type != "game";
                result.ExcludedToAdd.Add(new ExcludedApp(app.AppId, IsNonGame: isNonGame, NoDetails: !isNonGame));
                continue;
            }

            PushMessage($"[REVIEWS] Fetching review data for {details.Name} ({app.AppId})");
            var reviews = await FetchReviewData(app.AppId, ct);
            ct.ThrowIfCancellationRequested();

            var totalReviews = reviews.PositiveAllReviews + reviews.NegativeAllReviews;

            if (totalReviews <= settings.ReviewThreshold && !existingGames.Where(game => game.AppId == app.AppId).Any())
            {
                PushMessage($"[EXCLUDE] AppID {app.AppId} (only {totalReviews} total reviews)", Colors.Yellow);

                result.ExcludedToAdd.Add(new ExcludedApp(app.AppId, NotEnoughReviews: true));
                continue;
            }

            var game = CreateGameRecord(details, reviews);
            result.GamesToAdd.Add(game);
            PushMessage($"[ADDED] {game.Name} ({game.AppId}) with {totalReviews} reviews", Colors.Green);

            if (excludedApps.Select(x => x.AppId == game.AppId).Any())
            {
                result.ExcludedToRemove.Add(new ExcludedApp(app.AppId));
            }

            if (game.AppId != app.AppId)
            {
                PushMessage($"[EXCLUDE] REDIRECT {app.AppId}", Colors.Yellow);

                result.ExcludedToAdd.Add(new ExcludedApp(app.AppId, IsNonGame: true));
            }
        }

        DBHelper.SaveProcessedData(result);
    }

    private static List<SteamApp> FilterApps(ProcessorSettings settings, List<SteamApp> allApps, HashSet<ExcludedApp> excludedApps, HashSet<Game> existingGames)
    {
        DateTime today = DateTime.UtcNow;
        var excludedById = excludedApps.ToDictionary(e => e.AppId);
        var existingById = existingGames.ToDictionary(g => g.AppId);

        return allApps.Where(app =>
        {
            if (excludedById.TryGetValue(app.AppId, out var excluded))
            {
                if (excluded.IsNonGame) return false;
                if (!settings.UpdateExcludedByReviewThreshold && excluded.NotEnoughReviews) return false;
                if (!settings.UpdateExcludedByAppDetails && excluded.NoDetails) return false;
                if ((today - excluded.ExcludedDate).TotalDays < settings.DaysIgnored) return false;
                return true;
            }

            if (existingById.TryGetValue(app.AppId, out var game))
            {
                if (!settings.UpdateExisting) return false;
                if ((today - game.LastFetched).TotalDays < settings.DaysIgnored) return false;
                return true;
            }

            return settings.AddNew;
        }).ToList();
    }

    private static async Task<ReviewData> FetchReviewData(uint appId, CancellationToken ct)
    {
        PushMessage($"[API] Fetching reviews for AppID {appId}");
        var all = await SteamRequestHelper.GetReviewCounts(appId, "all", ct);
        var html = await SteamRequestHelper.FetchStorePage(appId, ct);
        var (Recent, All) = GetGameReviews(html);

        return new ReviewData
        {
            PositiveAllReviews = all.TotalPositive,
            NegativeAllReviews = all.TotalNegative,
            Recent = Recent,
            All = All
        };
    }

    public static (ReviewSummary Recent, ReviewSummary All) GetGameReviews(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var recentSummary = new ReviewSummary();
        var allSummary = new ReviewSummary();

        // Find user reviews section
        var userReviewsNode = doc.DocumentNode.SelectSingleNode("//div[@id='userReviews']");

        if (userReviewsNode != null)
        {
            var summaryRows = userReviewsNode.SelectNodes(".//div[@class='user_reviews_summary_row']");

            foreach (var row in summaryRows)
            {
                var subtitleNode = row.SelectSingleNode(".//div[contains(@class, 'subtitle')]");
                var summaryNode = row.SelectSingleNode(".//span[contains(@class, 'game_review_summary')]");
                var tooltip = row.GetAttributeValue("data-tooltip-html", "");

                if (subtitleNode?.InnerText.Trim() == "Recent Reviews:")
                {
                    recentSummary.Text = summaryNode?.InnerText.Trim();
                    ParseTooltip(tooltip, out int total);
                    recentSummary.Total = total;
                }
                else if (subtitleNode?.InnerText.Trim() == "All Reviews:")
                {
                    allSummary.Text = summaryNode?.InnerText.Trim();
                    ParseTooltip(tooltip, out int total);
                    allSummary.Total = total;
                }
            }
        }

        return (recentSummary, allSummary);
    }

    private static void ParseTooltip(string tooltip, out int total)
    {
        total = 0;

        var match = Regex.Match(tooltip, @"(\d+)% of the ([\d,]+) user reviews");
        if (match.Success)
        {
            int.TryParse(match.Groups[2].Value.Replace(",", ""), out total);
        }
    }

    private static bool IsValidGame(AppDetails details) =>
        details?.Type == "game" && !string.IsNullOrEmpty(details.ReleaseDate?.Date);

    private static Game CreateGameRecord(AppDetails details, ReviewData reviews)
    {
        if (details.Genres == null || details.Genres.Count == 0)
        {
            PushMessage($"[WARNING] No genres found for {details.Name}", Colors.Yellow);
        }

        try
        {
            if (!DateTime.TryParse(details.ReleaseDate.Date.Split('-').First(), out var releaseDate))
            {
                // Extract year from the string using regex
                var match = Regex.Match(details.ReleaseDate.Date, @"\d{4}");
                releaseDate = match.Success ? new DateTime(int.Parse(match.Value), 1, 1) : DateTime.MinValue;
            }
            return new Game
            {
                AppId = details.SteamAppId,
                Name = details.Name,
                ReleaseDate = releaseDate,
                Genres = details.Genres != null ? string.Join(", ", details.Genres.Select(g => g.Description)) : "Unknown",
                RecentReviews = reviews.Recent.Text ?? reviews.All.Text ?? string.Empty,
                AllReviews = reviews.All.Text ?? reviews.Recent.Text ?? string.Empty,
                PositiveAllReviews = reviews.PositiveAllReviews,
                NegativeAllReviews = reviews.NegativeAllReviews,
                LastFetched = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            PushMessage($"[ERROR] Failed to parse release date for {details.Name}: {details.ReleaseDate?.Date}", Colors.Yellow);
            throw;
        }
    }

    private static void CalculateAndExportScores()
    {
        PushMessage("[SCORING] Calculating game scores...");
        var games = DBHelper.LoadGames();
        var scoredGames = new List<ScoredGame>();

        foreach (var game in games)
        {
            var scoredGame = ExportHelper.CalculateScores(game);
            scoredGames.Add(scoredGame);
        }

        ExportHelper.ExportToCsv(scoredGames);
        PushMessage($"[EXPORT] Generated scores for {scoredGames.Count} games", Colors.Green);
    }

    private static void PushMessage(string message)
    {
        PushMessage(message, Colors.White);
    }

    private static void PushMessage(string message, Color color)
    {
        MessageBus.Publish(new OutputMessage(message, color));
    }
}