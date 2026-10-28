using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Controls;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Reporting;

namespace AgentSpeed.Reporting;

internal sealed partial class ReportProfileViewModel : ObservableObject, IDisposable
{
    private readonly ReportController _report;
    private Localizer _loc;
    private readonly DispatcherTimer _poll;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _requests;
    private readonly ProfileNameDraft _name = new();
    private ReportProfile? _profile;
    private ReportPairing? _pairing;
    private byte[]? _pendingAvatar;
    private bool _inviter;
    private bool _busy;
    private bool _active;
    private bool _managerVisible;
    private bool _refreshNeeded;
    private bool _available;
    private string _status = string.Empty;
    private string _codeInput = string.Empty;
    private ImageSource? _avatar;
    private ImageSource? _savedAvatar;
    private string _context;

    public ReportProfileViewModel(ReportController report, Localizer loc)
    {
        _report = report;
        _loc = loc;
        _context = Context;
        _name.Reset(report.FieldUsername);
        RefreshCommand = Command(Refresh);
        ChooseAvatarCommand = new RelayCommand(ChooseAvatar, () => CanAvatar && !Busy);
        UploadAvatarCommand = Command(UploadAvatar, () => CanAvatar && HasPendingAvatar);
        CancelAvatarCommand = new RelayCommand(() => { _pendingAvatar = null; _avatar = _savedAvatar; Changed(); }, () => HasPendingAvatar && !Busy);
        RemoveAvatarCommand = Command(RemoveAvatar, () => CanAvatar && (_profile?.Avatar is not null || _pendingAvatar is not null));
        CreateIdentityCommand = Command(() => ChangeProfile("POST", "/v1/profile/identity", "{}"u8.ToArray()), () => CanIdentity && !IsBound && _profile is not null);
        SaveNameCommand = Command(SaveName, () => CanEditName && _name.IsDirty);
        UnlinkCommand = Command(Unlink, () => CanIdentity && IsBound);
        InviteCommand = Command(Invite, () => _managerVisible && CanIdentity && IsBound && !PairingActive);
        JoinCommand = Command(Join, () => _managerVisible && CanIdentity && !IsBound && _profile is not null && ReportProfileJson.NormalizeCode(CodeInput) is not null && !PairingActive);
        ApproveCommand = Command(Approve, () => _managerVisible && CanApprove);
        CheckCommand = Command(CheckPairing, () => _managerVisible && _pairing is not null);
        CancelCommand = Command(CancelPairing, () => _managerVisible && _pairing is not null);
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(2.5), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher.CurrentDispatcher);
        _poll.Stop();
        _report.Changed += ReportChanged;
    }

    public bool Busy => _busy;
    public bool CanUse => _active && _report.CanManageProfile && !_lifetime.IsCancellationRequested;
    public bool CanAvatar => CanUse && _report.Discovery?.Extensions.ProfileAvatar == true;
    public bool CanIdentity => CanUse && _report.Discovery?.Extensions.SharedIdentity == true;
    public bool CanEditName => CanUse && _profile is not null && (!IsBound || CanIdentity);
    public bool IsBound => _profile?.SharedUserId is not null;
    public bool HasPendingAvatar => _pendingAvatar is not null;
    public bool HasPairing => _pairing is not null;
    internal bool IsPolling => _poll.IsEnabled;
    private bool PairingActive => _pairing?.State is "waiting" or "pending";
    private CancellationToken RequestToken => _requests?.Token ?? _lifetime.Token;
    private string Context => (_report.Identity?.DeviceId ?? string.Empty) + "|" + (_report.Discovery?.Api.Text ?? string.Empty);
    public bool CanApprove => CanIdentity && _inviter && _pairing is { State: "pending", Claimant: not null };
    public string Status => !_report.NetworkEnabled ? _loc["settings.network.offHint"] : !CanUse ? _loc["profile.unavailable"] : _status;
    public string IdentityText => _loc[IsBound ? "profile.identity.bound" : "profile.identity.unbound"];
    public string NameLabel => _loc[IsBound ? "profile.displayName" : "report.username"];
    public string AvatarChoiceLabel => _loc[_avatar is null ? "profile.avatar.choose" : "profile.avatar.change"];
    public string? AvatarUnavailableHint => CanAvatar ? null : _loc["profile.unavailable"];
    public string AvatarStatus => HasPendingAvatar ? _loc["profile.avatar.pending"] : string.Empty;
    public string DisplayName { get => _name.Text; set { _name.Edit(value); Changed(); } }
    public bool NameEditing { get => _name.IsEditing; set { _name.SetEditing(value); Changed(); } }
    public string CodeInput { get => _codeInput; set { Set(ref _codeInput, value); CommandManager.InvalidateRequerySuggested(); } }
    public ImageSource? Avatar => _avatar;
    public string InviteCode => _inviter ? _pairing?.Code ?? string.Empty : string.Empty;
    public string PairingStatus => _pairing is null ? string.Empty : _loc["profile.pairing." + (_pairing.State == "cancelled" ? "expired" : _pairing.State)];
    public string Claimant => _pairing?.Claimant is { } device ? device.Platform + " · " + Fingerprint(device.DeviceId) : string.Empty;
    public IReadOnlyList<string> Devices => _profile?.Devices.Select(d => d.Platform + " · " + Fingerprint(d.DeviceId)).ToArray() ?? [];
    public ICommand RefreshCommand { get; }
    public ICommand ChooseAvatarCommand { get; }
    public ICommand UploadAvatarCommand { get; }
    public ICommand CancelAvatarCommand { get; }
    public ICommand RemoveAvatarCommand { get; }
    public ICommand CreateIdentityCommand { get; }
    public ICommand SaveNameCommand { get; }
    public ICommand UnlinkCommand { get; }
    public ICommand InviteCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand CancelCommand { get; }

    public void SetLocalizer(Localizer loc) { _loc = loc; Changed(); }
    public void RestoreName() { _name.Restore(); Changed(); }
    public void Activate()
    {
        if (_active) return;
        _active = true;
        _available = _report.CanManageProfile;
        _requests = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        RequestRefresh();
        Changed();
    }
    public void Deactivate()
    {
        _active = false;
        _refreshNeeded = false;
        EndDeviceManagement();
        _requests?.Cancel();
        _requests?.Dispose();
        _requests = null;
        Changed();
    }
    public void BeginDeviceManagement()
    {
        _managerVisible = true;
        if (_pairing is { } pairing && pairing.ExpiresAt <= _report.ProfileNow)
            _pairing = pairing with { State = "expired", Code = null };
        if (PairingActive && CanUse) _poll.Start();
        Changed();
    }
    public void EndDeviceManagement()
    {
        _managerVisible = false;
        _poll.Stop();
        _codeInput = string.Empty;
        Changed();
    }
    private void RequestRefresh()
    {
        _refreshNeeded = true;
        if (!CanUse || Busy) return;
        _refreshNeeded = false;
        _ = Run(Refresh);
    }
    private ICommand Command(Func<Task> action, Func<bool>? allowed = null) => new RelayCommand(() => _ = Run(action),
        () => CanUse && !Busy && (allowed?.Invoke() ?? true));
    private async Task Run(Func<Task> action)
    {
        if (!CanUse || _busy) return;
        _busy = true;
        _status = string.Empty;
        Changed();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        { _status = _loc["profile.failed"]; }
        finally
        {
            _busy = false;
            Changed();
            if (_refreshNeeded && CanUse) RequestRefresh();
        }
    }
    private async Task Refresh()
    {
        if (!await ChangeProfile("GET", "/v1/profile")) return;
        if (_profile?.Avatar is { } avatar && _pendingAvatar is null && CanAvatar)
        {
            var response = await _report.ProfileRequestAsync("GET", "/v1/profile/avatar", cancellationToken: RequestToken);
            if (response is { Status: 200 } && response.Headers.GetValueOrDefault("content-type")?.Split(';')[0].Trim() == "image/png"
                && ReportHash.Sha256Hex(response.Body) == avatar.Sha256) _avatar = ReportAvatarBitmap.Preview(response.Body);
            else if (CanUse) _status = _loc["profile.failed"];
        }
    }
    private async Task<bool> ChangeProfile(string method, string path, byte[]? body = null)
    {
        if (!CanUse) return false;
        var context = _context;
        var response = await _report.ProfileRequestAsync(method, path, body, RequestToken);
        if (!CanUse || context != _context) return false;
        var profile = response is { Status: 200 or 201 } && _report.Discovery is { } document
            ? ReportProfileJson.Profile(response.Body, document.UsernameRules) : null;
        if (profile is null) { _status = _loc["profile.failed"]; return false; }
        _profile = profile;
        _name.Receive(profile.DisplayName);
        if (profile.Avatar is null && _pendingAvatar is null) _avatar = null;
        return true;
    }
    private void ChooseAvatar()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG / JPEG|*.png;*.jpg;*.jpeg", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true) PrepareAvatar(dialog.FileName);
    }
    internal bool PrepareAvatar(string path)
    {
        try
        {
            var image = ReportAvatarBitmap.Prepare(path);
            if (_pendingAvatar is null) _savedAvatar = _avatar;
            _pendingAvatar = image.Png;
            _avatar = image.Preview;
            _status = string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { _status = _loc["profile.avatar.invalid"]; }
        Changed();
        return _status.Length == 0;
    }
    private async Task UploadAvatar()
    {
        if (_pendingAvatar is { } bytes && await ChangeProfile("PUT", "/v1/profile/avatar", bytes))
        { _pendingAvatar = null; _savedAvatar = _avatar; }
    }
    private async Task RemoveAvatar()
    {
        if (await ChangeProfile("DELETE", "/v1/profile/avatar")) { _pendingAvatar = null; _avatar = _savedAvatar = null; }
    }
    private async Task SaveName()
    {
        var submitted = _name.Text;
        var name = ReportUsername.Normalized(submitted);
        if (_report.Discovery is not { } document || ReportUsername.Validate(name, document.UsernameRules) is not null)
        { _status = _loc["profile.failed"]; return; }
        if (IsBound)
        {
            if (!await ChangeProfile("PATCH", "/v1/profile", ReportJson.Object(("displayName", ReportJson.Of(name))).ToCompactBytes())) return;
        }
        else
        {
            if (!await _report.RenameDeviceNameAsync(name)) { if (CanUse) _status = _loc["profile.failed"]; return; }
            if (!CanUse) return;
            if (_profile is not null) _profile = _profile with { DisplayName = _report.FieldUsername };
        }
        _name.Commit(submitted, _profile?.DisplayName ?? _report.FieldUsername);
    }
    private void ReportChanged(object? sender, EventArgs args)
    {
        var reset = _context != Context;
        if (reset)
        {
            _context = Context;
            EndDeviceManagement();
            _pairing = null;
            _profile = null;
            _pendingAvatar = null;
            _avatar = _savedAvatar = null;
            _name.Reset(_report.FieldUsername);
        }
        var available = _report.CanManageProfile;
        if (!available)
        {
            _poll.Stop();
            _requests?.Cancel();
        }
        else if (_active && (!_available || reset))
        {
            _requests?.Dispose();
            _requests = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            RequestRefresh();
        }
        _available = available;
        Changed();
    }
    private void Changed() { OnPropertyChanged(string.Empty); CommandManager.InvalidateRequerySuggested(); }
    private static string Fingerprint(string id) => id[..8] + "…" + id[^4..];
    public void Dispose()
    {
        Deactivate();
        _lifetime.Cancel();
        _report.Changed -= ReportChanged;
        _pendingAvatar = null;
        _pairing = null;
    }
}
