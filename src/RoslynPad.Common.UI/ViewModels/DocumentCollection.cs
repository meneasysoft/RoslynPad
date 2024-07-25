using System.Collections.ObjectModel;

namespace RoslynPad.UI;

/// <summary>
/// A keyed observable collection for <see cref="DocumentViewModel"/>.
/// </summary>
internal sealed class DocumentCollection : ObservableCollection<DocumentViewModel>
{
    private readonly Dictionary<string, DocumentViewModel> _dictionary;

    public DocumentCollection(IEnumerable<DocumentViewModel> items)
    {
        _dictionary = [];

        foreach (var item in items)
        {
            Add(item);
        }
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        _dictionary.Clear();
    }

    protected override void InsertItem(int index, DocumentViewModel item)
    {
        base.InsertItem(index, item);
        _dictionary.Add(item.Name, item);
    }

    protected override void RemoveItem(int index)
    {
        var item = Items[index];
        base.RemoveItem(index);
        _dictionary.Remove(item.Name);
    }

    protected override void SetItem(int index, DocumentViewModel item)
    {
        var existingItem = Items[index];
        base.SetItem(index, existingItem);
        _dictionary.Remove(existingItem.Name);
        _dictionary.Add(item.Name, item);
    }

    public DocumentViewModel? this[string name]
    {
        get
        {
            _dictionary.TryGetValue(name, out var item);
            return item;
        }
    }
}