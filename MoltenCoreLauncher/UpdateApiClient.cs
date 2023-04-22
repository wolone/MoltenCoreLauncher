using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoltenCoreLauncher;

public class UpdateApiClient
{
    private readonly LauncherConfig _config;

    public UpdateApiClient(LauncherConfig config)
    {
        _config = config;
    }

    public (string? provider, string downloadUrl) GetWindowsGameDownloadSource()
    {
        return _config.WindowsGameDownloadUrl == LauncherConfig.DEFAULT_DOWNLOAD_URL
            ? ("123云盘", "http://download-cdn.123pan.cn/123-241/d7fbafcc/1811863065-0/d7fbafcc1ab1b21a64e2accd92ebc4f1/c-m1?v=2&t=1682266049&s=d5ec2c9ae07214094038c45a9850479f&filename=1.14.2.42597%E7%BA%AF%E5%87%80%E4%B8%AD%E6%96%87%E7%89%88.zip")
            : (null, _config.WindowsGameDownloadUrl);
    }

    public (string? provider, string downloadUrl) GetMacGamePatchDownloadSource()
    {
        return _config.MacGamePatchDownloadUrl == LauncherConfig.DEFAULT_DOWNLOAD_URL
            ? throw new NotImplementedException("MacOs")
            : (null, _config.WindowsGameDownloadUrl);
    }

    public GitHubReleaseInfo GetLatestThisLauncherRelease()
    {
        return GetGitHubReleaseInfo(_config.GitRepoMoltenCoreLauncher);
    }

    public GitHubReleaseInfo GetLatestHermesProxyRelease()
    {
        return GetGitHubReleaseInfo(_config.GitRepoHermesProxy);
    }

    public GitHubReleaseInfo GetLatestArctiumLauncherRelease()
    {
        return GetGitHubReleaseInfo(_config.GitRepoArctiumLauncher);
    }

    private static GitHubReleaseInfo GetGitHubReleaseInfo(string repo)
    {
        var releaseUrl = $"https://api.github.com/repos/{repo}/releases/latest";
        var releaseInfo = PerformWebRequest<GitHubReleaseInfo>(releaseUrl);
        return releaseInfo;
    }

    private static TJsonResponse PerformWebRequest<TJsonResponse>(string url) where TJsonResponse : new()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "curl/7.0.0"); // otherwise we get blocked
        var response = client.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.ReasonPhrase == "rate limit exceeded")
            {
                Console.WriteLine("You are being rate-limited, did you open the launcher too many times in a short time?");
                return new TJsonResponse();
            }
        }
        response.EnsureSuccessStatusCode();
        var rawJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); // easier to debug with a string and the performance is neglectable for such small jsons
        var parsedJson = JsonSerializer.Deserialize<TJsonResponse>(rawJson);
        if (parsedJson == null)
        {
            Console.WriteLine($"Debug: {rawJson}");
            throw new NoNullAllowedException("The web response resulted in an null object");
        }
        return parsedJson;
    }
}

public class GitHubReleaseInfo
{
    [JsonPropertyName("name")] 
    public string? Name { get; set; }
    
    [JsonPropertyName("tag_name")] 
    public string? TagName { get; set; }

    [JsonPropertyName("assets")] 
    public List<Asset>? Assets { get; set; }

    public class Asset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = null!;
    }
}
