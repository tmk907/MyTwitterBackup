using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyTwitterBackup.Desktop.ViewModels;

namespace MyTwitterBackup.Desktop.Helpers;

public sealed partial class MediaTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PhotoTemplate { get; set; }

    public DataTemplate? VideoTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is MediaItemViewModel { IsVideo: true } ? VideoTemplate : PhotoTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
