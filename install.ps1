<#
Agent Speed installer for Windows.

    irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1 | iex

With options:

    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1))) -Uninstall

Options: -Version vX.Y.Z, -NoLaunch, -NoAutostart, -TrafficHelper, -Uninstall, -Purge

-TrafficHelper also installs (or updates) the traffic helper service that measures per-process traffic
(spec/WINDOWS-SERVICE.md); Windows asks for administrator approval once. Without it an installed helper is never
touched. -Uninstall also removes the helper when this user installed it (one approval prompt).
Environment: AGENT_SPEED_VERSION, AGENT_SPEED_DOWNLOAD_BASE (mirrors, testing)
#>
[CmdletBinding()]
param(
    [string]$Version = $env:AGENT_SPEED_VERSION,
    [switch]$NoLaunch,
    [switch]$NoAutostart,
    [switch]$TrafficHelper,
    [switch]$Uninstall,
    [switch]$Purge
)

# Run in a child scope so preferences and helpers do not leak into the caller's session.
& {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $Repo = 'zoefix/agent-speed'
    $AppName = 'Agent Speed'
    $ExeName = 'AgentSpeed.exe'
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\AgentSpeed'
    $StartMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
    $UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AgentSpeed'
    $RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $SettingsDir = Join-Path $env:APPDATA 'AgentSpeed'
    $LogsDir = Join-Path $env:LOCALAPPDATA 'AgentSpeed'

    function Get-Text {
        $culture = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        $t = @{
            Downloading = 'Downloading the latest release'; Verifying = 'Verifying integrity'; Installing = 'Installing'
            Done = 'Installed'; Launched = 'Agent Speed is running — look at your taskbar.'
            UpdateHint = 'Run the same command again any time to update.'; Uninstalling = 'Uninstalling Agent Speed'
            Uninstalled = 'Removed'; NotInstalled = 'Agent Speed is not installed'; OldWindows = 'Windows 10 (1809) or later is required'
            DownloadFailed = 'Download failed'; NoChecksum = 'Checksum not found'; BadChecksum = 'Checksum mismatch, installation aborted'
            BadArchive = 'Unexpected package contents'; Retrying = 'The download was incomplete, trying again…'
            MirrorHint = 'If GitHub is slow on your network, set AGENT_SPEED_DOWNLOAD_BASE to a mirror of the release files.'
            HelperPreparing = 'Preparing the traffic helper'
            HelperMissing = 'This build carries no traffic helper, so per-agent traffic stays unavailable.'
            HelperApprove = 'Windows will now ask whether to let "Agent Speed Traffic Helper" make changes. The publisher shows as unknown because releases are not code-signed yet.'
            HelperWaiting = 'Waiting for administrator approval…'
            HelperInstalled = 'Traffic helper installed. Per-agent traffic is now measured.'
            HelperCancelled = 'Cancelled. Nothing was changed.'
            HelperRefused = 'The traffic helper refused to install because a safety check failed (for example, its install folder is writable by other users).'
            HelperFailed = 'The traffic helper could not be installed (code {0}).'
            HelperRemoving = 'Removing the traffic helper'
            HelperRemoveApprove = 'Approve the Windows prompt for "Agent Speed Traffic Helper" to remove it (the publisher shows as unknown).'
            HelperRemoved = 'The traffic helper was removed.'
            HelperRemoveCancelled = 'Cancelled. The traffic helper stays installed.'
            HelperRemoveFailed = 'The traffic helper could not be removed (code {0}).'
        }
        if ($culture -match '^zh-(TW|HK|MO)|^zh-Hant') {
            $t = @{
                Downloading = '下載最新版本'; Verifying = '驗證檔案完整性'; Installing = '安裝'
                Done = '安裝完成'; Launched = 'Agent Speed 已啟動，請看工作列。'
                UpdateHint = '之後重新執行同一行指令即可更新。'; Uninstalling = '移除 Agent Speed'
                Uninstalled = '已移除'; NotInstalled = '沒有找到已安裝的 Agent Speed'; OldWindows = '需要 Windows 10 (1809) 或更新版本'
                DownloadFailed = '下載失敗'; NoChecksum = '找不到校驗碼'; BadChecksum = '校驗碼不符，已中止安裝'
                BadArchive = '安裝包內容不正確'; Retrying = '下載不完整，重新下載…'
                MirrorHint = '若 GitHub 在你的網路很慢，可把 AGENT_SPEED_DOWNLOAD_BASE 設為發佈檔案的鏡像網址。'
                HelperPreparing = '準備流量輔助服務'
                HelperMissing = '這個版本沒有附帶流量輔助服務，無法記錄各 agent 的流量。'
                HelperApprove = '接下來 Windows 會詢問是否允許「Agent Speed Traffic Helper」變更這台電腦。發行者會顯示為「未知」，因為目前的版本沒有數位簽章。'
                HelperWaiting = '等待系統管理員確認…'
                HelperInstalled = '流量輔助服務已安裝，開始記錄各 agent 的流量。'
                HelperCancelled = '已取消，沒有變更任何設定。'
                HelperRefused = '流量輔助服務拒絕安裝：安全檢查未通過（例如安裝資料夾可被其他使用者寫入）。'
                HelperFailed = '無法安裝流量輔助服務（代碼 {0}）。'
                HelperRemoving = '移除流量輔助服務'
                HelperRemoveApprove = '請在 Windows 詢問是否允許「Agent Speed Traffic Helper」變更這台電腦時同意，以移除它（發行者會顯示為「未知」）。'
                HelperRemoved = '已移除流量輔助服務。'
                HelperRemoveCancelled = '已取消，流量輔助服務仍保持安裝。'
                HelperRemoveFailed = '無法移除流量輔助服務（代碼 {0}）。'
            }
        }
        elseif ($culture -match '^zh') {
            $t = @{
                Downloading = '下载最新版本'; Verifying = '验证文件完整性'; Installing = '安装'
                Done = '安装完成'; Launched = 'Agent Speed 已启动，请查看任务栏。'
                UpdateHint = '之后重新运行同一行命令即可更新。'; Uninstalling = '卸载 Agent Speed'
                Uninstalled = '已卸载'; NotInstalled = '没有找到已安装的 Agent Speed'; OldWindows = '需要 Windows 10 (1809) 或更高版本'
                DownloadFailed = '下载失败'; NoChecksum = '找不到校验码'; BadChecksum = '校验码不匹配，已中止安装'
                BadArchive = '安装包内容不正确'; Retrying = '下载不完整，重新下载…'
                MirrorHint = '若 GitHub 在你的网络很慢，可将 AGENT_SPEED_DOWNLOAD_BASE 设为发布文件的镜像地址。'
                HelperPreparing = '准备流量辅助服务'
                HelperMissing = '这个版本没有附带流量辅助服务，无法记录各 agent 的流量。'
                HelperApprove = '接下来 Windows 会询问是否允许“Agent Speed Traffic Helper”更改这台电脑。发布者会显示为“未知”，因为当前版本没有数字签名。'
                HelperWaiting = '等待管理员确认…'
                HelperInstalled = '流量辅助服务已安装，开始记录各 agent 的流量。'
                HelperCancelled = '已取消，没有更改任何设置。'
                HelperRefused = '流量辅助服务拒绝安装：安全检查未通过（例如安装文件夹可被其他用户写入）。'
                HelperFailed = '无法安装流量辅助服务（代码 {0}）。'
                HelperRemoving = '卸载流量辅助服务'
                HelperRemoveApprove = '请在 Windows 询问是否允许“Agent Speed Traffic Helper”更改这台电脑时同意，以卸载它（发布者会显示为“未知”）。'
                HelperRemoved = '已卸载流量辅助服务。'
                HelperRemoveCancelled = '已取消，流量辅助服务仍保持安装。'
                HelperRemoveFailed = '无法卸载流量辅助服务（代码 {0}）。'
            }
        }
        elseif ($culture -match '^ja') {
            $t = @{
                Downloading = '最新バージョンをダウンロード'; Verifying = 'ファイルの整合性を確認'; Installing = 'インストール'
                Done = 'インストール完了'; Launched = 'Agent Speed を起動しました。タスクバーを確認してください。'
                UpdateHint = '同じコマンドを再実行すると更新できます。'; Uninstalling = 'Agent Speed をアンインストール'
                Uninstalled = '削除しました'; NotInstalled = 'インストールされた Agent Speed が見つかりません'
                OldWindows = 'Windows 10 (1809) 以降が必要です'; DownloadFailed = 'ダウンロードに失敗しました'
                NoChecksum = 'チェックサムが見つかりません'; BadChecksum = 'チェックサムが一致しないため中止しました'
                BadArchive = 'パッケージの内容が正しくありません'; Retrying = 'ダウンロードが不完全なため再試行します…'
                MirrorHint = 'GitHub が遅い場合は AGENT_SPEED_DOWNLOAD_BASE にリリースファイルのミラーを設定してください。'
                HelperPreparing = '通信量ヘルパーを準備'
                HelperMissing = 'このビルドには通信量ヘルパーが含まれていないため、agent ごとの通信量は計測できません。'
                HelperApprove = '続いて Windows が「Agent Speed Traffic Helper」による変更を許可するか確認します。現在のリリースにはコード署名がないため、発行元は「不明」と表示されます。'
                HelperWaiting = '管理者の承認を待っています…'
                HelperInstalled = '通信量ヘルパーをインストールしました。agent ごとの通信量の計測を始めます。'
                HelperCancelled = 'キャンセルしました。何も変更されていません。'
                HelperRefused = '安全確認に失敗したため、通信量ヘルパーはインストールを拒否しました（例：インストール先フォルダーに他のユーザーが書き込める）。'
                HelperFailed = '通信量ヘルパーをインストールできませんでした（コード {0}）。'
                HelperRemoving = '通信量ヘルパーを削除'
                HelperRemoveApprove = 'Windows が「Agent Speed Traffic Helper」による変更の許可を求めたら、承認して削除してください（発行元は「不明」と表示されます）。'
                HelperRemoved = '通信量ヘルパーを削除しました。'
                HelperRemoveCancelled = 'キャンセルしました。通信量ヘルパーはインストールされたままです。'
                HelperRemoveFailed = '通信量ヘルパーを削除できませんでした（コード {0}）。'
            }
        }
        return $t
    }

    $T = Get-Text

    function Write-Title { Write-Host ''; Write-Host '◉ Agent Speed' -ForegroundColor Cyan; Write-Host '' }
    function Write-Step([string]$Text) { Write-Host '  › ' -ForegroundColor Cyan -NoNewline; Write-Host $Text }
    function Write-Done([string]$Text) { Write-Host ''; Write-Host '  ✓ ' -ForegroundColor Green -NoNewline; Write-Host $Text }
    function Write-Hint([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
    function Write-Ok([string]$Text) { Write-Host '  ✓ ' -ForegroundColor Green -NoNewline; Write-Host $Text }
    function Write-Warn([string]$Text) { Write-Host '  ! ' -ForegroundColor Yellow -NoNewline; Write-Host $Text }
    function Stop-WithError([string]$Text) {
        Write-Host ''
        Write-Host "  ✗ $Text" -ForegroundColor Red
        Write-Host ''
        throw $Text
    }

    # The traffic helper (spec/WINDOWS-SERVICE.md 5). This text is also pasted verbatim into the generated uninstall.ps1,
    # so it depends on nothing else in this script. The elevated helper always runs from a fresh private copy extracted
    # from AgentSpeed.exe: it is NativeAOT and loads only System32 DLLs, and a running image cannot delete itself.
    $HelperCode = @'
function Get-AgentSpeedUserSid {
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($sid -cmatch '^S-1-[0-9]+(-[0-9]+)+$') { return $sid }
    return $null
}

# A new folder %LOCALAPPDATA%\AgentSpeed\helper-staging\<random>, created with a protected DACL that admits only this
# user, SYSTEM and Administrators (never %TEMP%).
function New-AgentSpeedHelperFolder([string]$Sid) {
    $parent = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AgentSpeed\helper-staging'
    [void][System.IO.Directory]::CreateDirectory($parent)
    $bytes = New-Object byte[] 16
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $folder = Join-Path $parent (-join ($bytes | ForEach-Object { $_.ToString('x2') }))
    if (Test-Path -LiteralPath $folder) { throw 'The staging folder already exists.' }
    $security = New-Object System.Security.AccessControl.DirectorySecurity
    $security.SetSecurityDescriptorSddlForm("O:${Sid}D:P(A;OICI;FA;;;$Sid)(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)")
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        [System.IO.FileSystemAclExtensions]::Create((New-Object System.IO.DirectoryInfo $folder), $security)
    }
    else {
        [void][System.IO.Directory]::CreateDirectory($folder, $security)
    }
    return $folder
}

# Writes the helper embedded in AgentSpeed.exe and the app's own SHA-256 of it into $Folder; returns that hash, or $null
# when the build carries no helper (or anything went wrong).
function Export-AgentSpeedHelper([string]$AppExe, [string]$Folder) {
    if (-not $AppExe -or -not (Test-Path -LiteralPath $AppExe -PathType Leaf)) { return $null }
    try {
        $extract = Start-Process -FilePath $AppExe -ArgumentList '--extract-helper', "`"$Folder`"" -Wait -PassThru
        if ($extract.ExitCode -ne 0) { return $null }
    }
    catch { return $null }
    $hashFile = Join-Path $Folder 'AgentSpeedTrafficHelper.exe.sha256'
    if (-not (Test-Path -LiteralPath $hashFile -PathType Leaf)) { return $null }
    $hash = ([System.IO.File]::ReadAllText($hashFile)).Trim()
    if ($hash -cnotmatch '^[0-9a-f]{64}$') { return $null }
    return $hash
}

# The elevated run's exit code (0 ok, 1 refused, 2 failed); $null when the Windows prompt was cancelled; -1 when it could
# not start. Process.Start with "runas" is the same ShellExecuteEx call as Start-Process -Verb RunAs, but it reports a
# cancelled prompt as ERROR_CANCELLED (1223) instead of only a localized message.
function Invoke-AgentSpeedElevated([string]$FilePath, [string]$Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath
    $info.Arguments = $Arguments
    $info.Verb = 'runas'
    $info.UseShellExecute = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $info.WorkingDirectory = [Environment]::SystemDirectory
    $process = $null
    try { $process = [System.Diagnostics.Process]::Start($info) }
    catch {
        $e = $_.Exception
        while ($e) {
            if ($e -is [System.ComponentModel.Win32Exception] -and $e.NativeErrorCode -eq 1223) { return $null }
            $e = $e.InnerException
        }
        return -1
    }
    if (-not $process) { return -1 }
    try {
        $process.WaitForExit()
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

# Holds the staged helper open with read sharing only from the hash check until the elevated run has exited, so it
# cannot be changed or replaced in between. Like Invoke-AgentSpeedElevated, plus -2 when it does not match $Sha256.
function Invoke-AgentSpeedHelper([string]$FilePath, [string]$Sha256, [string]$Arguments) {
    $hold = [System.IO.File]::Open($FilePath, 'Open', 'Read', 'Read')
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = -join ($sha.ComputeHash($hold) | ForEach-Object { $_.ToString('x2') }) } finally { $sha.Dispose() }
        if ($actual -cne $Sha256) { return -2 }
        return Invoke-AgentSpeedElevated $FilePath $Arguments
    }
    finally { $hold.Dispose() }
}

# Removes this user from the helper's allowed users; the helper deletes itself when none remain. Does nothing (and so
# shows no prompt) unless the helper is installed and this user is among its allowed users.
function Remove-AgentSpeedTrafficHelper([hashtable]$Text, [string]$AppExe) {
    $sid = Get-AgentSpeedUserSid
    if (-not $sid) { return }
    $allowed = $null
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $key = $base.OpenSubKey('SOFTWARE\AgentSpeed\TrafficHelper')
            if ($key) {
                try { $allowed = $key.GetValue('AllowedUsers') } finally { $key.Dispose() }
            }
        }
        finally { $base.Dispose() }
    }
    catch { return }
    if (-not (@($allowed) | Where-Object { $_ -is [string] -and $_ -ieq $sid })) { return }

    Write-Host '  › ' -ForegroundColor Cyan -NoNewline; Write-Host $Text.HelperRemoving
    $folder = $null
    try {
        $folder = New-AgentSpeedHelperFolder $sid
        $helper = Join-Path $folder 'AgentSpeedTrafficHelper.exe'
        $hash = Export-AgentSpeedHelper $AppExe $folder
        if (-not $hash) {
            # A build without an embedded helper: run a private copy of the installed (administrator-owned) one.
            $programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
            $installed = Join-Path $programFiles 'Agent Speed\Traffic Helper\AgentSpeedTrafficHelper.exe'
            if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) { return }
            Copy-Item -LiteralPath $installed -Destination $helper -ErrorAction Stop
            $hash = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        Write-Host "    $($Text.HelperRemoveApprove)" -ForegroundColor DarkGray
        $code = Invoke-AgentSpeedHelper $helper $hash "remove --user $sid"
        if ($null -eq $code) { Write-Host "    $($Text.HelperRemoveCancelled)" -ForegroundColor DarkGray }
        elseif ($code -eq 0) { Write-Host '  ✓ ' -ForegroundColor Green -NoNewline; Write-Host $Text.HelperRemoved }
        else { Write-Host '  ! ' -ForegroundColor Yellow -NoNewline; Write-Host ($Text.HelperRemoveFailed -f $code) }
    }
    catch {
        Write-Host '  ! ' -ForegroundColor Yellow -NoNewline; Write-Host ($Text.HelperRemoveFailed -f -1)
    }
    finally {
        if ($folder) { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
'@
    . ([scriptblock]::Create($HelperCode))

    # A single-quoted PowerShell literal; curly single quotes also end such strings, so they are doubled too.
    function ConvertTo-Literal([string]$Value) { "'" + ($Value -replace "['‘’‚‛]", '$0$0') + "'" }

    # Never fails the app install: every problem is reported and the install continues without the helper.
    function Install-TrafficHelper([string]$AppExe) {
        Write-Step $T.HelperPreparing
        $folder = $null
        try {
            $sid = Get-AgentSpeedUserSid
            if (-not $sid) {
                Write-Warn ($T.HelperFailed -f -1)
                return
            }

            $folder = New-AgentSpeedHelperFolder $sid
            $hash = Export-AgentSpeedHelper $AppExe $folder
            if (-not $hash) {
                Write-Hint $T.HelperMissing
                return
            }

            $helper = Join-Path $folder 'AgentSpeedTrafficHelper.exe'
            Write-Hint $T.HelperApprove
            Write-Hint $T.HelperWaiting
            $code = Invoke-AgentSpeedHelper $helper $hash "install --user $sid --sha256 $hash"
            if ($null -eq $code) { Write-Hint $T.HelperCancelled }
            elseif ($code -eq 0) { Write-Ok $T.HelperInstalled }
            elseif ($code -eq 1) { Write-Warn $T.HelperRefused }
            else { Write-Warn ($T.HelperFailed -f $code) }
        }
        catch {
            Write-Warn ($T.HelperFailed -f -1)
        }
        finally {
            if ($folder) { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }

    function Get-Architecture {
        $arch = $env:PROCESSOR_ARCHITEW6432
        if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
        if ($arch -eq 'ARM64') { return 'arm64' }
        return 'x64'
    }

    function Stop-AgentSpeed {
        $running = Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue
        if (-not $running) { return }
        foreach ($process in $running) {
            try { [void]$process.CloseMainWindow() } catch { }
        }
        $deadline = (Get-Date).AddSeconds(4)
        while ((Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }

    function Invoke-Download([string]$Url, [string]$Path, [switch]$ShowProgress) {
        Add-Type -AssemblyName System.Net.Http
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $client = New-Object System.Net.Http.HttpClient
            $client.Timeout = [TimeSpan]::FromMinutes(30)
            $client.DefaultRequestHeaders.UserAgent.ParseAdd('AgentSpeed-Installer')
            try {
                $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode)" }
                $total = $response.Content.Headers.ContentLength
                $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $target = [IO.File]::Create($Path)
                try {
                    $buffer = New-Object byte[] 262144
                    $done = [long]0
                    $shown = -1
                    while ($true) {
                        $read = $source.ReadAsync($buffer, 0, $buffer.Length)
                        if (-not $read.Wait([TimeSpan]::FromSeconds(60))) { throw 'stalled' }
                        if ($read.Result -le 0) { break }
                        $target.Write($buffer, 0, $read.Result)
                        $done += $read.Result
                        if ($ShowProgress -and $total) {
                            $percent = [int](100 * $done / $total)
                            if ($percent -ne $shown) {
                                Write-Host -NoNewline ("`r    {0,3}%  {1:N1} / {2:N1} MB" -f $percent, ($done / 1MB), ($total / 1MB)) -ForegroundColor DarkGray
                                $shown = $percent
                            }
                        }
                    }
                    if ($total -and $done -ne $total) { throw 'incomplete download' }
                }
                finally {
                    $target.Dispose()
                    $source.Dispose()
                    if ($ShowProgress -and $total) { Write-Host '' }
                }
                return
            }
            catch {
                if ($attempt -eq 3) { Stop-WithError "$($T.DownloadFailed): $Url`n    $($T.MirrorHint)" }
                Write-Hint $T.Retrying
                Start-Sleep -Seconds (2 * $attempt)
            }
            finally {
                $client.Dispose()
            }
        }
    }

    function Remove-AgentSpeed {
        Write-Title
        Write-Step $T.Uninstalling
        Stop-AgentSpeed
        Remove-AgentSpeedTrafficHelper $T (Join-Path $InstallDir $ExeName)
        $found = Test-Path $InstallDir
        Remove-ItemProperty -Path $RunKey -Name 'AgentSpeed' -ErrorAction SilentlyContinue
        Remove-Item -Path $StartMenuLink -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue
        if ($found) {
            Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($Purge) {
            Remove-Item -Path $SettingsDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $LogsDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($found) { Write-Done $T.Uninstalled } else { Write-Done $T.NotInstalled }
        Write-Host ''
    }

    function Install-AgentSpeed {
        if ([Environment]::OSVersion.Version.Build -lt 17763) { Stop-WithError $T.OldWindows }
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        }
        catch { }

        $arch = Get-Architecture
        $asset = "AgentSpeed-Windows-$arch.zip"
        $base = $env:AGENT_SPEED_DOWNLOAD_BASE
        if (-not $base) {
            if ($Version) {
                if ($Version -notmatch '^v') { $Version = "v$Version" }
                if ($Version -notmatch '^v[0-9A-Za-z.\-]+$') { throw 'Invalid -Version' }
                $base = "https://github.com/$Repo/releases/download/$Version"
            }
            else {
                $base = "https://github.com/$Repo/releases/latest/download"
            }
        }

        $temp = Join-Path ([IO.Path]::GetTempPath()) ("agent-speed-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            Write-Title
            Write-Step $T.Downloading
            $zip = Join-Path $temp $asset
            $sums = Join-Path $temp 'SHA256SUMS.txt'
            Invoke-Download "$base/SHA256SUMS.txt" $sums
            $expected = $null
            foreach ($line in Get-Content -Path $sums) {
                $parts = $line.Trim() -split '\s+', 2
                if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $asset) { $expected = $parts[0].ToLowerInvariant(); break }
            }
            if (-not $expected) { Stop-WithError $T.NoChecksum }

            $verifiedOnce = $false
            for ($attempt = 1; ; $attempt++) {
                Invoke-Download "$base/$asset" $zip -ShowProgress
                if (-not $verifiedOnce) { Write-Step $T.Verifying; $verifiedOnce = $true }
                $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($expected -eq $actual) { break }
                if ($attempt -ge 3) { Stop-WithError "$($T.BadChecksum)`n    $($T.MirrorHint)" }
                Write-Hint $T.Retrying
            }

            $unpacked = Join-Path $temp 'unpacked'
            Expand-Archive -Path $zip -DestinationPath $unpacked -Force
            $exe = Join-Path $unpacked $ExeName
            if (-not (Test-Path $exe)) { Stop-WithError $T.BadArchive }

            Write-Step $T.Installing
            Stop-AgentSpeed
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            Copy-Item -Path (Join-Path $unpacked '*') -Destination $InstallDir -Recurse -Force
            $target = Join-Path $InstallDir $ExeName
            Get-ChildItem -Path $InstallDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

            $uninstaller = Join-Path $InstallDir 'uninstall.ps1'
            $helperText = @(foreach ($name in 'HelperRemoving', 'HelperRemoveApprove', 'HelperRemoved', 'HelperRemoveCancelled', 'HelperRemoveFailed') {
                    "$name = $(ConvertTo-Literal $T[$name])"
                }) -join '; '
            $uninstallBody = @"
`$ErrorActionPreference = 'SilentlyContinue'
Get-Process -Name 'AgentSpeed' | Stop-Process -Force
Start-Sleep -Milliseconds 500
$HelperCode
Remove-AgentSpeedTrafficHelper @{ $helperText } $(ConvertTo-Literal $target)
Remove-ItemProperty -Path '$RunKey' -Name 'AgentSpeed'
Remove-Item -Path '$StartMenuLink' -Force
Remove-Item -Path '$UninstallKey' -Recurse -Force
Set-Location `$env:TEMP
Remove-Item -Path '$InstallDir' -Recurse -Force
"@
            # With a BOM even under PowerShell 7: the uninstall entry runs Windows PowerShell 5.1, which reads BOM-less
            # files in the ANSI code page and would garble the localized texts.
            [IO.File]::WriteAllText($uninstaller, $uninstallBody, (New-Object System.Text.UTF8Encoding $true))

            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($StartMenuLink)
            $link.TargetPath = $target
            $link.WorkingDirectory = $InstallDir
            $link.IconLocation = "$target,0"
            $link.Description = $AppName
            $link.Save()

            $fileVersion = (Get-Item $target).VersionInfo.ProductVersion
            if (-not $fileVersion) { $fileVersion = '1.0.0' }
            $fileVersion = ($fileVersion -split '\+')[0]
            New-Item -Path $UninstallKey -Force | Out-Null
            $props = @{
                DisplayName = $AppName
                DisplayVersion = $fileVersion
                Publisher = 'zoefix'
                DisplayIcon = "$target,0"
                InstallLocation = $InstallDir
                URLInfoAbout = "https://github.com/$Repo"
                UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
                NoModify = 1
                NoRepair = 1
            }
            foreach ($key in $props.Keys) {
                $type = if ($props[$key] -is [int]) { 'DWord' } else { 'String' }
                New-ItemProperty -Path $UninstallKey -Name $key -Value $props[$key] -PropertyType $type -Force | Out-Null
            }

            if (-not $NoAutostart) {
                New-ItemProperty -Path $RunKey -Name 'AgentSpeed' -Value "`"$target`"" -PropertyType String -Force | Out-Null
            }

            Write-Done "$($T.Done) $fileVersion → $InstallDir"
            if ($TrafficHelper) { Install-TrafficHelper $target }
            if (-not $NoLaunch) {
                Start-Process -FilePath $target -WorkingDirectory $InstallDir
                Write-Hint $T.Launched
            }
            Write-Hint $T.UpdateHint
            Write-Host ''
        }
        finally {
            Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if ($Uninstall) { Remove-AgentSpeed } else { Install-AgentSpeed }
}
