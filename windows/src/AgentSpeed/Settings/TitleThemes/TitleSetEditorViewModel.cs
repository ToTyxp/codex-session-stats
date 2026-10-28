using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Controls;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Services;
using AgentSpeed.Titles;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>One row of the title table: level, threshold, the field and its reset button (DESIGN 5, Title themes 4).</summary>
internal sealed class TitleFieldRowViewModel(TitleSetEditorViewModel editor, int level) : ObservableObject
{
    private string _threshold = string.Empty;
    private Brush _thresholdBrush = Brushes.Gray;
    private string _committed = string.Empty;
    private string _placeholder = string.Empty;
    private string _accessibleName = string.Empty;
    private string _resetName = string.Empty;
    private bool _focused;

    /// <summary>0 (idle) or 1–13.</summary>
    public int Level { get; } = level;

    public bool IsIdle => Level == 0;

    public string LevelText => Level == 0 ? string.Empty : Level.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The threshold at the current redline and metric; the idle row's <c>titles.custom.idleRow</c>.</summary>
    public string Threshold { get => _threshold; set => Set(ref _threshold, value); }

    public Brush ThresholdBrush { get => _thresholdBrush; set => Set(ref _thresholdBrush, value); }

    /// <summary>The stored text; empty follows the base.</summary>
    public string Committed { get => _committed; set => Set(ref _committed, value); }

    /// <summary>The base's title in the current language.</summary>
    public string Placeholder { get => _placeholder; set => Set(ref _placeholder, value); }

    public string AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }

    /// <summary><c>titles.custom.reset</c> with the base title.</summary>
    public string ResetName { get => _resetName; set => Set(ref _resetName, value); }

    /// <summary>The field has the focus: the row gets the accent wash.</summary>
    public bool IsFocused { get => _focused; set => Set(ref _focused, value); }

    public void Focused() => editor.OnRowFocused(this);

    public void Blurred() => editor.OnRowBlurred(this);

    public void Draft(string text) => editor.Draft(Level, text);

    /// <summary>Commits the text and returns what was stored (after the text rules and the length budget).</summary>
    public string Commit(string text) => editor.Commit(Level, text);

    public void Move(int delta) => editor.RequestRowFocus(Level + delta);
}

