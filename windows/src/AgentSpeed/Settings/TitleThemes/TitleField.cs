using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// The behaviour of a custom title field (DESIGN 5, Title themes editor), attached to a plain Fluent text box: typing
/// is a draft every surface shows at once; Return, focus leaving or 0.8 s without typing commits (never during an IME
/// composition); ↑ / ↓ move between rows, Return commits and moves down, Esc restores the text the field had when it
/// was focused. Ctrl+Z stays the text box's own undo.
/// </summary>
internal sealed class TitleField
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached("Row", typeof(TitleFieldRowViewModel),
        typeof(TitleField), new PropertyMetadata(null, OnRowChanged));

    /// <summary>The field has text (the row's reset button fades in).</summary>
    public static readonly DependencyProperty HasTextProperty = DependencyProperty.RegisterAttached("HasText", typeof(bool),
        typeof(TitleField), new PropertyMetadata(false));

    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached("Controller", typeof(TitleField),
        typeof(TitleField), new PropertyMetadata(null));

    private static readonly TimeSpan IdleCommit = TimeSpan.FromSeconds(0.8);

    private readonly TextBox _box;
    private readonly DispatcherTimer _idle;
    private TitleFieldRowViewModel? _row;
    private string _focusText = string.Empty;
    private string _modelText = string.Empty;
    private bool _committing;
    private bool _composing;
    private bool _writing;

    private TitleField(TextBox box)
    {
        _box = box;
        _idle = new DispatcherTimer(IdleCommit, DispatcherPriority.Input, (_, _) => CommitIfIdle(), box.Dispatcher);
        _idle.Stop();
        box.GotKeyboardFocus += OnGotFocus;
        box.LostKeyboardFocus += OnLostFocus;
        box.TextChanged += OnTextChanged;
        box.PreviewKeyDown += OnPreviewKeyDown;
        TextCompositionManager.AddPreviewTextInputStartHandler(box, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(box, (_, _) => OnCompositionEnded());
        box.Unloaded += (_, _) => OnUnloaded();
        box.Loaded += (_, _) => Attach(_row);
    }

    public static TitleFieldRowViewModel? GetRow(DependencyObject element) => (TitleFieldRowViewModel?)element.GetValue(RowProperty);

    public static void SetRow(DependencyObject element, TitleFieldRowViewModel? value) => element.SetValue(RowProperty, value);

    public static bool GetHasText(DependencyObject element) => (bool)element.GetValue(HasTextProperty);

    public static void SetHasText(DependencyObject element, bool value) => element.SetValue(HasTextProperty, value);

    /// <summary>Empties a field as an edit of the text box (so Ctrl+Z brings the text back) and commits it.</summary>
    public static void Reset(TextBox box)
    {
        if (box.GetValue(ControllerProperty) is not TitleField field || box.Text.Length == 0)
        {
            return;
        }

        box.SelectAll();
        box.SelectedText = string.Empty;
        field.Commit();
    }

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        if (box.GetValue(ControllerProperty) is not TitleField field)
        {
            field = new TitleField(box);
            box.SetValue(ControllerProperty, field);
        }

        field.Attach(e.NewValue as TitleFieldRowViewModel);
    }

    private void Attach(TitleFieldRowViewModel? row)
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowPropertyChanged;
        }

        _idle.Stop();
        _row = row;
        if (row is null)
        {
            return;
        }

        // The rows outlive the settings window: Unloaded drops this subscription and Loaded makes it again.
        row.PropertyChanged += OnRowPropertyChanged;

        _modelText = row.Committed;
        Write(row.Committed);
    }

    /// <summary>
    /// A new committed text is this field's own commit or an outside edit (reset all, undo, another set), which replaces
    /// the text unless an input method is composing.
    /// </summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_row is not { } row || (e.PropertyName is not (nameof(TitleFieldRowViewModel.Committed) or null or "")))
        {
            return;
        }

        if (row.Committed == _modelText)
        {
            return;
        }

        _modelText = row.Committed;
        if (!_committing && !_composing && _box.Text != row.Committed)
        {
            _idle.Stop();
            Write(row.Committed);
            _focusText = row.Committed;
        }
    }

    /// <summary>The window closed while typing: the pending text is committed and the draft goes away.</summary>
    private void OnUnloaded()
    {
        if (_idle.IsEnabled)
        {
            Commit();
        }

        _idle.Stop();
        if (_row is { IsFocused: true } row)
        {
            row.Blurred();
        }

        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(e.NewFocus, _box))
        {
            return;
        }

        _focusText = _box.Text;
        _row?.Focused();
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldFocus, _box))
        {
            return;
        }

        _composing = false;
        Commit();
        _row?.Blurred();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SetHasText(_box, _box.Text.Length > 0);
        if (_writing || _row is null)
        {
            return;
        }

        _row.Draft(_box.Text);
        _idle.Stop();
        _idle.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_row is not { } row || _composing || e.Key == Key.ImeProcessed || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                row.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                row.Move(-1);
                e.Handled = true;
                break;
            case Key.Down:
                row.Move(1);
                e.Handled = true;
                break;
            case Key.Escape when _box.Text != _focusText:
                _box.SelectAll();
                _box.SelectedText = _focusText;
                _box.CaretIndex = _box.Text.Length;
                _idle.Stop();
                e.Handled = true;
                break;
        }
    }

    /// <summary>A composition ended (or a key typed a character): the pause starts again for uncommitted text.</summary>
    private void OnCompositionEnded()
    {
        var wasComposing = _composing;
        _composing = false;
        if (wasComposing && _row is not null && _box.Text != _modelText)
        {
            _idle.Stop();
            _idle.Start();
        }
    }

    /// <summary>The pause commits, unless an input method is still composing (its end restarts the pause).</summary>
    private void CommitIfIdle()
    {
        _idle.Stop();
        if (!_composing)
        {
            Commit();
        }
    }

    /// <summary>Stores the text; a field left unchanged stores nothing (no rewrite, no re-cut in another language).</summary>
    private void Commit()
    {
        _idle.Stop();
        if (_row is not { } row || _box.Text == _modelText)
        {
            return;
        }

        var typed = _box.Text;
        string stored;
        _committing = true;
        try
        {
            stored = row.Commit(typed);
        }
        finally
        {
            _committing = false;
        }

        _modelText = row.Committed;
        if (stored != typed)
        {
            Write(stored);
        }
    }

    private void Write(string text)
    {
        if (_box.Text == text)
        {
            return;
        }

        _writing = true;
        try
        {
            var atEnd = _box.CaretIndex >= _box.Text.Length;
            _box.Text = text;
            _box.CaretIndex = atEnd ? text.Length : Math.Min(_box.CaretIndex, text.Length);
        }
        finally
        {
            _writing = false;
        }
    }
}
