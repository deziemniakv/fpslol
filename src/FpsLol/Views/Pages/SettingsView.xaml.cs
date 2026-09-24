using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace FpsLol.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Keeps the activity log scrolled to the newest entry.</summary>
    private void OnLogsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox list || list.ItemsSource is not INotifyCollectionChanged source) return;

        void ScrollToEnd()
        {
            if (list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]);
        }

        NotifyCollectionChangedEventHandler handler = (_, _) => ScrollToEnd();
        source.CollectionChanged += handler;
        list.Unloaded += (_, _) => source.CollectionChanged -= handler;
        ScrollToEnd();
    }
}
