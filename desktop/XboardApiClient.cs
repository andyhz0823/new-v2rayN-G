using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace V2rayNG.Xboard;

public sealed record XboardLoginResult(string SubscriptionToken, string SanctumToken, bool IsAdmin);

public sealed record XboardSubscriptionProfile(
    string SubscribeUrl,
    string? Name = null,
    long? PlanId = null,
    long? SubscriptionId = null,
    long? Upload = null,
    long? Download = null,
    long? Total = null,
    long? ExpireAt = null,
    string? Source = null,
    bool? IsExternal = null)
{
    [JsonIgnore]
    public bool IsUsable
    {
        get
        {
            if (ExpireAt is { } expiry && expiry > 0)
            {
                var expiryDate = expiry > 9_999_999_999L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(expiry)
                    : DateTimeOffset.FromUnixTimeSeconds(expiry);
                if (expiryDate <= DateTimeOffset.UtcNow) return false;
            }

            return Total is not { } total || (Upload ?? 0) + (Download ?? 0) < total;
        }
    }
}

public sealed record XboardSubscribeResult(
    string SubscribeUrl,
    IReadOnlyList<XboardSubscriptionProfile> Subscriptions,
    long? PlanId,
    string? ExpiredAt,
    string? Email,
    string? Token);

public sealed class XboardApiException : Exception
{
    public XboardApiException(string message) : base(message) { }
}

public sealed class XboardApiClient
{
    private readonly HttpClient _http;
    private readonly Uri _panelUri;
    private string? _sanctumToken;

    public XboardApiClient(string panelUrl, HttpClient? httpClient = null)
    {
        if (!Uri.TryCreate(NormalizeHttpUrl(panelUrl), UriKind.Absolute, out var panelUri) ||
            panelUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Panel URL must be an absolute HTTP(S) URL.", nameof(panelUrl));

        _panelUri = new Uri(panelUri, panelUri.AbsolutePath.TrimEnd('/') + "/");
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("v2rayn-g/1.0");
    }

    public async Task<XboardSubscribeResult> LoginAndGetSubscriptionsAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new XboardApiException("Email and password are required.");

        using var loginPayload = JsonContent.Create(new { email, password });
        using var loginResponse = await _http.PostAsync(
            new Uri(_panelUri, "api/v1/passport/auth/login"), loginPayload, cancellationToken);
        var loginJson = await ReadJsonObjectAsync(loginResponse, cancellationToken);
        var loginData = RequireData(loginJson, "Login failed, please check account and password");

        var subscriptionToken = GetString(loginData, "token");
        var authData = GetString(loginData, "auth_data");
        if (string.IsNullOrWhiteSpace(subscriptionToken) || string.IsNullOrWhiteSpace(authData))
            throw new XboardApiException("Login response is incomplete.");

        _sanctumToken = authData.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authData[7..].Trim()
            : authData.Trim();
        return await GetSubscriptionsAsync(subscriptionToken, cancellationToken);
    }

    public async Task<XboardSubscribeResult> GetSubscriptionsAsync(
        string? subscriptionToken = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(_panelUri, "api/v1/user/getSubscribe"));
        if (!string.IsNullOrWhiteSpace(_sanctumToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sanctumToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await ReadJsonObjectAsync(response, cancellationToken);
        var data = RequireData(json, "Subscription response is invalid");
        return ParseSubscribeResponse(data, _panelUri, subscriptionToken);
    }

    public static XboardSubscribeResult ParseSubscribeResponse(
        JsonElement data, Uri panelUri, string? subscriptionToken = null)
    {
        var raw = FirstArray(data, "subscriptions", "plans", "profiles");
        var profiles = raw
            .Select(item => ParseProfile(item, panelUri))
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.SubscribeUrl))
            .Select(item => item!)
            .GroupBy(item => item.SubscriptionId is { } id ? $"id:{id}" : $"url:{item.SubscribeUrl}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var fallbackUrl = FirstString(data, "subscribe_url", "subscription_url", "subscribeUrl", "url", "link");
        if (profiles.Count == 0 && !string.IsNullOrWhiteSpace(fallbackUrl))
        {
            profiles.Add(new XboardSubscriptionProfile(
                NormalizeSubscriptionUrl(fallbackUrl, panelUri),
                FirstString(data, "plan_name", "name", "title"),
                IntOrNull(data, "plan_id"), null,
                IntOrNull(data, "u", "upload"), IntOrNull(data, "d", "download"),
                IntOrNull(data, "transfer_enable", "total"), IntOrNull(data, "expired_at", "expire_at", "expire")));
        }

        return new XboardSubscribeResult(
            profiles.FirstOrDefault()?.SubscribeUrl ?? string.Empty,
            profiles,
            IntOrNull(data, "plan_id"),
            FirstString(data, "expired_at", "expire_at", "expire"),
            FirstString(data, "email"),
            subscriptionToken ?? FirstString(data, "token"));
    }

