using System.Windows;
using AgentSpeed.Controls;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

internal sealed partial class ReportProfileViewModel
{
    private async Task Unlink()
    {
        if (!ConfirmDialog.Show(Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), _loc["profile.identity.unlink"],
            _loc["profile.identity.unlinkNote"], string.Empty, _loc["profile.identity.unlink"], _loc["history.cancel"])) return;
        await ChangeProfile("DELETE", "/v1/profile/identity");
        if (_status.Length == 0) { _pairing = null; _poll.Stop(); }
    }

    private async Task Invite()
    {
        var response = await _report.ProfileRequestAsync("POST", "/v1/profile/pairings", "{}"u8.ToArray(), RequestToken);
        if (!CanUse || !_managerVisible) return;
        var pairing = response is { Status: 201 } ? ReportProfileJson.Pairing(response.Body, _report.ProfileNow) : null;
        if (pairing is not { Code: not null, State: "waiting" }) { _status = _loc["profile.failed"]; return; }
        _pairing = pairing;
        _inviter = true;
        if (_managerVisible) _poll.Start();
    }

    private async Task Join()
    {
        if (ReportProfileJson.NormalizeCode(CodeInput) is not { } code) return;
        var response = await _report.ProfileRequestAsync("POST", "/v1/profile/pairings/claim", ReportJson.Object(("code", ReportJson.Of(code))).ToCompactBytes(), RequestToken);
        if (!CanUse || !_managerVisible) return;
        var pairing = response is { Status: 202 } ? ReportProfileJson.Pairing(response.Body, _report.ProfileNow) : null;
        if (pairing is null) { _status = _loc["profile.failed"]; return; }
        _pairing = pairing with { Code = null };
        CodeInput = string.Empty;
        _inviter = false;
        if (_managerVisible) _poll.Start();
    }

    private async Task CheckPairing()
    {
        if (_pairing is not { } prior) return;
        var response = await _report.ProfileRequestAsync("GET", "/v1/profile/pairings/" + prior.Id, cancellationToken: RequestToken);
        if (!CanUse || !_managerVisible) return;
        var pairing = response is { Status: 200 } ? ReportProfileJson.Pairing(response.Body, _report.ProfileNow) : null;
        if (pairing is null) { _status = _loc["profile.failed"]; _poll.Stop(); return; }
        _pairing = pairing with { Code = _inviter && pairing.State is "waiting" or "pending" ? prior.Code : null };
        if (pairing.State is "approved" or "expired" or "cancelled")
        {
            _poll.Stop();
            if (pairing.State == "approved") await Refresh();
        }
    }

    private async Task Approve()
    {
        if (!CanApprove || _pairing is not { } pairing) return;
        var response = await _report.ProfileRequestAsync("POST", "/v1/profile/pairings/" + pairing.Id + "/approve", "{}"u8.ToArray(), RequestToken);
        var approved = response is { Status: 200 } ? ReportProfileJson.Pairing(response.Body, _report.ProfileNow) : null;
        if (approved?.State != "approved") { _status = _loc["profile.failed"]; return; }
        _pairing = approved with { Code = null };
        _poll.Stop();
        await Refresh();
    }

    private async Task CancelPairing()
    {
        if (_pairing is not { } pairing) return;
        var response = await _report.ProfileRequestAsync("DELETE", "/v1/profile/pairings/" + pairing.Id, cancellationToken: RequestToken);
        if (response?.Status != 204) { _status = _loc["profile.failed"]; return; }
        _pairing = null;
        CodeInput = string.Empty;
        _poll.Stop();
    }

    private void Poll()
    {
        if (!_managerVisible || !CanUse || _pairing is null) { _poll.Stop(); return; }
        if (_pairing.ExpiresAt <= _report.ProfileNow)
        { _pairing = _pairing with { State = "expired", Code = null }; _poll.Stop(); Changed(); return; }
        if (!Busy) _ = Run(CheckPairing);
    }
}
