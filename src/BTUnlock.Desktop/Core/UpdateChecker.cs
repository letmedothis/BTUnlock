using System.Net.Http;
using System.Text.Json;

namespace BTUnlock.Desktop.Core;

internal static class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/letmedothis/BTUnlock/releases/latest";
    private const string LatestApi = "https://api.github.com/repos/letmedothis/BTUnlock/releases/latest";

    /// <summary>比较 GitHub 最新 Release 与本程序版本（0.1.N 的 N）。网络失败不抛异常。</summary>
    public static async Task<(bool HasUpdate, string? LatestTag, string Reason)> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BTUnlock");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (tag is null) return (false, null, "响应缺少 tag_name");

            var parts = tag.TrimStart('v').Split('.');
            var current = typeof(UpdateChecker).Assembly.GetName().Version;
            if (parts.Length == 3 && int.TryParse(parts[2], out var latest) &&
                current is { } cur && cur.Build > 0)
            {
                return latest > cur.Build
                    ? (true, tag, $"最新 {tag}，当前 v{cur}")
                    : (false, tag, $"已是最新（v{cur}）");
            }
            return (false, tag, "版本号格式无法比较");
        }
        catch (Exception ex)
        {
            return (false, null, $"网络检查失败：{ex.Message}");
        }
    }
}
