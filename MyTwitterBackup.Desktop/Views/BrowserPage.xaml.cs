using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Core.Twitter;
using MyTwitterBackup.Desktop.Services;
using MyTwitterBackup.Desktop.ViewModels;
using Serilog;
using Windows.System;

namespace MyTwitterBackup.Desktop.Views;

/// <summary>
/// Hosts the WebView2. Captures x.com GraphQL responses in two ways (deduplicated in the view model):
/// an injected script that hooks fetch/XHR (primary) and CoreWebView2.WebResourceResponseReceived (fallback).
/// </summary>
public sealed partial class BrowserPage : Page, IWebViewHost
{
    private bool _initialized;

    public BrowserPage()
    {
        ViewModel = App.GetService<BrowserViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public BrowserViewModel ViewModel { get; }

    public void Navigate(string url)
    {
        if (WebView.CoreWebView2 is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            WebView.CoreWebView2.Navigate(uri.ToString());
        }
    }

    public async Task<bool> SetAutoScrollAsync(bool enabled)
    {
        if (WebView.CoreWebView2 is null)
        {
            return false;
        }

        var action = enabled ? "start" : "stop";
        var result = await WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.__mtbAutoScroll ? (window.__mtbAutoScroll.{action}(), true) : false");
        return result == "true";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await WebView.EnsureCoreWebView2Async();
            var core = WebView.CoreWebView2;

            await core.AddScriptToExecuteOnDocumentCreatedAsync(CaptureScript.Source);
            core.WebMessageReceived += OnWebMessageReceived;
            core.WebResourceResponseReceived += OnWebResourceResponseReceived;
            core.SourceChanged += (_, _) => OnUrlChanged();
            core.HistoryChanged += (_, _) => OnUrlChanged();
            core.NewWindowRequested += OnNewWindowRequested;

            App.GetService<MediaDownloader>().UserAgent = core.Settings.UserAgent;
            ViewModel.Host = this;
            core.Navigate(BrowserViewModel.StartUrl);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 initialization failed");
            WebViewErrorBar.Message = $"WebView2 could not be started: {ex.Message} Install the Microsoft Edge WebView2 Runtime.";
            WebViewErrorBar.IsOpen = true;
        }
    }

    private void OnUrlChanged()
    {
        var core = WebView.CoreWebView2;
        ViewModel.OnUrlChanged(core.Source);
        BackButton.IsEnabled = core.CanGoBack;
        ForwardButton.IsEnabled = core.CanGoForward;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // The script is injected into every document; accept messages only from x.com / twitter.com.
        if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) || !XUrlClassifier.IsXHost(source))
        {
            return;
        }

        ViewModel.OnWebMessage(args.WebMessageAsJson);
    }

    private async void OnWebResourceResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        var url = args.Request.Uri;
        if (!GraphQlTweetParser.IsGraphQlUrl(url) || args.Response.StatusCode != 200)
        {
            return;
        }

        Log.Debug("Network response {Url}", url);

        try
        {
            using var stream = await args.Response.GetContentAsync();
            if (stream is null)
            {
                return;
            }

            using var reader = new StreamReader(stream.AsStreamForRead());
            var body = await reader.ReadToEndAsync();
            _ = Task.Run(() => ViewModel.OnGraphQlResponse(url, body));
        }
        catch (Exception ex)
        {
            // Content is not always available (e.g. cancelled requests); the injected script covers these cases.
            Log.Debug(ex, "Could not read response content for {Url}", url);
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Keep x.com links in the app browser; open external links in the default browser.
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            args.Handled = true;
            if (XUrlClassifier.IsXHost(uri))
            {
                sender.Navigate(uri.ToString());
            }
            else
            {
                _ = Launcher.LaunchUriAsync(uri);
            }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoBack();

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoForward();

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Reload();

    private void HomeButton_Click(object sender, RoutedEventArgs e) => Navigate(BrowserViewModel.StartUrl);

    private void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var text = (args.QueryText ?? sender.Text).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        Navigate(text);
    }
}
