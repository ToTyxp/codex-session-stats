using System.Diagnostics;
using System.Windows;
using AgentSpeed.App;
using AgentSpeed.Controls;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Updates;

namespace AgentSpeed.Updates;

/// <summary>One rendered line of the release notes.</summary>
internal sealed class NoteLine(NoteBlock block)
{
    public string Text { get; } = block.Text;

    public string Marker { get; } = block.Marker;

    public bool IsHeading { get; } = block.Kind == NoteBlockKind.Heading;

    public bool HasMarker { get; } = block.Kind == NoteBlockKind.Bullet;

    public Thickness Margin { get; } = block.Kind switch
    {
        NoteBlockKind.Heading => new Thickness(0, 6, 0, 4),
        NoteBlockKind.Bullet => new Thickness(block.Indent * 16, 1, 0, 1),
        _ => new Thickness(0, 2, 0, 6),
    };
}

/// <summary>The compact update window (UPDATES section 4): offer, progress, result and errors.</summary>
internal sealed class UpdateWindowViewModel : ObservableObject, IDisposable
{
    private readonly UpdateService _updates;
    private Localizer _loc;
    private ReleaseInfo? _notesRelease;
    private IReadOnlyList<NoteLine> _notes = [];

    public UpdateWindowViewModel(UpdateService updates, Localizer localizer)
    {
        _updates = updates;
        _loc = localizer;
        InstallCommand = new RelayCommand(() => _ = _updates.InstallAsync(), () => Phase is UpdatePhase.Available);
        SkipCommand = new RelayCommand(() =>
        {
            _updates.Skip();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }, () => Phase is UpdatePhase.Available);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        RetryCommand = new RelayCommand(Retry, () => Phase is UpdatePhase.CheckFailed or UpdatePhase.InstallFailed);
        OpenReleaseCommand = new RelayCommand(OpenRelease);
        _updates.Changed += OnUpdatesChanged;
    }

    public event EventHandler? CloseRequested;

    public RelayCommand InstallCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand CloseCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand OpenReleaseCommand { get; }

    private UpdateStatus Status => _updates.Status;

    private UpdatePhase Phase => Status.Phase;

    private bool HasRelease => Phase is UpdatePhase.Available or UpdatePhase.InstallFailed || Status.IsInstalling;

    public string Title => Phase switch
    {
        UpdatePhase.UpToDate => _loc["update.upToDate"],
        UpdatePhase.Checking or UpdatePhase.CheckFailed => _loc["update.check"],
        _ when HasRelease && Status.Release is { } release => UpdateText.AvailableTitle(release, _loc),
        _ => _loc["app.name"],
    };

    public string Subtitle => UpdateText.Current(_updates.CurrentVersion, _loc);

    public IReadOnlyList<NoteLine> Notes
    {
        get
        {
            var release = HasRelease ? Status.Release : null;
            if (!ReferenceEquals(release, _notesRelease))
            {
                _notesRelease = release;
                _notes = release is null ? [] : ReleaseNotes.Parse(release.Notes).Select(b => new NoteLine(b)).ToArray();
            }

            return _notes;
        }
    }

    public bool ShowNotes => HasRelease && Notes.Count > 0;

    public bool ShowOffer => Phase == UpdatePhase.Available;

    public bool ShowProgress => Phase == UpdatePhase.Checking || Status.IsInstalling;

    public bool IsIndeterminate => Phase != UpdatePhase.Downloading;

    public double ProgressPercent => Math.Clamp(Status.Progress, 0, 1) * 100;

    public string ProgressText => UpdateText.StatusLine(Status, _loc);

    public bool ShowError => Status.IsFailed;

    public string ErrorText => Phase == UpdatePhase.InstallFailed
        ? UpdateText.InstallFailed(Status.Failure ?? UpdateFailure.Invalid, _loc)
        : UpdateText.FailureReason(Status.Failure ?? UpdateFailure.Network, _loc);

    /// <summary>Close (checking, up to date, check failed, idle) or Later (install failed) next to the primary button.</summary>
    public bool ShowDismiss => !ShowOffer && !Status.IsInstalling;

    public string DismissText => Phase == UpdatePhase.InstallFailed ? _loc["update.later"] : _loc["win.update.close"];

    public bool DismissIsDefault => Phase is UpdatePhase.UpToDate or UpdatePhase.Checking or UpdatePhase.Idle;

    public bool ShowRetry => Status.IsFailed;

    public void Relocalize(Localizer localizer)
    {
        _loc = localizer;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _updates.Changed -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (Phase == UpdatePhase.Idle)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        OnPropertyChanged(string.Empty);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void Retry()
    {
        if (Phase == UpdatePhase.InstallFailed)
        {
            _ = _updates.InstallAsync();
        }
        else
        {
            _ = _updates.CheckAsync(manual: true);
        }
    }

    private void OpenRelease()
    {
        var page = _updates.ReleasePage;
        try
        {
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"Could not open the release page: {UpdateLog.Describe(ex)}");
        }
    }
}
