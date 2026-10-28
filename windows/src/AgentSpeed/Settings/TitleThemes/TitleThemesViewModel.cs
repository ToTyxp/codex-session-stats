using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Services;
using AgentSpeed.Titles;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>An item of the custom cards grid: a custom set or the New card.</summary>
internal abstract class TitleCardItem : ObservableObject
{
}

/// <summary>A theme card (DESIGN 5, Title themes 2): name over the preview strip, selected state and its menu.</summary>
internal sealed class TitleThemeCardViewModel(string id, bool isCustom) : TitleCardItem
{
    private string _name = string.Empty;
    private IReadOnlyList<string> _stripTitles = [];
    private IReadOnlyList<Brush> _stripBrushes = [];
    private string _tooltip = string.Empty;
    private string _accessibleName = string.Empty;
    private bool _selected;

    public string Id { get; } = id;

    public bool IsCustom { get; } = isCustom;

    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Levels 1, 5, 9 and 13 as typed or localized.</summary>
    public IReadOnlyList<string> StripTitles
    {
        get => _stripTitles;
        set
        {
            if (!_stripTitles.SequenceEqual(value, StringComparer.Ordinal))
            {
                _stripTitles = value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<Brush> StripBrushes
    {
        get => _stripBrushes;
        set
        {
            if (!_stripBrushes.SequenceEqual(value))
            {
                _stripBrushes = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The whole ladder with the thresholds at the current redline.</summary>
    public string Tooltip { get => _tooltip; set => Set(ref _tooltip, value); }

    public string AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    /// <summary>Added after the grid was shown: the card appears with the spring.</summary>
    public bool Appear { get; set; }
}

/// <summary>The last, dashed card: a copy of the selected theme; disabled at eight custom sets.</summary>
internal sealed class NewTitleCardViewModel : TitleCardItem
{
    private string _text = string.Empty;
    private bool _enabled = true;
    private string? _tooltip;

    public string Text { get => _text; set => Set(ref _text, value); }

    public bool IsEnabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary><c>titles.custom.limit</c> while disabled.</summary>
    public string? Tooltip { get => _tooltip; set => Set(ref _tooltip, value); }
}

/// <summary>
/// Settings → General, Title themes (DESIGN 5): the theme cards, the undo banner and the editor of the selected custom
/// set. Every change goes through <see cref="TitleThemesService"/> and applies live everywhere.
/// </summary>
internal sealed class TitleThemesViewModel : ObservableObject
{
    public const string NewCardId = "titles.custom.new";

    private readonly SettingsService _settings;
    private readonly TitleThemesService _titles;
    private Localizer _loc;
    private bool _dark = true;
    private bool _reduceMotion;
    private bool _shown;
    private bool _hasEditor;

    public TitleThemesViewModel(SettingsService settings, TitleThemesService titles, Localizer loc)
    {
        _settings = settings;
        _titles = titles;
        _loc = loc;
        foreach (var preset in TitlePresets.All)
        {
            PresetCards.Add(new TitleThemeCardViewModel(TitlePresets.Id(preset), false));
        }

        CustomCards.Add(NewCard);
        Editor = new TitleSetEditorViewModel(settings, titles, loc, this);
        SelectCommand = new RelayCommand(p =>
        {
            if (p is TitleThemeCardViewModel card)
            {
                Select(card.Id);
            }
        });
        NewCommand = new RelayCommand(() => NewSet(), () => _titles.Document.CanAddCustom);
        UndoCommand = new RelayCommand(() => _titles.UndoLast(), () => _titles.Undo is not null);

        _titles.Changed += (_, _) => Refresh();
        _titles.UndoChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasUndo));
            OnPropertyChanged(nameof(UndoText));
            CommandManager.InvalidateRequerySuggested();
        };
        _settings.Changed += (_, e) =>
        {
            if (e.Previous.Redline != e.Current.Redline || e.Previous.Metric != e.Current.Metric)
            {
                Refresh();
            }
            else if (!ReferenceEquals(e.Previous.Orbs, e.Current.Orbs))
            {
                Editor.OnLookChanged();
            }
        };
        Refresh();
        _shown = true;
    }

    /// <summary>A card asked for the editor's name field (a new copy, or Edit in a card's menu).</summary>
    public event EventHandler? NameFocusRequested;

    public ObservableCollection<TitleThemeCardViewModel> PresetCards { get; } = [];

    /// <summary>The custom sets in creation order, then <see cref="NewCard"/>.</summary>
    public ObservableCollection<TitleCardItem> CustomCards { get; } = [];

    public NewTitleCardViewModel NewCard { get; } = new();

    public TitleSetEditorViewModel Editor { get; }

    public ICommand SelectCommand { get; }

    public ICommand NewCommand { get; }

    public ICommand UndoCommand { get; }

    public bool HasUndo => _titles.Undo is not null;

    public string UndoText => _titles.UndoMessage;

    /// <summary>The selected (General) theme is a custom set: its editor shows.</summary>
    public bool HasEditor => _hasEditor;

    public string CustomHeader => _loc["titles.custom"];

    /// <summary>The style of the orb selected in Settings → 懸浮窗 (the floating orb preview); set by the settings model.</summary>
    public Func<OrbStyle>? SelectedOrbStyle { get; set; }

    public bool DarkTheme
    {
        get => _dark;
        set
        {
            if (Set(ref _dark, value))
            {
                Refresh();
                Editor.OnLookChanged();
            }
        }
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (Set(ref _reduceMotion, value))
            {
                Editor.OnLookChanged();
            }
        }
    }

    public Localizer Localizer => _loc;

    internal TitleThemesService Service => _titles;

    internal double Redline => _settings.Current.Redline;

    /// <summary>Every card that can take the focus, in keyboard order: presets, custom sets, then New while it is enabled.</summary>
    public IReadOnlyList<string> FocusOrder =>
    [
        .. PresetCards.Select(card => card.Id),
        .. CustomCards.OfType<TitleThemeCardViewModel>().Select(card => card.Id),
        .. NewCard.IsEnabled ? [NewCardId] : Array.Empty<string>(),
    ];

    public bool IsCustom(string id) => _titles.Document.CustomSet(id) is not null;

    public bool CanAddCustom => _titles.Document.CanAddCustom;

    public void Select(string id) => _titles.Select(id);

    /// <summary>Copies a theme into a new selected set and opens its editor with the name field focused.</summary>
    public string? Duplicate(string id)
    {
        if (_titles.Copy(id) is not { } newId)
        {
            return null;
        }

        NameFocusRequested?.Invoke(this, EventArgs.Empty);
        return newId;
    }

    public void Edit(string id)
    {
        _titles.Select(id);
        NameFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id) => _titles.Delete(id);

    /// <summary>The New card: a copy of the selected theme.</summary>
    public string? NewSet() => Duplicate(_titles.Document.General.Id);

    public void Relocalize(Localizer loc)
    {
        _loc = loc;
        Editor.Relocalize(loc);
        Refresh();
        OnPropertyChanged(nameof(CustomHeader));
        OnPropertyChanged(nameof(UndoText));
    }

    /// <summary>Rebuilds every card and the editor from the stored themes (with a draft applied) and the redline.</summary>
    public void Refresh()
    {
        var document = _titles.Document;
        var selected = document.General.Id;
        var redline = _settings.Current.Redline;
        var brushes = TitleCardStrip.Levels.Select(level => (Brush)TitleHeatBrushes.ForLevel((int)level, redline, _dark)).ToArray();
        foreach (var card in PresetCards)
        {
            Fill(card, _titles.Theme(card.Id), selected, redline, brushes);
        }

        var ids = document.Custom.Select(set => set.Id).ToList();
        for (var i = CustomCards.Count - 1; i >= 0; i--)
        {
            if (CustomCards[i] is TitleThemeCardViewModel card && !ids.Contains(card.Id))
            {
                CustomCards.RemoveAt(i);
            }
        }

        for (var i = 0; i < ids.Count; i++)
        {
            var card = CustomCards.OfType<TitleThemeCardViewModel>().FirstOrDefault(c => c.Id == ids[i]);
            if (card is null)
            {
                card = new TitleThemeCardViewModel(ids[i], true) { Appear = _shown };
                CustomCards.Insert(i, card);
            }
            else if (CustomCards.IndexOf(card) != i)
            {
                CustomCards.Move(CustomCards.IndexOf(card), i);
            }

            Fill(card, _titles.Theme(card.Id), selected, redline, brushes);
        }

        NewCard.Text = _loc["titles.custom.new"];
        NewCard.IsEnabled = document.CanAddCustom;
        NewCard.Tooltip = document.CanAddCustom ? null : _loc.Format("titles.custom.limit", TitleThemesDocument.MaxCustom);

        Editor.Show(document.General.Custom is not null ? document.General.Id : null);
        if (_hasEditor != Editor.SetId is not null)
        {
            _hasEditor = Editor.SetId is not null;
            OnPropertyChanged(nameof(HasEditor));
        }

        OnPropertyChanged(nameof(CanAddCustom));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Fill(TitleThemeCardViewModel card, TitleTheme? theme, string selected, double redline, IReadOnlyList<Brush> brushes)
    {
        if (theme is null)
        {
            return;
        }

        var name = _titles.Name(theme);
        var strip = TitleSettingsText.StripTitles(theme, _loc);
        card.Name = name;
        card.StripTitles = strip;
        card.StripBrushes = brushes;
        card.Tooltip = TitleSettingsText.Ladder(theme, redline, _loc);
        card.AccessibleName = TitleSettingsText.CardAccessibleName(name, strip, _loc);
        card.IsSelected = card.Id == selected;
    }
}
