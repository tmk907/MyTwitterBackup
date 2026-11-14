using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using MyTwitterBackup.Desktop.Services;
using MyTwitterBackup.Desktop.Views;
using Windows.Graphics;

namespace MyTwitterBackup.Desktop;

public sealed partial class MainWindow : Window, IRecipient<ResolveMissingDataMessage>
{
    private const int WidthDip = 1280;
    private const int HeightDip = 840;

    private static readonly Dictionary<string, Type> s_pages = new()
    {
        ["Browser"] = typeof(BrowserPage),
        ["Queue"] = typeof(QueuePage),
        ["Downloaded"] = typeof(DownloadedPage),
        ["Settings"] = typeof(SettingsPage),
    };

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));

        if (App.StartupError is { } error)
        {
            StartupErrorBar.Message = error;
            StartupErrorBar.IsOpen = true;
        }

        WeakReferenceMessenger.Default.Register(this);
        NavView.SelectedItem = BrowserNavItem;
    }

    public void Receive(ResolveMissingDataMessage message)
    {
        // The fallback for missing post data runs in the browser.
        NavView.SelectedItem = BrowserNavItem;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "Settings" : args.SelectedItemContainer?.Tag as string;
        if (tag is null || !s_pages.TryGetValue(tag, out var pageType) || ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        // Pages are not navigated "back"; the browser page instance is kept by NavigationCacheMode.
        ContentFrame.BackStack.Clear();
    }
}
