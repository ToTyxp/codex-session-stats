using System.Security.Cryptography;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

internal sealed partial class ReportController
{
    public bool CanManageProfile => NetworkEnabled && !NeedsReview && Identity?.IsRegistered == true && Discovery?.Extensions.HasProfile == true;
    public DateTimeOffset ProfileNow => DateTimeOffset.FromUnixTimeMilliseconds(UnixMs + _clockCorrection * 1000);

    public async Task<bool> RenameDeviceNameAsync(string name)
    {
        if (!NetworkEnabled || Identity is not { IsRegistered: true } identity
            || ReportUsername.Validate(name, UsernameRules) is not null) return false;
        if (identity.Username == name) return true;
        ClearRejection();
        await Rename(name);
        return Identity?.DeviceId == identity.DeviceId && Identity.Username == name;
    }

    public async Task<ReportHttpResponse?> ProfileRequestAsync(string method, string path, byte[]? data = null, CancellationToken cancellationToken = default)
    {
        if (!CanManageProfile || Discovery is not { } document || Identity is not { } identity
            || !ReportProfileEndpoint.Allows(document.Extensions, method, path)) return null;
        var body = data ?? [];
        var avatar = path == "/v1/profile/avatar";
        if (method == "PUT" && (!avatar || !ReportAvatarImage.IsUpload(body))) return null;
        if (method != "PUT" && body.Length > ReportApi.ApiMaxBytes) return null;
        if (_signer is null) LoadSecrets();
        if (_signer is not { } signer) return null;
        ReportRequest request;
        try
        {
            request = new ReportRequestBuilder(Client.UserAgent, identity.DeviceId, _apiKey)
                .Make(method, document.Api.Endpoint(path), body, signer, UnixMs / 1000 + _clockCorrection);
            if (avatar)
            {
                var headers = request.Headers.Where(h => h.Key is not ("Accept" or "Content-Type")).ToList();
                headers.Add(new("Accept", method == "GET" ? "image/png" : "application/json"));
                if (method == "PUT") headers.Add(new("Content-Type", "image/png"));
                request = request with { Headers = headers };
            }
        }
        catch (CryptographicException) { return null; }
        try
        {
            var response = await new ReportProfileApi(_counting).SendAsync(request, avatar && method == "GET", cancellationToken).ConfigureAwait(true);
            return NetworkEnabled && Identity?.DeviceId == identity.DeviceId && Discovery?.Api.Text == document.Api.Text ? response : null;
        }
        catch (OperationCanceledException) { return null; }
    }
}
