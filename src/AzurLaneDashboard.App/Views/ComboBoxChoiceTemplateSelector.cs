using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AzurLaneDashboard.App.Views;

/// <summary>
/// A combo box shows its selection with the item template too; this lets the closed box stay short
/// (a name) while the open list carries the comparison columns.
/// </summary>
public sealed class ComboBoxChoiceTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SelectedTemplate { get; set; }
    public DataTemplate? DropDownTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        for (var parent = container; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ComboBoxItem) return DropDownTemplate;
            if (parent is ComboBox) return SelectedTemplate;
        }

        return SelectedTemplate;
    }
}
