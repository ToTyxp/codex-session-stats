using System.ComponentModel;
using System.Windows.Data;
using AgentSpeed.Controls;
using AgentSpeed.Presentation.Cost;

namespace AgentSpeed.Settings.Cost;

/// <summary>
/// The currency flyout (DESIGN 9, Currency): the query, the grouped or ranked rows and the highlighted row that
/// ↑ / ↓ move and Return selects. The rows are rebuilt only while the flyout is open.
/// </summary>
internal sealed class CurrencyPickerViewModel : ObservableObject
{
    private readonly CostDisplay _display;
    private readonly Action<string> _select;
    private string _query = string.Empty;
    private IReadOnlyList<CurrencyPickerRow> _rows = [];
    private ICollectionView _view = new ListCollectionView(new List<CurrencyPickerRow>());
    private CurrencyPickerRow? _highlighted;
    private bool _isOpen;

    public CurrencyPickerViewModel(CostDisplay display, Action<string> select)
    {
        _display = display;
        _select = select;
        display.Changed += (_, _) =>
        {
            if (_isOpen)
            {
                var code = _highlighted?.Code;
                Rebuild();
                Highlighted = _rows.FirstOrDefault(row => row.Code == code) ?? FirstRow;
            }
        };
    }

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasQuery));
                Rebuild();
                Highlighted = FirstRow;
            }
        }
    }

    public bool HasQuery => _query.Length > 0;

    /// <summary>Grouped by <see cref="CurrencyPickerRow.Group"/> unless searching.</summary>
    public ICollectionView Rows { get => _view; private set => Set(ref _view, value); }

    public bool IsEmpty => _rows.Count == 0;

    public CurrencyPickerRow? Highlighted { get => _highlighted; set => Set(ref _highlighted, value); }

    private CurrencyPickerRow? FirstRow => _rows.Count > 0 ? _rows[0] : null;

    /// <summary>Starts with an empty query and the current currency highlighted.</summary>
    public void Open()
    {
        _isOpen = true;
        _query = string.Empty;
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(HasQuery));
        Rebuild();
        var index = CurrencyPickerList.InitialIndex(_rows);
        Highlighted = index >= 0 ? _rows[index] : null;
    }

    public void Close() => _isOpen = false;

    public void Move(int step)
    {
        var index = _highlighted is { } row ? IndexOf(row.Code) : -1;
        var next = CurrencyPickerList.Move(index, step, _rows.Count);
        Highlighted = next >= 0 ? _rows[next] : null;
    }

    /// <summary>Selects <paramref name="row"/> (else the highlighted or first row); false when there is none.</summary>
    public bool Commit(CurrencyPickerRow? row = null)
    {
        var chosen = row ?? _highlighted ?? FirstRow;
        if (chosen is null)
        {
            return false;
        }

        _select(chosen.Code);
        return true;
    }

    private int IndexOf(string code)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Code == code)
            {
                return i;
            }
        }

        return -1;
    }

    private void Rebuild()
    {
        var wasEmpty = IsEmpty;
        _rows = CurrencyPickerList.Rows(_display, _query);
        var view = new ListCollectionView(_rows.ToList());
        if (!CurrencyPickerList.IsSearching(_query))
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CurrencyPickerRow.Group)));
        }

        Rows = view;
        if (IsEmpty != wasEmpty)
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
