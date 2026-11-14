using System.Net;
using Serilog;

namespace MyTwitterBackup.Core.Services;

/// <summary>Downloads media files from pbs.twimg.com / video.twimg.com.</summary>
public sealed class MediaDownloader : IDisposable
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _http;

    public MediaDownloader() : this(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
    }

    public MediaDownloader(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>User agent sent with requests. The app sets it to the WebView2 user agent.</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    /// <summary>Downloads the file unless it already exists. Returns true when a file was downloaded.</summary>
    public async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(destinationPath);
        if (info.Exists && info.Length > 0)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tempPath = destinationPath + ".part";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.Referrer = new Uri("https://x.com/");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var target = File.Create(tempPath))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                File.Move(tempPath, destinationPath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (attempt < MaxAttempts
                                       && (ex is HttpRequestException or IOException or TaskCanceledException)
                                       && !cancellationToken.IsCancellationRequested
                                       && !IsPermanent(ex))
            {
                Log.Warning(ex, "Download attempt {Attempt} failed for {Url}", attempt, url);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
    }

    private static bool IsPermanent(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    public void Dispose() => _http.Dispose();
}
