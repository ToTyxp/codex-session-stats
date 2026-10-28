using System.Windows.Threading;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Services;

namespace AgentSpeed.Titles;

/// <summary>Kind of undo offered after a custom set is deleted or reset (DESIGN 5, Title themes).</summary>
internal enum TitleUndoKind
{
    Delete,
    Reset,
}

/// <summary>An undo offered for 8 s: the documents before the change, restored exactly.</summary>
internal sealed record TitleThemesUndo(TitleUndoKind Kind, string Name, TitleThemesDocument Before, OrbsDocument? OrbsBefore, DateTime Expires);

/// <summary>Uncommitted text of one custom title field: every surface already shows it (the editor's live previews).</summary>
internal readonly record struct TitleDraft(string SetId, int Level, string Text);

/// <summary>What committing a custom title field did (DESIGN 1, text rules and length).</summary>
internal readonly record struct TitleCommit(string Text, bool RemovedEmoji, bool Cut);

/// <summary>
/// The title themes setting (DESIGN 1): the General theme, each orb's theme, the custom sets and their editing with an
/// 8 s undo, persisted in the settings value <c>titleThemes</c>. Every surface asks this service for its titles.
/// </summary>
internal sealed class TitleThemesService
{
    public static readonly TimeSpan UndoTime = TimeSpan.FromSeconds(8);

    private readonly SettingsService _settings;
    private readonly Func<Localizer> _localizer;
    private readonly DispatcherTimer _undoTimer;
    private TitleDraft? _draft;