    private static XboardSubscriptionProfile? ParseProfile(JsonElement item, Uri panelUri)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var url = FirstString(item, "subscribe_url", "subscription_url", "subscribeUrl", "url", "link");
        if (string.IsNullOrWhiteSpace(url)) return null;
        var parsed = Uri.TryCreate(NormalizeHttpUrl(url), UriKind.Absolute, out var uri) ? uri : null;
        var explicitExternal = item.TryGetProperty("is_external", out var externalValue) &&
            externalValue.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? externalValue.GetBoolean()
            : (bool?)null;
        var isExternal = explicitExternal ?? (parsed is not null &&
            !string.Equals(parsed.Host, panelUri.Host, StringComparison.OrdinalIgnoreCase));

        return new XboardSubscriptionProfile(
            NormalizeSubscriptionUrl(url, panelUri, isExternal),
            FirstString(item, "plan_name", "name", "title"),
            IntOrNull(item, "plan_id"), IntOrNull(item, "id", "subscription_id"),
            IntOrNull(item, "u", "upload"), IntOrNull(item, "d", "download"),
            IntOrNull(item, "transfer_enable", "total"), IntOrNull(item, "expired_at", "expire_at", "expire"),
            FirstString(item, "source"), isExternal);
    }

    private static async Task<JsonElement> ReadJsonObjectAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new XboardApiException($"Xboard request failed ({(int)response.StatusCode}).");
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new XboardApiException("Xboard returned invalid JSON.");
        }
    }

    private static JsonElement RequireData(JsonElement envelope, string fallbackMessage)
    {
        if (envelope.ValueKind != JsonValueKind.Object ||
            !envelope.TryGetProperty("status", out var status) ||
            !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            throw new XboardApiException(GetString(envelope, "message") is { Length: > 0 } message ? message : fallbackMessage);
        if (!envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new XboardApiException("Xboard response data is invalid.");
        return data;
    }

    private static IEnumerable<JsonElement> FirstArray(JsonElement data, params string[] keys)
    {
        foreach (var key in keys)
            if (data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray();
        return Array.Empty<JsonElement>();
    }

    private static string FirstString(JsonElement data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            var text = value.ToString().Trim();
            if (text.Length > 0) return text;
        }
        return string.Empty;
    }

    private static long? IntOrNull(JsonElement data, params string[] keys)
    {
        var text = FirstString(data, keys);
        return long.TryParse(text, out var value) ? value : null;
    }

    public static string NormalizeHttpUrl(string url)
    {
        var value = url.Trim();
        if (value.Length == 0) return value;
        return value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}";
    }

    public static string NormalizeSubscriptionUrl(string url, Uri panelUri, bool? external = null)
    {
        var normalized = NormalizeHttpUrl(url);
        if (external == true || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, panelUri.Host, StringComparison.OrdinalIgnoreCase))
            return normalized;
        if (uri.Query.Contains("flag=", StringComparison.OrdinalIgnoreCase)) return normalized;
        var builder = new UriBuilder(uri)
        {
            Query = string.IsNullOrWhiteSpace(uri.Query)
                ? "flag=v2rayn-g"
                : $"{uri.Query.TrimStart('?')}&flag=v2rayn-g"
        };
        return builder.Uri.ToString();
    }

    private static string GetString(JsonElement data, string key) =>
        data.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString().Trim() : string.Empty;
}
