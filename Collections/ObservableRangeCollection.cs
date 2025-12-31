using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace UltimateVideoBrowser.Collections;

/// <summary>
/// ObservableCollection with efficient bulk operations (AddRange/ReplaceRange).
/// This avoids per-item CollectionChanged notifications, which greatly improves UI performance.
/// </summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private bool suppressNotifications;

    /// <summary>
    /// Removes a range of items and raises a single CollectionChanged event.
    /// If the items are not contiguous, a Reset notification is raised for correctness.
    /// </summary>
    public void RemoveRange(IReadOnlyList<T> items)
    {
        if (items == null || items.Count == 0)
            return;

        // NotifyCollectionChangedEventArgs requires an IList; make sure we have one.
        IList oldItems = items as IList ?? items.ToList();

        // Best effort: try to remove as a contiguous block to keep UI updates minimal.
        var startIndex = IndexOf(items[0]);
        var contiguous = startIndex >= 0;
        if (contiguous)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var idx = startIndex + i;
                if (idx >= Count || !Equals(this[idx], items[i]))
                {
                    contiguous = false;
                    break;
                }
            }
        }

        try
        {
            suppressNotifications = true;

            if (contiguous)
            {
                for (var i = 0; i < items.Count; i++)
                    Items.RemoveAt(startIndex);
            }
            else
            {
                // Fallback: remove by value (order may differ). This is O(n*m) but used rarely.
                foreach (var item in items)
                    Items.Remove(item);
            }
        }
        finally
        {
            suppressNotifications = false;
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));

        if (contiguous)
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItems, startIndex));
        else
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds a range of items and raises a single CollectionChanged event.
    /// </summary>
    public void AddRange(IReadOnlyList<T> items)
    {
        if (items == null || items.Count == 0)
            return;

        // NotifyCollectionChangedEventArgs requires an IList; make sure we have one.
        IList newItems = items as IList ?? items.ToList();

        var startIndex = Count;
        try
        {
            suppressNotifications = true;
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            suppressNotifications = false;
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems, startIndex));
    }

    /// <summary>
    /// Replaces the entire collection contents. Raises a single Reset notification.
    /// </summary>
    public void ReplaceRange(IReadOnlyList<T> items)
    {
        try
        {
            suppressNotifications = true;
            Items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                    Items.Add(item);
            }
        }
        finally
        {
            suppressNotifications = false;
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (suppressNotifications)
            return;
        base.OnCollectionChanged(e);
    }
}
