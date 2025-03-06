using GameScoreCalculator.Models;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Media;

namespace GameScoreCalculator.Helpers;

public static class SteamRequestHelper
{
    private static readonly HttpClient _httpClient = CreateSteamClient();
    private const int _initialDelay = 5000;
    private const int _maxRetries = 100;
    private static int _currentDelay = _initialDelay;
    private const int _maxDelay = 20000;

    private static HttpClient CreateSteamClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        handler.CookieContainer.Add(new Cookie(
            name: "birthtime",
            value: "283993201",  // January 1, 1970
            path: "/",
            domain: "store.steampowered.com")
        );

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        return client;
    }

    public static async Task<List<SteamApp>> FetchAllSteamApps()
    {
        var response = await _httpClient.GetStringAsync("https://api.steampowered.com/ISteamApps/GetAppList/v2/");
        return JsonSerializer.Deserialize<SteamAppList>(response)?.AppList?.Apps ?? [];
    }

    public static async Task<AppDetails> FetchAppDetails(uint appId, CancellationToken ct)
    {
        int retryCount = 0;

        while (true)
        {
            try
            {
                PushMessage($"[API] Fetching details for AppID {appId} (attempt {retryCount + 1})");
                var response = await _httpClient.GetAsync($"https://store.steampowered.com/api/appdetails?appids={appId}", ct);

                HandleSpecialStatusCodes(response.StatusCode);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, AppDetailsResponse>>(content);

                if (data?.TryGetValue(appId.ToString(), out var result) != true || result?.Data == null)
                {
                    PushMessage($"[WARNING] Empty response for AppID {appId}");
                    return null;
                }

                ResetDelay();
                return result.Data;
            }
            catch (HttpRequestException ex) when (IsRetryableNetworkError(ex))
            {
                if (retryCount >= _maxRetries)
                {
                    PushMessage($"[ERROR] AppID {appId} failed after {_maxRetries} retries ({ex.Message})", Colors.Red);
                    return null;
                }

                await HandleRetry(ex.Message, retryCount, ct);
                retryCount++;
            }
            catch (Exception ex)
            {
                PushMessage($"[ERROR] AppID {appId} details fetch failed: {ex.Message}", Colors.Red);
                return null;
            }
        }
    }

    public static async Task<ReviewCounts> GetReviewCounts(uint appId, string filter, CancellationToken ct)
    {
        int retryCount = 0;

        while (true)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var response = await _httpClient.GetAsync(
                    $"https://store.steampowered.com/appreviews/{appId}?json=1&filter={filter}&num_per_page=0", ct);

                HandleSpecialStatusCodes(response.StatusCode);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ReviewResponse>(content)?.QuerySummary ?? new ReviewCounts();

                ResetDelay();
                return result;
            }
            catch (HttpRequestException ex) when (IsRetryableNetworkError(ex))
            {
                if (retryCount >= _maxRetries)
                {
                    PushMessage($"[ERROR] {filter} reviews failed after {_maxRetries} retries ({ex.Message})", Colors.Red);
                    return new ReviewCounts();
                }

                await HandleRetry(ex.Message, retryCount, ct);
                retryCount++;
            }
            catch (Exception ex)
            {
                PushMessage($"[ERROR] {filter} reviews failed: {ex.Message}", Colors.Red);
                return new ReviewCounts();
            }
        }
    }

    public static async Task<string> FetchStorePage(uint appId, CancellationToken ct)
    {
        int retryCount = 0;

        while (true)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var response = await _httpClient.GetAsync($"https://store.steampowered.com/app/{appId}", ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                if (retryCount >= _maxRetries)
                {
                    PushMessage($"[ERROR] Failed to fetch store page for {appId}: {ex.Message}", Colors.Red);

                    return string.Empty;
                }

                await HandleRetry(ex.Message, retryCount, ct);
                retryCount++;
            }
        }
    }

    private static void HandleSpecialStatusCodes(HttpStatusCode statusCode)
    {
        if ((int)statusCode == 429)
        {
            throw new HttpRequestException("Rate limit exceeded", null, HttpStatusCode.TooManyRequests);
        }
        if ((int)statusCode == 503)
        {
            throw new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static bool IsRetryableNetworkError(HttpRequestException ex)
    {
        if (ex.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode switch
            {
                SocketError.HostNotFound => true,
                SocketError.HostUnreachable => true,
                SocketError.ConnectionRefused => true,
                SocketError.TimedOut => true,
                _ => false
            };
        }

        return ex.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,
            _ => false
        };
    }

    private static async Task HandleRetry(string errorMessage, int retryCount, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var backoffDelay = CalculateBackoffDelay(retryCount);
        PushMessage($"[WARNING] Network error: {errorMessage}. Retrying in {backoffDelay}ms...", Colors.Yellow);
        await Task.Delay(backoffDelay, ct);
    }

    private static int CalculateBackoffDelay(int retryCount)
    {
        var jitter = new Random().Next(500, 1000);
        var delay = 2 * retryCount * _currentDelay + jitter;
        _currentDelay = delay <= _maxDelay ? delay : _maxDelay + jitter;
        return _currentDelay;
    }

    private static void ResetDelay() => _currentDelay = _initialDelay;

    private static void PushMessage(string message)
    {
        PushMessage(message, Colors.White);
    }

    private static void PushMessage(string message, Color color)
    {
        MessageBus.Publish(new OutputMessage(message, color));
    }
}