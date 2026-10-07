using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Updates.UpdateService
{
    /// <summary>
    /// Checks GitHub Releases for a newer version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two constraints shape this class. The brief requires core features to work
    /// offline, so a failed check is a message and never an error state - nothing
    /// about groups, workspaces or icons depends on this. And no paid API is
    /// involved: the public GitHub Releases endpoint needs no key.
    /// </para>
    /// <para>
    /// Version comparison is done by parsed components rather than by string
    /// ordering, because "10.0" sorts before "9.0" alphabetically and that mistake
    /// would tell every user on 9.x that they were up to date.
    /// </para>
    /// </remarks>
    public sealed class UpdateService : IUpdateService, IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly string _feedUrl;
        private readonly AppSettings _settings;
        private readonly IAppLogger? _logger;
        private readonly HttpClient _http;
        private bool _ownsHttpClient;
        private bool _disposed;

        public UpdateService(AppSettings settings, IAppLogger? logger = null, HttpClient? httpClient = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;

            _feedUrl = string.IsNullOrWhiteSpace(settings.UpdateFeedUrl)
                ? "https://api.github.com/repos/tjackenpacken/taskbar-groups/releases/latest"
                : settings.UpdateFeedUrl;

            if (httpClient != null)
            {
                _http = httpClient;
            }
            else
            {
                _http = new HttpClient { Timeout = Timeout };
                _ownsHttpClient = true;

                // GitHub rejects requests without a user agent.
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("taskbar-groups");
                _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            }
        }

        public string CurrentVersion
        {
            get
            {
                try
                {
                    System.Reflection.AssemblyName? name = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
                    return name?.Version?.ToString() ?? "0.0.0";
                }
                catch (Exception)
                {
                    return "0.0.0";
                }
            }
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            var result = new UpdateCheckResult { CurrentVersion = CurrentVersion };

            if (_settings.OfflineMode)
            {
                result.CheckSucceeded = true;
                result.UpdateAvailable = false;
                result.Message = "Offline mode is on, so no update check was made.";
                return result;
            }

            if (_disposed)
            {
                result.Message = "The updater has been disposed.";
                return result;
            }

            try
            {
                using HttpResponseMessage response = await _http
                    .GetAsync(_feedUrl, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    result.Message = "The update server answered " + (int)response.StatusCode + ".";
                    _logger?.Log(SystemLogLevel.Debug, "Updater", result.Message);
                    return result;
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;

                string latest = ReadString(root, "tag_name") ?? ReadString(root, "name") ?? string.Empty;
                result.LatestVersion = Normalize(latest);
                result.ReleaseUrl = ReadString(root, "html_url") ?? string.Empty;

                result.CheckSucceeded = true;
                result.UpdateAvailable = IsNewer(result.LatestVersion, result.CurrentVersion);
                result.Message = result.UpdateAvailable
                    ? "Version " + result.LatestVersion + " is available."
                    : "You are up to date.";

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Message = "The update check was cancelled.";
                return result;
            }
            catch (HttpRequestException ex)
            {
                // Offline, blocked by a firewall, or the endpoint is unreachable.
                // None of that is a problem for the user right now.
                result.Message = "Could not reach the update server: " + ex.Message;
                _logger?.Log(SystemLogLevel.Debug, "Updater", result.Message);
                return result;
            }
            catch (JsonException ex)
            {
                result.Message = "The update server returned an unexpected response: " + ex.Message;
                _logger?.Log(SystemLogLevel.Debug, "Updater", result.Message);
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "The update check failed: " + ex.Message;
                _logger?.Log(SystemLogLevel.Warning, "Updater", result.Message, ex);
                return result;
            }
        }

        private static string? ReadString(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(property, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        internal static string Normalize(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;

            string value = tag!.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);

            int suffix = value.IndexOfAny(new[] { '-', '+' });
            if (suffix > 0) value = value.Substring(0, suffix);

            return value;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is strictly newer than
        /// <paramref name="current"/>, comparing numeric components.
        /// </summary>
        internal static bool IsNewer(string? candidate, string? current)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (string.IsNullOrWhiteSpace(current)) return true;

            int[] left = ParseComponents(candidate!);
            int[] right = ParseComponents(current!);

            int length = Math.Max(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int a = index < left.Length ? left[index] : 0;
                int b = index < right.Length ? right[index] : 0;

                if (a > b) return true;
                if (a < b) return false;
            }

            return false;
        }

        internal static int[] ParseComponents(string version)
        {
            string normalized = Normalize(version);
            if (string.IsNullOrEmpty(normalized)) return Array.Empty<int>();

            string[] parts = normalized.Split('.');
            var components = new int[parts.Length];

            for (int index = 0; index < parts.Length; index++)
            {
                components[index] = int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : 0;
            }

            return components;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ownsHttpClient) _http.Dispose();
        }
    }
}