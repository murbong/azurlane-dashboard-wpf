using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AzurLaneDashboard.App.ViewModels;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items) => AddRangeAndTrimStart(items, int.MaxValue);

    public void RemoveFromStart(int count)
    {
        var removeCount = Math.Clamp(count, 0, Count);
        if (removeCount == 0) return;
        CheckReentrancy();
        RemoveFirstItems(removeCount);
        NotifyReset();
    }

    public void AddRangeAndTrimStart(IEnumerable<T> items, int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        // Enumerate before mutation: self-input and failing enumerators are safe.
        var incoming = items.ToArray();
        if (incoming.Length == 0 && Count <= maximumCount) return;
        CheckReentrancy();
        foreach (var item in incoming) Items.Add(item);
        RemoveFirstItems(Math.Max(0, Count - maximumCount));
        NotifyReset();
    }

    public void ReplaceRange(IEnumerable<T> items)
    {
        var replacement = items.ToArray();
        if (this.SequenceEqual(replacement)) return;
        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement) Items.Add(item);
        NotifyReset();
    }

    private void RemoveFirstItems(int count)
    {
        if (Items is List<T> list) list.RemoveRange(0, count);
        else
            for (var index = count - 1; index >= 0; index--) Items.RemoveAt(index);
    }

    private void NotifyReset()
    {
        // Direct Items mutations do not raise per-item ObservableCollection events.
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
