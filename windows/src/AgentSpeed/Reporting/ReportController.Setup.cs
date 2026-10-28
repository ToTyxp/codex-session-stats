using System.Security.Cryptography;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Reporting;

/// <summary>
/// The dashboard invitation (DESIGN 11) and the setup that follows its one click (REPORTING 3.3, One-click consent): fetch the
/// preset's document, confirm it when the invitation covered it, create the key and register. Reporting that is on but not
/// registered yet is driven from here, and leaves nothing behind when it is turned off.
/// </summary>
internal sealed partial class ReportController
{
    /// <summary><c>report.invite.accept</c>: the user's consent for the preset; nothing was requested before it.</summary>
    public void AcceptInvitation()
    {
        if (!InvitationOffered || SelectedService is null)
        {
            return;
        }

        UpdatePreferences(p => p with { Invite = ReportInviteAnswer.Accepted, Enabled = true });
        Problem = null;
        ClearSetupMessages();
        _consentWanted = false;
        InviteJustAccepted = true;
        After(InviteDoneHold, () =>
        {
            InviteJustAccepted = false;
            Notify();
        });
        ContinueSetup();
        Notify();
    }

    /// <summary><c>report.invite.decline</c> and the card's close button: answered, and nothing else happens.</summary>
    public void DeclineInvitation()
    {
        if (Preferences.Invite is null)
        {
            UpdatePreferences(p => p with { Invite = ReportInviteAnswer.Declined });
            Notify();
        }
    }

    /// <summary><c>report.service.review</c> for <c>report.status.review</c>: the consent sheet finishes what the invitation did not cover.</summary>
    public void ReviewInvitation() => BeginConsent(review: false);

    /// <summary>The invitation's consent still applies: accepted, for the preset, and no consent sheet asked for instead.</summary>
    private bool CanConfirmByInvitation =>
        Preferences.Invite == ReportInviteAnswer.Accepted && Preferences.Preset == ReportPreset.CodexRadar && !_consentWanted && Consent is null;

    /// <summary>
    /// Moves reporting that is on but not registered one step on: register an existing device, or fetch the document and
    /// confirm it. Runs on accepting, at launch, when the network comes back, on <c>report.service.retry</c>, after the user
    /// fixed a name or a key, and from the hourly retry after a passing failure.
    /// </summary>
    private void ContinueSetup()
    {
        if (!IsSettingUp || !NetworkEnabled || Problem is not null || Consent is not null || Registering || SelectedService is not { } service)
        {
            return;
        }

        if (Identity is { } identity)
        {
            if (identity.Pin is null || identity.Discovery is not { } document)
            {
                return;
            }

            if (_signer is null)
            {
                LoadSecrets();
            }

            ReRegister(NextUsername(document.UsernameRules));
            return;
        }

        if (!CanConfirmByInvitation || ServiceState.Kind == ReportServiceStateKind.Checking)
        {
            return;
        }

        if (ServiceState is { Kind: ReportServiceStateKind.Ready, Document: { } ready })
        {
            ConfirmInvitedIfCovered(ready, service);
        }
        else
        {
            CheckService();
        }
    }

    /// <summary>
    /// REPORTING 3.3: the fetched document is stored as confirmed without a sheet only when it adds nothing the invitation did
    /// not say; then the key is created and the device registers.
    /// </summary>
    private void ConfirmInvitedIfCovered(ReportDiscovery document, ReportServiceUrl service)
    {
        if (!IsSettingUp || Identity is not null || !CanConfirmByInvitation || Problem is not null || !Equals(SelectedService, service))
        {
            return;
        }

        if (!document.SupportsThisClient)
        {
            Problem = ReportProblem.Upgrade;
            Notify();
            return;
        }

        var name = NextUsername(document.UsernameRules);
        var presetDomain = Preferences.Preset == ReportPreset.CodexRadar ? PresetDomain : null;
        if (ReportInvite.Gap(document, service, name, presetDomain) is { } gap)
        {
            InviteGap = gap;
            Notify();
            return;
        }

        InviteGap = null;
        var deviceId = ReportCanonical.NewUuid();
        IReportSigner key;
        try
        {
            key = _keyStore.CreateKey(deviceId);
        }
        catch (CryptographicException)
        {
            RegistrationFailure = ReportFailure.Key;
            Notify();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var identity = new ReportIdentity
        {
            Service = service,
            DeviceId = deviceId,
            Pin = ReportConsentPin.Of(document),
            Discovery = document,
            DiscoveryFetchedAt = now,
            KeyCreatedAt = now,
            LastUsedAt = now,
        };
        IReadOnlyList<string> dropped = [];
        UpdatePreferences(p => p.Upsert(identity, out dropped));
        foreach (var old in dropped)
        {
            _keyStore.DeleteAll(old);
        }

        _signer = key;
        _pendingSigner = null;
        _apiKey = null;
        RegistrationFailure = null;
        ReRegister(name);
    }

    /// <summary><c>report.service.retry</c>: the fetch or registration that failed, again, at once.</summary>
    public void Retry()
    {
        if (!NetworkEnabled)
        {
            return;
        }

        if (IsSettingUp)
        {
            RegistrationFailure = null;
            Cancel(ref _setupTimer);
            ContinueSetup();
        }
        else if (_consentWanted)
        {
            BeginConsent(review: false);
        }
        else
        {
            CheckService();
        }

        Notify();
    }

    /// <summary>At most one automatic registration attempt an hour (REPORTING 7.3).</summary>
    private void ScheduleSetupRetry()
    {
        Cancel(ref _setupTimer);
        _setupTimer = After(SetupRetryInterval, ContinueSetup);
    }

    /// <summary>
    /// Turning reporting off while it is being set up: the unregistered device and its keys go, and so do the setup's
    /// messages, so a later visit starts clean.
    /// </summary>
    private void EndSetup()
    {
        Cancel(ref _setupTimer);
        _consentWanted = false;
        ClearSetupMessages();
        DiscardUnregisteredIdentity();
        if (ServiceIssue is not null)
        {
            ServiceState = ReportServiceState.Unknown;
        }

        if (Problem is ReportProblem.UsernameTaken or ReportProblem.UsernameRejected or ReportProblem.ApiKey)
        {
            Problem = null;
        }
    }

    private void ClearSetupMessages()
    {
        InviteGap = null;
        RegistrationFailure = null;
    }

    /// <summary>A device id and key that never registered anywhere carry nothing worth keeping.</summary>
    private void DiscardUnregisteredIdentity()
    {
        if (Identity is not { IsRegistered: false } identity || Registering)
        {
            return;
        }

        _keyStore.DeleteAll(identity.DeviceId);
        UpdatePreferences(p => p.Removing(identity.Id));
        LoadSecrets();
    }

    /// <summary>
    /// The pane closed: with reporting off, a fetch's message belonged to the attempt that caused it (DESIGN 11, Service), so
    /// it goes.
    /// </summary>
    public void PaneClosed()
    {
        if (Consent is null)
        {
            _consentWanted = false;
        }

        if (Preferences.Enabled)
        {
            return;
        }

        if (ServiceIssue is not null)
        {
            ServiceState = ReportServiceState.Unknown;
        }

        if (RenameState == ReportRenameKind.Failed)
        {
            RenameState = ReportRenameKind.Idle;
            RenameError = null;
        }

        Notify();
    }
}
