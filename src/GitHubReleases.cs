using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExtraLifeLauncher
{
    class ReleaseInfo
    {
        public string Tag;
        public string Name;
        public string Notes;
        public DateTime PublishedAt;
        public bool Prerelease;
        public string AssetName;
        public string AssetUrl;
        public long AssetSize;
        public string Sha256; // lowercase hex, or null when GitHub didn't supply a digest
    }

    struct DownloadProgress
    {
        public long Received;
        public long Total;
        public double BytesPerSecond;
    }

    /// <summary>A failure with a message that's fine to show the player as-is.</summary>
    class LauncherException : Exception
    {
        public LauncherException(string message) : base(message) { }
    }

    /// <summary>Reads the public releases repo through the anonymous GitHub REST API (60 requests/hour per IP).</summary>
    static class GitHubReleases
    {
        static readonly HttpClient Http;

        static GitHubReleases()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("ExtraLifeLauncher/" + LauncherUpdater.CurrentVersion);
        }

        /// <summary>Newest build of <paramref name="game"/>.</summary>
        public static Task<ReleaseInfo> GetLatestGameAsync(GameInfo game)
        {
            return GetLatestAsync(game.RepoOwner, game.RepoName, game.AssetName, game.IncludePrereleases, ".zip");
        }

        /// <summary>Newest launcher build (full releases only).</summary>
        public static Task<ReleaseInfo> GetLatestLauncherAsync()
        {
            return GetLatestAsync(LauncherConfig.LauncherRepoOwner, LauncherConfig.LauncherRepoName,
                LauncherConfig.LauncherAssetName, false, null);
        }

        /// <param name="fallbackExtension">Accept any asset with this extension when none is named <paramref name="assetName"/>; null for exact name only.</param>
        static async Task<ReleaseInfo> GetLatestAsync(string owner, string repo, string assetName,
            bool includePrereleases, string fallbackExtension)
        {
            var url = string.Format("https://api.github.com/repos/{0}/{1}/releases?per_page=20", owner, repo);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(request, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    throw new LauncherException("The update server took too long to respond.");
                }
                catch (HttpRequestException)
                {
                    throw new LauncherException("Couldn't reach the update server. Check your internet connection.");
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new LauncherException("The release repository wasn't found (is it public?).");
                    if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                        throw new LauncherException("GitHub is rate-limiting update checks. Try again in a few minutes.");
                    if (!response.IsSuccessStatusCode)
                        throw new LauncherException("Update server error (" + (int)response.StatusCode + ").");

                    var json = await response.Content.ReadAsStringAsync();
                    var releases = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[];
                    if (releases == null) return null;

                    // The API lists newest first.
                    foreach (var item in releases)
                    {
                        var release = item as Dictionary<string, object>;
                        if (release == null || GetBool(release, "draft")) continue;
                        var prerelease = GetBool(release, "prerelease");
                        if (prerelease && !includePrereleases) continue;

                        var asset = PickAsset(release, assetName, fallbackExtension);
                        if (asset == null) continue;

                        var digest = GetString(asset, "digest");
                        DateTime published;
                        DateTime.TryParse(GetString(release, "published_at"), out published);

                        return new ReleaseInfo
                        {
                            Tag = GetString(release, "tag_name"),
                            Name = GetString(release, "name"),
                            Notes = GetString(release, "body"),
                            PublishedAt = published,
                            Prerelease = prerelease,
                            AssetName = GetString(asset, "name"),
                            AssetUrl = GetString(asset, "browser_download_url"),
                            AssetSize = asset.ContainsKey("size") ? Convert.ToInt64(asset["size"]) : 0,
                            Sha256 = digest != null && digest.StartsWith("sha256:") ? digest.Substring(7).ToLowerInvariant() : null,
                        };
                    }
                    return null;
                }
            }
        }

        static Dictionary<string, object> PickAsset(Dictionary<string, object> release, string assetName, string fallbackExtension)
        {
            var assets = release.ContainsKey("assets") ? release["assets"] as object[] : null;
            if (assets == null) return null;

            Dictionary<string, object> fallback = null;
            foreach (var item in assets)
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                var name = GetString(asset, "name") ?? "";
                if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) return asset;
                if (fallback == null && fallbackExtension != null && name.EndsWith(fallbackExtension, StringComparison.OrdinalIgnoreCase))
                    fallback = asset;
            }
            return fallback;
        }

        /// <summary>Streams the release zip to <paramref name="destination"/>, verifying its SHA-256 when GitHub provides one.</summary>
        public static async Task DownloadAsync(ReleaseInfo release, string destination,
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new LauncherException("Download failed (" + (int)response.StatusCode + ").");

                var total = response.Content.Headers.ContentLength ?? release.AssetSize;
                var buffer = new byte[81920];
                long received = 0, lastReportBytes = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                double speed = 0;

                using (var sha = SHA256.Create())
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, ct);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        received += read;

                        var elapsed = clock.Elapsed - lastReport;
                        if (elapsed.TotalMilliseconds >= 250)
                        {
                            var instant = (received - lastReportBytes) / elapsed.TotalSeconds;
                            speed = speed <= 0 ? instant : speed * 0.7 + instant * 0.3;
                            lastReport = clock.Elapsed;
                            lastReportBytes = received;
                            progress.Report(new DownloadProgress { Received = received, Total = total, BytesPerSecond = speed });
                        }
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    progress.Report(new DownloadProgress { Received = received, Total = total, BytesPerSecond = speed });

                    if (total > 0 && received != total)
                        throw new LauncherException("The download was interrupted. Please try again.");

                    if (release.Sha256 != null)
                    {
                        var actual = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                        if (actual != release.Sha256)
                            throw new LauncherException("The download was corrupted (checksum mismatch). Please try again.");
                    }
                }
            }
        }

        static string GetString(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) ? value as string : null;
        }

        static bool GetBool(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) && value is bool && (bool)value;
        }
    }
}
