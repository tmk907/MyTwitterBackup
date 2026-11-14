using Microsoft.UI.Xaml;

namespace MyTwitterBackup.Desktop.Helpers;

/// <summary>Static functions for x:Bind (used instead of IValueConverter).</summary>
public static class XamlHelpers
{
    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NotNullToVisibility(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NullToVisibility(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StringToVisibility(string? value) =>
        string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility CountToVisibility(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ZeroToVisibility(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static bool Not(bool value) => !value;

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static Microsoft.UI.Xaml.Controls.InfoBarSeverity ErrorSeverity(bool isError) =>
        isError ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
}
