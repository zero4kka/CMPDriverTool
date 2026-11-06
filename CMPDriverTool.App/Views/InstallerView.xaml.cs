using System.Collections.Specialized;
using System.Windows.Controls;

namespace CMPDriverTool.App.Views;

public partial class InstallerView : UserControl
{
    public InstallerView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)LogListBox.Items).CollectionChanged += LogListBox_CollectionChanged;
    }

    private void LogListBox_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }
}