/// <summary>
/// The editor of the selected custom set (DESIGN 5, Title themes 4): name, base, the 14 fields with live thresholds,
/// the notices, the footer actions and what the live previews show.
/// </summary>
internal sealed class TitleSetEditorViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeTime = TimeSpan.FromSeconds(4);

    private readonly SettingsService _settings;
    private readonly TitleThemesService _titles;
    private readonly TitleThemesViewModel _owner;
    private readonly DispatcherTimer _noticeTimer;
    private Localizer _loc;
    private string? _setId;
    private string _title = string.Empty;
    private string _name = string.Empty;
    private int _baseIndex;
    private string _baseHint = string.Empty;
    private bool _showsVehicleArt;
    private string _speedHeader = string.Empty;
    private string? _duplicateTooltip;
    private string? _notice;
    private int? _focusedLevel;
    private IReadOnlyList<string> _previewLabels = [];
    private bool _previewShowsVehicle;
    private OrbStyle _previewStyle = OrbStyle.Led;
    private string _previewName = string.Empty;
    private bool _syncing;

    public TitleSetEditorViewModel(SettingsService settings, TitleThemesService titles, Localizer loc, TitleThemesViewModel owner)
    {
        _settings = settings;
        _titles = titles;
        _loc = loc;
        _owner = owner;
        for (var level = 0; level <= TitlePresets.LevelCount; level++)
        {
            Rows.Add(new TitleFieldRowViewModel(this, level));
        }

        _noticeTimer = new DispatcherTimer(NoticeTime, DispatcherPriority.Background, (_, _) =>
        {
            _noticeTimer!.Stop();
            Notice = null;
        }, Dispatcher.CurrentDispatcher);
        _noticeTimer.Stop();
        ResetAllCommand = new RelayCommand(() =>
        {
            if (_setId is { } id)
            {
                _titles.ResetAll(id);
            }
        }, () => CurrentSet is { IsEmpty: false });
        DuplicateCommand = new RelayCommand(() =>
        {
            if (_setId is { } id)
            {
                _owner.Duplicate(id);
            }
        }, () => _titles.Document.CanAddCustom);
        DeleteCommand = new RelayCommand(() =>
        {
            if (_setId is { } id)
            {
                _titles.Delete(id);
            }
        });
        Relocalize(loc);
    }

    /// <summary>↑ / ↓ / Return asked for the field of this level.</summary>
    public event EventHandler<int>? RowFocusRequested;

    /// <summary>A notice appeared: the view announces it politely.</summary>
    public event EventHandler<string>? NoticeShown;

    public ObservableCollection<TitleFieldRowViewModel> Rows { get; } = [];

    public ICommand ResetAllCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>The custom set being edited; null when the General theme is a preset.</summary>
    public string? SetId => _setId;

    /// <summary><c>titles.custom.editing</c> with the set's name.</summary>
    public string Title { get => _title; private set => Set(ref _title, value); }

    /// <summary>The stored name (the name field shows it until the user edits).</summary>
    public string NameText { get => _name; private set => Set(ref _name, value); }

    /// <summary>The base preset in picker order.</summary>
    public int BaseIndex
    {
        get => _baseIndex;
        set
        {
            if (_syncing || value < 0 || value >= TitlePresets.All.Count || _setId is not { } id)
            {
                return;
            }

            _baseIndex = value;
            _titles.SetBase(id, TitlePresets.All[value]);
        }
    }

    /// <summary>
    /// The nine presets for the base picker. The items stay the same objects and only their names follow the language,
    /// so the picker keeps its selection when the language changes.
    /// </summary>
    public IReadOnlyList<TitleBaseOption> BaseOptions { get; } = TitlePresets.All.Select(_ => new TitleBaseOption()).ToArray();

    public string BaseHint { get => _baseHint; private set => Set(ref _baseHint, value); }

    /// <summary>The base is Vehicles: the dashboard keeps the vehicle (<c>titles.custom.vehicleArt</c>).</summary>
    public bool ShowsVehicleArt { get => _showsVehicleArt; private set => Set(ref _showsVehicleArt, value); }

    public string SpeedHeader { get => _speedHeader; private set => Set(ref _speedHeader, value); }

    public bool CanDuplicate => _titles.Document.CanAddCustom;

    /// <summary><c>titles.custom.limit</c> while no copy can be made; null otherwise.</summary>
    public string? DuplicateTooltip { get => _duplicateTooltip; private set => Set(ref _duplicateTooltip, value); }

    /// <summary><c>titles.custom.tooLong</c> or <c>titles.custom.noEmoji</c>, for 4 s.</summary>
    public string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => _notice is not null;

    /// <summary>The field that has the focus (0 = idle), or null.</summary>
    public int? FocusedLevel
    {
        get => _focusedLevel;
        private set
        {
            if (Set(ref _focusedLevel, value))
            {
                OnPropertyChanged(nameof(PreviewLevel));
                UpdatePreviewName();
            }
        }
    }

    /// <summary>The level both previews show: the focused row's, else 13.</summary>
    public int PreviewLevel => TitleEditorMotion.PreviewLevel(_focusedLevel);

    /// <summary>The edited theme's labels as the readout and the badge draw them (with the draft being typed).</summary>
    public IReadOnlyList<string> PreviewLabels
    {
        get => _previewLabels;
        private set
        {
            if (!_previewLabels.SequenceEqual(value, StringComparer.Ordinal))
            {
                _previewLabels = value;
                OnPropertyChanged();
            }
        }
    }

    public bool PreviewShowsVehicle { get => _previewShowsVehicle; private set => Set(ref _previewShowsVehicle, value); }

    public OrbStyle PreviewStyle { get => _previewStyle; private set => Set(ref _previewStyle, value); }

    /// <summary><c>titles.custom.preview</c> and the title the previews show.</summary>
    public string PreviewName { get => _previewName; private set => Set(ref _previewName, value); }

    public string PreviewOrbCaption => _loc.Format("orbs.row.detail", _loc["settings.orb"], (int)TitleOrbPreview.Diameter);

    public double Redline => _settings.Current.Redline;

    public bool DarkTheme => _owner.DarkTheme;

    public bool ReduceMotion => _owner.ReduceMotion;

    public string Unit => _loc["unit.tokS"];

    public string PeakText => _loc["peak.new"];

    public string LanguageCode => _loc.LanguageCode;

    public bool Latin => _loc.UsesLatinScript;

    private CustomTitleSet? CurrentSet => _setId is null ? null : _titles.Document.CustomSet(_setId);

    /// <summary>Edits <paramref name="setId"/> (null hides the editor); the previews rest on level 13 for a new set.</summary>
    public void Show(string? setId)
    {
        if (setId != _setId)
        {
            _setId = setId;
            Notice = null;
            if (_focusedLevel is { } level)
            {
                Rows[level].IsFocused = false;
            }

            FocusedLevel = null;
            OnPropertyChanged(nameof(SetId));
        }

        Fill();
    }

    public void Relocalize(Localizer loc)
    {
        _loc = loc;
        _syncing = true;
        try
        {
            for (var i = 0; i < BaseOptions.Count; i++)
            {
                BaseOptions[i].Name = loc[TitlePresets.NameKey(TitlePresets.All[i])];
            }
        }
        finally
        {
            _syncing = false;
        }

        SpeedHeader = loc.Format("titles.custom.speedColumn", loc["unit.tokS"]);
        Notice = null;
        Fill();
        OnPropertyChanged(nameof(PreviewOrbCaption));
        OnLookChanged();
    }

    /// <summary>Appearance, motion, language, redline or the selected orb's style changed: the previews follow.</summary>
    public void OnLookChanged()
    {
        PreviewStyle = _owner.SelectedOrbStyle?.Invoke() ?? OrbStyle.Led;
        foreach (var name in new[]
                 {
                     nameof(Redline), nameof(DarkTheme), nameof(ReduceMotion), nameof(Unit), nameof(PeakText), nameof(LanguageCode), nameof(Latin),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Reads the set, the redline and the draft into the editor.</summary>
    public void Fill()
    {
        if (CurrentSet is not { } set || _titles.Theme(set.Id) is not { } theme)
        {
            return;
        }

        var loc = _loc;
        var redline = _settings.Current.Redline;
        var dark = _owner.DarkTheme;
        var baseName = loc[TitlePresets.NameKey(set.Base)];
        Title = loc.Format("titles.custom.editing", set.Name);
        NameText = set.Name;
        _syncing = true;
        try
        {
            if (_baseIndex != (int)set.Base)
            {
                _baseIndex = (int)set.Base;
            }

            OnPropertyChanged(nameof(BaseIndex));
        }
        finally
        {
            _syncing = false;
        }

        BaseHint = loc.Format("titles.custom.baseHint", baseName);
        ShowsVehicleArt = TitlePresets.ShowsVehicleArt(set.Base);
        DuplicateTooltip = _titles.Document.CanAddCustom ? null : loc.Format("titles.custom.limit", TitleThemesDocument.MaxCustom);
        OnPropertyChanged(nameof(CanDuplicate));
        foreach (var row in Rows)
        {
            var level = row.Level;
            var placeholder = TitleSettingsText.BaseText(level, set.Base, loc);
            row.Threshold = level == 0 ? loc["titles.custom.idleRow"] : TitleSettingsText.Threshold(level, redline);
            if (level > 0)
            {
                row.ThresholdBrush = TitleHeatBrushes.ForLevel(level, redline, dark);
            }

            row.Placeholder = placeholder;
            row.Committed = set.Field(level);
            row.AccessibleName = TitleSettingsText.RowAccessibleName(level, redline, set.Base, loc);
            row.ResetName = loc.Format("titles.custom.reset", placeholder);
        }

        PreviewLabels = theme.Labels(loc);
        PreviewShowsVehicle = theme.ShowsVehicleArt;
        UpdatePreviewName();
        OnPropertyChanged(nameof(Redline));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Renames the set; an empty name keeps the previous one. Returns the stored name.</summary>
    public string CommitName(string text)
    {
        if (_setId is not { } id)
        {
            return text;
        }

        _titles.Rename(id, text);
        return CurrentSet?.Name ?? text;
    }

    internal void OnRowFocused(TitleFieldRowViewModel row)
    {
        if (_focusedLevel is { } previous && previous != row.Level)
        {
            Rows[previous].IsFocused = false;
        }

        row.IsFocused = true;
        FocusedLevel = row.Level;
    }

    /// <summary>Focus left a field: unless another field took it, the previews return to level 13.</summary>
    internal void OnRowBlurred(TitleFieldRowViewModel row)
    {
        _titles.ClearDraft();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_focusedLevel == row.Level && !row.IsFocused)
            {
                FocusedLevel = null;
            }
        });
        row.IsFocused = false;
    }

    internal void Draft(int level, string text)
    {
        if (_setId is { } id)
        {
            _titles.SetDraft(id, level, text);
        }
    }

    internal string Commit(int level, string text)
    {
        if (_setId is not { } id)
        {
            return text;
        }

        var result = _titles.CommitField(id, level, text);
        if (result.Cut)
        {
            ShowNotice(_loc["titles.custom.tooLong"]);
        }
        else if (result.RemovedEmoji)
        {
            ShowNotice(_loc["titles.custom.noEmoji"]);
        }

        return result.Text;
    }

    internal void RequestRowFocus(int level)
    {
        if (level >= 0 && level <= TitlePresets.LevelCount)
        {
            RowFocusRequested?.Invoke(this, level);
        }
    }

    private void ShowNotice(string text)
    {
        Notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        NoticeShown?.Invoke(this, text);
    }

    private void UpdatePreviewName()
    {
        var level = PreviewLevel;
        var title = _setId is not null && _titles.Theme(_setId) is { } theme ? _titles.Text(TitleSettingsText.LevelTitle(level), theme) : string.Empty;
        PreviewName = string.Join(_loc["list.separator"], _loc["titles.custom.preview"], title);
    }
}

/// <summary>One preset in the base picker; its name follows the language.</summary>
internal sealed class TitleBaseOption : ObservableObject
{
    private string _name = string.Empty;

    public string Name { get => _name; set => Set(ref _name, value); }

    public override string ToString() => _name;
}