    public TitleThemesService(Dispatcher ui, SettingsService settings, Func<Localizer> localizer)
    {
        _settings = settings;
        _localizer = localizer;
        _undoTimer = new DispatcherTimer(DispatcherPriority.Background, ui);
        _undoTimer.Tick += (_, _) => DismissUndo();
        _settings.Changed += (_, e) =>
        {
            if (!ReferenceEquals(e.Previous.TitleThemes, e.Current.TitleThemes) && !e.Previous.TitleThemes.Equals(e.Current.TitleThemes))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The document, a draft or the undo banner changed: titles may read differently everywhere.</summary>
    public event EventHandler? Changed;

    /// <summary>The undo banner appeared, changed or went away.</summary>
    public event EventHandler? UndoChanged;

    public TitleThemesDocument Document => _settings.Current.TitleThemes;

    public TitleThemesUndo? Undo { get; private set; }

    public TitleDraft? Draft => _draft;

    private Localizer Loc => _localizer();

    // ---- Resolution ----

    /// <summary>The General theme (dashboard, Settings → General, taskbar and tray accessibility), with a draft applied.</summary>
    public TitleTheme General => ApplyingDraft(Document.General);

    /// <summary>A floating orb's theme: its own, else General (DESIGN 2b <c>titleTheme</c>).</summary>
    public TitleTheme ForOrb(OrbDefinition? orb) => ApplyingDraft(Document.OrbTheme(orb?.TitleTheme));

    /// <summary>A theme by id with a draft applied, or null when the id names nothing known.</summary>
    public TitleTheme? Theme(string? id) => Document.Theme(id) is { } theme ? ApplyingDraft(theme) : null;

    /// <summary>The text of a title in a theme, as typed (editor, title lists, accessibility).</summary>
    public string Text(SpeedTitle title, TitleTheme theme) => theme.Text(title, Loc);

    /// <summary>The text as the readout and the badge draw it (Latin uppercase in English).</summary>
    public string Label(SpeedTitle title, TitleTheme theme) => theme.Label(title, Loc);

    public string Name(TitleTheme theme) => theme.Name(Loc);

    /// <summary>Shows uncommitted text of a field everywhere until it is committed or abandoned.</summary>
    public void SetDraft(string setId, int level, string text)
    {
        var next = new TitleDraft(setId, level, text ?? string.Empty);
        if (_draft == next)
        {
            return;
        }

        _draft = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearDraft()
    {
        if (_draft is null)
        {
            return;
        }

        _draft = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private TitleTheme ApplyingDraft(TitleTheme theme)
    {
        if (_draft is not { } draft || theme.Custom is not { } set || set.Id != draft.SetId)
        {
            return theme;
        }

        return TitleTheme.Of(set.WithField(draft.Level, draft.Text));
    }

    // ---- Editing ----

    public void Select(string id) => Apply(d => TitleThemeEditing.Select(d, id));

    /// <summary>Duplicates <paramref name="sourceId"/> into a new selected custom set; null at eight sets.</summary>
    public string? Copy(string sourceId)
    {
        var document = Document;
        if (document.Theme(sourceId) is not { } source)
        {
            return null;
        }

        var loc = Loc;
        var id = Guid.NewGuid().ToString("D").ToUpperInvariant();
        if (TitleThemeEditing.Copy(document, sourceId, id, source.Name(loc), name => loc.Format("titles.custom.copyName", name)) is not { } next)
        {
            return null;
        }

        Apply(_ => next);
        return id;
    }

    /// <summary>Deletes a custom set without confirmation; General and orb references fall back to its base; 8 s undo.</summary>
    public void Delete(string id)
    {
        var before = _settings.Current;
        if (before.TitleThemes.CustomSet(id) is not { } set || TitleThemeEditing.Delete(before.TitleThemes, id) is not { } deletion)
        {
            return;
        }

        ClearDraft();
        _settings.Update(s => s with
        {
            TitleThemes = deletion.Document,
            Orbs = OrbEditing.ReplaceTitleTheme(s.Orbs, id, TitlePresets.Id(deletion.Base)),
        });
        OfferUndo(new TitleThemesUndo(TitleUndoKind.Delete, Name(TitleTheme.Of(set)), before.TitleThemes, before.Orbs,
            DateTime.UtcNow + UndoTime));
    }

    public void Rename(string id, string? name) => Apply(d => TitleThemeEditing.Rename(d, id, name));

    public void SetBase(string id, TitlePreset @base) => Apply(d => TitleThemeEditing.SetBase(d, id, @base));

    /// <summary>Applies the text rules and the length budget of the current language, then stores the field.</summary>
    public TitleCommit CommitField(string id, int level, string? text)
    {
        var clean = TitleText.Sanitize(text);
        var language = Loc.LanguageCode;
        var fitted = TitleText.Fitted(clean.Text, TitleText.MaximumWidth, t => TitleMeasure.W7(t, language));
        _draft = null;
        Apply(d => TitleThemeEditing.SetField(d, id, level, fitted.Text));
        Changed?.Invoke(this, EventArgs.Empty);
        return new TitleCommit(fitted.Text, clean.RemovedEmoji, fitted.Cut);
    }

    /// <summary>Empties all 14 fields; 8 s undo.</summary>
    public void ResetAll(string id)
    {
        var before = Document;
        if (before.CustomSet(id) is not { } set || set.IsEmpty)
        {
            return;
        }

        ClearDraft();
        Apply(d => TitleThemeEditing.ResetAll(d, id));
        OfferUndo(new TitleThemesUndo(TitleUndoKind.Reset, Name(TitleTheme.Of(set)), before, null, DateTime.UtcNow + UndoTime));
    }

    // ---- Undo ----

    public string UndoMessage => Undo switch
    {
        { Kind: TitleUndoKind.Delete } undo => Loc.Format("titles.custom.didDelete", undo.Name),
        { Kind: TitleUndoKind.Reset } undo => Loc.Format("titles.custom.didReset", undo.Name),
        _ => string.Empty,
    };

    /// <summary>Restores the set with the same id, its position and every reference.</summary>
    public void UndoLast()
    {
        if (Undo is not { } undo)
        {
            return;
        }

        DismissUndo();
        _settings.Update(s => s with
        {
            TitleThemes = undo.Before,
            Orbs = undo.OrbsBefore is { } orbs ? OrbEditing.RestoreTitleThemes(s.Orbs, orbs) : s.Orbs,
        });
    }

    public void DismissUndo()
    {
        _undoTimer.Stop();
        if (Undo is null)
        {
            return;
        }

        Undo = null;
        UndoChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OfferUndo(TitleThemesUndo undo)
    {
        Undo = undo;
        _undoTimer.Stop();
        _undoTimer.Interval = UndoTime;
        _undoTimer.Start();
        UndoChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(Func<TitleThemesDocument, TitleThemesDocument> change) =>
        _settings.Update(s => s with { TitleThemes = change(s.TitleThemes) });
}
