using System.Text.Json;
using V2rayNG.Xboard;

var options = ParseArgs(args);
if (options.TryGetValue("fixture", out var fixture))
{
    var json = await File.ReadAllTextAsync(fixture);
    using var document = JsonDocument.Parse(json);
    var data = document.RootElement.TryGetProperty("data", out var value) ? value : document.RootElement;
    var result = XboardApiClient.ParseSubscribeResponse(
        data, new Uri(options.GetValueOrDefault("panel-url", "https://panel.example.com")));
    await WriteResultAsync(result, options.GetValueOrDefault("json-out"));
    return;
}

var panelUrl = Required(options, "panel-url");
var email = Environment.GetEnvironmentVariable("XBOARD_EMAIL") ?? Required(options, "email");
var password = Environment.GetEnvironmentVariable("XBOARD_PASSWORD") ?? Required(options, "password");
var client = new XboardApiClient(panelUrl);
var subscriptions = await client.LoginAndGetSubscriptionsAsync(email, password);
await WriteResultAsync(subscriptions, options.GetValueOrDefault("json-out"));

static async Task WriteResultAsync(XboardSubscribeResult result, string? outputPath)
{
    var usable = result.Subscriptions.Where(item => item.IsUsable).ToArray();
    var payload = new
    {
        subscriptions = usable.Select(item => new
        {
            id = item.SubscriptionId?.ToString() ?? item.SubscribeUrl,
            remarks = item.Name ?? "Xboard subscription",
            url = item.SubscribeUrl,
            source = item.Source,
            external = item.IsExternal,
            planId = item.PlanId,
            upload = item.Upload,
            download = item.Download,
            total = item.Total,
            expireAt = item.ExpireAt
        })
    };
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, json);
        Console.WriteLine($"Wrote {usable.Length} usable subscription profile(s) to {fullPath}.");
    }
    else
    {
        Console.WriteLine($"Received {result.Subscriptions.Count} profile(s); {usable.Length} currently usable.");
        foreach (var item in usable)
            Console.WriteLine($"- {item.Name ?? "unnamed"} [{(item.IsExternal == true ? "external" : "internal")}]");
    }
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) continue;
        result[args[i][2..]] = args[++i];
    }
    return result;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name} (or XBOARD_{name.Replace('-', '_').ToUpperInvariant()}).");
