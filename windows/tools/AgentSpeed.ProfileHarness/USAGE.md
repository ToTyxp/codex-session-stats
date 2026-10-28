# Isolated Windows profile verification

Build a self-contained Windows x64 bundle on the development host:

```sh
dotnet publish windows/tools/AgentSpeed.ProfileHarness/AgentSpeed.ProfileHarness.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o windows/artifacts/inline-profile-harness-win-x64
```

No SDK, Python or installed .NET runtime is required on the Windows guest. Run the EXE directly after extracting the bundle.
Use a new, empty output directory for each run. The UI run requires a loopback reference receiver advertising the profile extensions.
An SSH reverse tunnel can forward guest `127.0.0.1:18789` to that isolated receiver; the harness refuses all non-loopback API requests and discovery redirects.

```powershell
.\AgentSpeed.ProfileHarness.exe --no-ui --output C:\Temp\agent-speed-native-001
.\AgentSpeed.ProfileHarness.exe --service http://127.0.0.1:18789 --output C:\Temp\agent-speed-profile-001 --language zh-Hant --hold-seconds 60
```

`--native-filter ProfileAvatarTests` runs only the three new native image tests; omit it to execute all Windows.Tests facts.
The runner executes those existing test methods directly with xUnit assertions, without a test runner or guest SDK.
`--no-ui` makes no HTTP requests. Both modes return exit code 0 only when their applicable checks passed.

The UI run constructs the production API `ReportPage` and its inline `ReportProfileCard`, opens the separate device manager, executes their bound commands, and writes native WPF renders plus `ui-results.json`.
Its fixture image enters the shared native image preparation method directly; it does not simulate interacting with the file picker.
The flow checks the merged avatar/name card, local-device versus shared-name updates, background refresh preserving typed names, offline pending preview, upload/replacement/removal/cancel, narrow layout, two independent device keys, explicit inviter approval, and unlinking only the second device. Leaving and reopening the API page must stop polling and preserve pending edits.
The unlink check uses the signed API directly to avoid automatically answering a production confirmation dialog.
Pairing codes are masked in the approval render and never written to JSON or logs.

The harness skips `Program.Main`, `SingleInstance`, agent monitors, traffic monitoring and history services.
Settings are in memory and point at dedicated paths; encrypted test keys and logs stay under the output directory.
It does not change the running Agent Speed app or its mutex. Its test registrations are deleted from the isolated receiver during normal cleanup.

The default UI run exits after verification. With `--hold-seconds`, it exits after that bounded hold; closing its own Profile window ends the hold immediately and performs cleanup.
If necessary, terminate only the `AgentSpeed.ProfileHarness` process. An interrupted run may leave its two synthetic registrations on the disposable reference receiver.

Evidence: `native-tests.json` and `native-tests.log` (individual Windows tests), `ui-results.json` (completed profile flow), `ui-error.json` (failed flow), and `api-profile-*.png` / `device-manager-*.png` (production WPF rendering).
These local checks do not prove a third-party production receiver has deployed the extensions.
