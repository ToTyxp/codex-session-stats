using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Core.Pricing;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Reporting;
using AgentSpeed.Services;
using AgentSpeed.Settings;
using AgentSpeed.Settings.Pages;
using AgentSpeed.Traffic;

namespace AgentSpeed.ProfileHarness;

internal static class ProfileFlow
{
    public static string? Step { get; private set; }
    public static async Task Run(Application app, ReportServiceUrl service, string output, Localizer loc, int hold)
    {
        using var http = new HttpReportTransport();
        var transport = new LoopbackTransport(http);
        var client = new ReportClientInfo("1.3.0", "Windows", 11);
        var api = new ReportApi(transport, client.UserAgent);
        var discovery = (await api.DiscoverAsync(service) as ReportDiscoveryOutcome.Document)?.Discovery
            ?? throw new InvalidOperationException("Loopback discovery failed.");
        if (!discovery.Extensions.ProfileAvatar || !discovery.Extensions.SharedIdentity) throw new InvalidOperationException("Loopback profile extensions are unavailable.");
        var store = new ReportKeyStore(Path.Combine(output, "keys"));
        var idA = ReportCanonical.NewUuid(); var idB = ReportCanonical.NewUuid();
        using var signerA = store.CreateKey(idA); using var signerB = store.CreateKey(idB);
        var nameA = "HarnessA-" + idA[..8]; var nameB = "HarnessB-" + idB[..8];
        async Task<ReportReply> Signed(string id, IReportSigner signer, string method, string endpoint, byte[] body)
        {
            var request = new ReportRequestBuilder(client.UserAgent, id, null).Make(method, discovery.Api.Endpoint(endpoint), body, signer, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return (await api.CallAsync(request)).Reply ?? throw new InvalidOperationException("Loopback signed request failed.");
        }
        if ((await Signed(idA, signerA, "POST", "/v1/devices", ReportDocuments.Registration(idA, nameA, signerA.PublicKey, client))).Status != 201
            || (await Signed(idB, signerB, "POST", "/v1/devices", ReportDocuments.Registration(idB, nameB, signerB.PublicKey, client))).Status != 201)
            throw new InvalidOperationException("Fixture registration failed.");
        await using var cleanup = new Cleanup(async () =>
        {
            var a = await Signed(idA, signerA, "DELETE", "/v1/devices/" + idA, []);
            var b = await Signed(idB, signerB, "DELETE", "/v1/devices/" + idB, []);
            var removed = a.Status is 200 or 204 or 404 && b.Status is 200 or 204 or 404;
            File.WriteAllText(Path.Combine(output, "cleanup.json"), JsonSerializer.Serialize(new { passed = removed, firstStatus = a.Status, secondStatus = b.Status }));
            if (!removed) throw new InvalidOperationException("Fixture cleanup failed.");
        });
        using var settingsA = Settings(app, service, discovery, idA, nameA, Path.Combine(output, "settings-a.json"));
        using var settingsB = Settings(app, service, discovery, idB, nameB, Path.Combine(output, "settings-b.json"));
        using var trafficA = new TrafficService(app.Dispatcher, settingsA); using var trafficB = new TrafficService(app.Dispatcher, settingsB);
        using var controllerA = new ReportController(app.Dispatcher, settingsA, new SpeedHub(), trafficA, new PriceBook(), "1.3.0", transport, store);
        using var controllerB = new ReportController(app.Dispatcher, settingsB, new SpeedHub(), trafficB, new PriceBook(), "1.3.0", transport, store);
        controllerA.Activate(); controllerB.Activate();
        using var paneA = new ReportPaneViewModel(controllerA, settingsA, loc, app.Dispatcher, (_, _) => false, () => { });
        using var paneB = new ReportPaneViewModel(controllerB, settingsB, loc, app.Dispatcher, (_, _) => false, () => { });
        var pageA = new ReportPage { DataContext = paneA };
        var pageB = new ReportPage { DataContext = paneB };
        var window = ApiWindow(pageA, loc);
        var otherWindow = ApiWindow(pageB, loc);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show(); otherWindow.Show();
        var first = paneA.Profile; var second = paneB.Profile;
        var cardA = (ReportProfileCard)pageA.FindName("InlineProfileCard");
        var cardB = (ReportProfileCard)pageB.FindName("InlineProfileCard");
        try
        {
            await Task.Delay(400); await Wait(first); await Wait(second);
            if (!cardA.IsVisible || ((TextBox)pageA.FindName("UsernameBox")).IsVisible)
                throw new InvalidOperationException("The API page did not replace the old username row with the inline profile card.");
            EditName(cardA, "Inline-" + idA[..8]);
            await Invoke(Button(cardA, "SaveNameButton").Command!, first);
            var originalDeviceA = controllerA.Identity?.Username;
            if (originalDeviceA != "Inline-" + idA[..8]) throw new InvalidOperationException("Unbound inline name did not use device rename.");
            EditName(cardA, "Draft kept during refresh");
            await Invoke(Button(cardA, "RefreshProfileButton").Command!, first);
            if (first.DisplayName != "Draft kept during refresh") throw new InvalidOperationException("Profile refresh overwrote an active name draft.");
            first.RestoreName(); first.NameEditing = false;
            await Invoke(Button(cardA, "ManageDevicesButton").Command!, first);
            await Task.Delay(100);
            var managerA = paneA.DeviceManagerWindow ?? throw new InvalidOperationException("Device manager did not open.");
            if (managerA.FindName("ProfileNameBox") is not null) throw new InvalidOperationException("Device manager duplicated profile editing.");
            await Invoke(Button(managerA, "CreateIdentityButton").Command!, first);
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-created.png"));
            var source = Path.Combine(output, "avatar-source.jpg");
            AvatarSource(source, Colors.Teal);
            if (!first.PrepareAvatar(source)) throw new InvalidOperationException("Native avatar preparation failed.");
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-preview.png"));
            await Invoke(Button(cardA, "CancelAvatarButton").Command!, first);
            if (first.HasPendingAvatar) throw new InvalidOperationException("Cancel did not discard the pending avatar.");
            if (!first.PrepareAvatar(source)) throw new InvalidOperationException("Native avatar preparation failed.");
            settingsA.Update(state => state with { NetworkEnabled = false });
            var beforeOffline = controllerA.RequestCount;
            if (Button(cardA, "UploadAvatarButton").Command!.CanExecute(null) || await controllerA.ProfileRequestAsync("GET", "/v1/profile") is not null
                || controllerA.RequestCount != beforeOffline || !first.HasPendingAvatar) throw new InvalidOperationException("Offline profile gating failed.");
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-offline.png"));
            settingsA.Update(state => state with { NetworkEnabled = true });
            await Wait(first);
            await Invoke(Button(cardA, "UploadAvatarButton").Command!, first);
            if (first.HasPendingAvatar) throw new InvalidOperationException("Avatar upload did not synchronize.");
            await Invoke(Button(managerA, "InviteButton").Command!, first);
            var code = first.InviteCode;
            await Invoke(Button(cardB, "ManageDevicesButton").Command!, second);
            await Task.Delay(100);
            var managerB = paneB.DeviceManagerWindow ?? throw new InvalidOperationException("Second device manager did not open.");
            var codeInput = (TextBox)managerB.FindName("PairingCodeBox");
            codeInput.Text = code; codeInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            await Invoke(Button(managerB, "JoinButton").Command!, second);
            code = string.Empty;
            await Invoke(Button(managerA, "CheckPairingButton").Command!, first);
            if (!first.CanApprove || second.IsBound) throw new InvalidOperationException("Pairing did not wait for inviter approval.");
            if (managerA.FindName("InviteCodeBox") is TextBox codeBox) codeBox.SetCurrentValue(TextBox.TextProperty, "••••-••••-••••");
            await Capture(managerA, Path.Combine(output, "device-manager-approval.png"));
            await Invoke(Button(managerA, "ApproveButton").Command!, first);
            await Invoke(Button(managerB, "CheckPairingButton").Command!, second);
            if (!second.IsBound || first.Devices.Count != 2 || second.Avatar is null) throw new InvalidOperationException("Approved pairing or shared avatar was not visible.");
            EditName(cardB, "Windows inline harness");
            await Invoke(Button(cardB, "SaveNameButton").Command!, second);
            first.NameEditing = false;
            await Invoke(Button(cardA, "RefreshProfileButton").Command!, first);
            if (first.DisplayName != "Windows inline harness" || controllerA.Identity?.Username != originalDeviceA || controllerB.Identity?.Username != nameB)
                throw new InvalidOperationException("Shared name editing changed a device username.");
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-shared.png"));
            await Capture(managerA, Path.Combine(output, "device-manager-shared.png"));
            window.Width = 390; window.UpdateLayout();
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-narrow.png"));
            if (((TextBox)cardA.FindName("ProfileNameBox")).ActualWidth < 60) throw new InvalidOperationException("The inline profile name is unusable at the narrow width.");
            window.Width = 650;
            AvatarSource(source, Colors.SlateBlue);
            if (!first.PrepareAvatar(source)) throw new InvalidOperationException("Replacement avatar preparation failed.");
            await Invoke(Button(cardA, "UploadAvatarButton").Command!, first);
            await Invoke(Button(cardB, "RefreshProfileButton").Command!, second);
            await Invoke(Button(cardB, "RemoveAvatarButton").Command!, second);
            await Invoke(Button(cardA, "RefreshProfileButton").Command!, first);
            if (first.Avatar is not null) throw new InvalidOperationException("Shared avatar removal did not synchronize.");
            await Invoke(Button(managerA, "InviteButton").Command!, first);
            if (!first.IsPolling) throw new InvalidOperationException("Visible device manager did not begin polling.");
            if (!first.PrepareAvatar(source)) throw new InvalidOperationException("Pending preview preparation failed.");
            EditName(cardA, "Draft survives navigation");
            var originalContent = window.Content;
            window.Content = new TextBlock { Text = "Inactive API page" };
            await Task.Delay(100);
            var beforeHidden = controllerA.RequestCount;
            await Task.Delay(2800);
            if (first.IsPolling || controllerA.RequestCount != beforeHidden || !first.HasPendingAvatar || first.DisplayName != "Draft survives navigation")
                throw new InvalidOperationException("Leaving API settings did not suspend polling and preserve drafts.");
            window.Content = originalContent;
            await Task.Delay(150); await Wait(first);
            if (!first.HasPendingAvatar || first.DisplayName != "Draft survives navigation") throw new InvalidOperationException("Reopening API settings overwrote pending edits.");
            first.RestoreName(); first.NameEditing = false;
            await Invoke(Button(cardA, "CancelAvatarButton").Command!, first);
            var unlinked = await controllerB.ProfileRequestAsync("DELETE", "/v1/profile/identity");
            if (unlinked?.Status != 200) throw new InvalidOperationException("Fixture unlink failed.");
            second.NameEditing = false;
            await Invoke(Button(cardB, "RefreshProfileButton").Command!, second);
            await Invoke(Button(cardA, "RefreshProfileButton").Command!, first);
            if (second.IsBound || first.Devices.Count != 1 || controllerA.Identity?.Username != originalDeviceA || controllerB.Identity?.Username != nameB)
                throw new InvalidOperationException("Unlink or device username isolation failed.");
            await CaptureCard(window, cardA, Path.Combine(output, "api-profile-unlinked.png"));
            File.WriteAllText(Path.Combine(output, "ui-results.json"), JsonSerializer.Serialize(new
            {
                passed = true, platform = Environment.OSVersion.VersionString, isolated = true, mutexAcquired = false,
                monitorStarted = false, historyServiceStarted = false, settingsEphemeral = true, loopbackOnly = true,
                inlineApiProfile = true, noDuplicateUsernameRow = true, unboundNameUsesDeviceRename = true,
                sharedNamePreservesDeviceNames = true, refreshPreservesTyping = true, navigationSuspendsPolling = true,
                navigationPreservesPendingEdits = true, narrowProfileInputUsable = true, managerContainsOnlyDeviceOperations = true,
                independentKeys = signerA.PublicKey != signerB.PublicKey, pendingAvatarPreservedOffline = true, explicitInviterApproval = true,
                avatarPrepareUploadReplaceRemoveCancel = true, unlinkPreservesOtherDevice = true, fixtureAvatarSelectedThroughSharedPreparation = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (hold > 0) await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(hold)), closed.Task);
        }
        finally { window.Close(); otherWindow.Close(); }
    }

    private static Window ApiWindow(ReportPage page, Localizer loc) => new()
    {
        Title = loc["profile.title"], Width = 650, Height = 800, MinWidth = 360, MinHeight = 400,
        MaxHeight = SystemParameters.WorkArea.Height * 0.92, ShowInTaskbar = false,
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = SystemColors.WindowBrush },
    };
    private static Button Button(FrameworkElement owner, string name) => (Button)(owner.FindName(name)
        ?? throw new InvalidOperationException("Missing UI control: " + name));
    private static void EditName(ReportProfileCard card, string name)
    {
        Window.GetWindow(card)?.Activate();
        var input = (TextBox)card.FindName("ProfileNameBox");
        input.Focus(); input.Text = name; input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
    private static async Task CaptureCard(Window window, ReportProfileCard card, string path)
    {
        window.UpdateLayout();
        card.BringIntoView();
        await Dispatcher.Yield(DispatcherPriority.Background);
        window.UpdateLayout();
        CaptureElement(card, path);
    }

    private static SettingsService Settings(Application app, ReportServiceUrl service, ReportDiscovery discovery, string id, string username, string path)
    {
        Environment.SetEnvironmentVariable("AGENT_SPEED_SETTINGS_FILE", path);
        var settings = new SettingsService(app.Dispatcher);
        settings.MakeEphemeral();
        var identity = new ReportIdentity { Service = service, DeviceId = id, Username = username, Discovery = discovery,
            DiscoveryFetchedAt = DateTimeOffset.UtcNow, Pin = ReportConsentPin.Of(discovery), KeyCreatedAt = DateTimeOffset.UtcNow };
        settings.Update(s => s with { NetworkEnabled = true, Report = new ReportPreferences { Enabled = false, Preset = ReportPreset.Custom,
            CustomUrl = service.Text, Identities = [identity] } });
        return settings;
    }
    private static async Task Invoke(ICommand command, ReportProfileViewModel model, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(command))] string? step = null)
    {
        Step = step;
        if (!command.CanExecute(null)) throw new InvalidOperationException("The profile action is unavailable.");
        command.Execute(null); await Wait(model);
        if (model.Status.Length > 0) throw new InvalidOperationException("The profile action did not synchronize.");
    }
    private static async Task Wait(ReportProfileViewModel model)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(15);
        while (model.Busy && DateTimeOffset.UtcNow < end) await Task.Delay(100);
        if (model.Busy) throw new InvalidOperationException("The profile action timed out.");
    }
    private static async Task Capture(Window window, string path)
    {
        if (window.Content is ScrollViewer viewer)
        {
            if (path.EndsWith("profile-approval.png", StringComparison.Ordinal)) viewer.ScrollToEnd();
            else viewer.ScrollToTop();
        }
        await Dispatcher.Yield(DispatcherPriority.Background);
        window.UpdateLayout();
        if (window.Content is not FrameworkElement content) throw new InvalidOperationException("Missing profile content.");
        CaptureElement(content, path);
    }
    private static void CaptureElement(FrameworkElement content, string path)
    {
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var ink = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 0 && pixels[i] < 180 && pixels[i + 1] < 180 && pixels[i + 2] < 180) ink++;
        if (ink < 100) throw new InvalidOperationException("The UI capture contains no rendered content.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void AvatarSource(string path, Color color)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 360, 240));
            context.DrawEllipse(Brushes.White, null, new Point(180, 90), 45, 45);
            context.DrawRoundedRectangle(Brushes.White, null, new Rect(95, 150, 170, 85), 42, 42);
        }
        var bitmap = new RenderTargetBitmap(360, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class LoopbackTransport(IReportTransport inner) : IReportTransport
    {
        public Task<ReportHttpResponse> SendAsync(ReportRequest request, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken = default) => request.Url.IsLoopback
            ? inner.SendAsync(request, maxBytes, timeout, cancellationToken) : throw new InvalidOperationException("Only loopback is allowed.");
        public Task<ReportHttpResponse> GetAsync(Uri url, string? etag, string userAgent, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            SendAsync(new ReportRequest("GET", url, [new("User-Agent", userAgent), new("Accept", "application/json")], []), maxBytes, timeout, cancellationToken);
    }
    private sealed class Cleanup(Func<Task> cleanup) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(cleanup());
    }
}
