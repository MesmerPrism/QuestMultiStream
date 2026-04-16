using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly QuestCastSessionManager _sessionManager;
    private readonly CastControlWindowManager _castControlWindowManager;
    private readonly DispatcherTimer _pollTimer;
    private DependencySnapshot _dependencies;
    private string _statusBanner = "Checking tooling and devices.";
    private string _dependencyHeadline = "Waiting for detection.";
    private string _dependencyDetail = "scrcpy and adb must be available before casting starts.";
    private string _scrcpyPath = "Not detected";
    private string _adbPath = "Not detected";
    private string _repoRoot = "Not detected";
    private string _maxSizeText = "1344";
    private string _videoBitRateText = "20";
    private string _maxFpsText = "30";
    private bool _enableAudio;
    private bool _enableControl;
    private bool _stayAwake = true;
    private WindowLayoutMode _selectedLayoutMode = WindowLayoutMode.Grid;
    private bool _initialized;

    public MainWindowViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _sessionManager = new QuestCastSessionManager();
        _castControlWindowManager = new CastControlWindowManager(
            StopSessionFromWindowAsync,
            ResizeSessionFromWindowAsync);
        _dependencies = _sessionManager.GetDependencySnapshot();

        LayoutModes = Enum.GetValues<WindowLayoutMode>();
        Devices = new ObservableCollection<DeviceRowViewModel>();
        Sessions = new ObservableCollection<SessionCardViewModel>();
        Logs = new ObservableCollection<LogEntryViewModel>();

        RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync, onError: HandleErrorAsync);
        ArrangeWindowsCommand = new AsyncRelayCommand(ArrangeWindowsAsync, onError: HandleErrorAsync);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, onError: HandleErrorAsync);

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pollTimer.Tick += OnPollTimerTick;
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }

    public ObservableCollection<SessionCardViewModel> Sessions { get; }

    public ObservableCollection<LogEntryViewModel> Logs { get; }

    public Array LayoutModes { get; }

    public string StatusBanner
    {
        get => _statusBanner;
        private set => SetProperty(ref _statusBanner, value);
    }

    public string DependencyHeadline
    {
        get => _dependencyHeadline;
        private set => SetProperty(ref _dependencyHeadline, value);
    }

    public string DependencyDetail
    {
        get => _dependencyDetail;
        private set => SetProperty(ref _dependencyDetail, value);
    }

    public string ScrcpyPath
    {
        get => _scrcpyPath;
        private set => SetProperty(ref _scrcpyPath, value);
    }

    public string AdbPath
    {
        get => _adbPath;
        private set => SetProperty(ref _adbPath, value);
    }

    public string RepoRoot
    {
        get => _repoRoot;
        private set => SetProperty(ref _repoRoot, value);
    }

    public string MaxSizeText
    {
        get => _maxSizeText;
        set
        {
            if (SetProperty(ref _maxSizeText, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public string VideoBitRateText
    {
        get => _videoBitRateText;
        set
        {
            if (SetProperty(ref _videoBitRateText, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public string MaxFpsText
    {
        get => _maxFpsText;
        set
        {
            if (SetProperty(ref _maxFpsText, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public bool EnableAudio
    {
        get => _enableAudio;
        set => SetProperty(ref _enableAudio, value);
    }

    public bool EnableControl
    {
        get => _enableControl;
        set => SetProperty(ref _enableControl, value);
    }

    public bool StayAwake
    {
        get => _stayAwake;
        set => SetProperty(ref _stayAwake, value);
    }

    public string CaptureTransportHeadline
        => "Custom cast wrapper";

    public string CaptureTransportDetail
        => "Each Quest row lists the real display and camera feeds that scrcpy reports. Live casts use a custom wrapper overlay that tracks one borderless scrcpy window so the controls and resize behavior stay locked to the same surface.";

    public string DisplayGuideText
        => "Refresh Devices reloads the feed list from scrcpy. Display 0 is the reliable stereo mirror, cameras 1, 50, and 51 are valid on this headset, and Display 3 plus the tiny surfaces stay marked as experimental.";

    public string ProfileSummary
        => $"{ParsePositiveInt(MaxSizeText, 1344, 640, 4096)} px max · {ParsePositiveInt(VideoBitRateText, 20, 2, 200)} Mbps · {ParsePositiveInt(MaxFpsText, 30, 15, 144)} fps";

    public WindowLayoutMode SelectedLayoutMode
    {
        get => _selectedLayoutMode;
        set
        {
            if (SetProperty(ref _selectedLayoutMode, value))
            {
                RefreshStatusBanner();
            }
        }
    }

    public AsyncRelayCommand RefreshDevicesCommand { get; }

    public AsyncRelayCommand ArrangeWindowsCommand { get; }

    public AsyncRelayCommand StopAllCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _sessionManager.LogRaised += OnLogRaised;
        await RefreshDevicesAsync();
        _pollTimer.Start();
    }

    public void Shutdown()
    {
        _pollTimer.Stop();
        _sessionManager.LogRaised -= OnLogRaised;
        _castControlWindowManager.CloseAll();
        _sessionManager.Dispose();
    }

    private async Task RefreshDevicesAsync()
    {
        _dependencies = _sessionManager.GetDependencySnapshot();
        UpdateDependencySnapshot(_dependencies);

        var previousSelections = new Dictionary<string, (ScrcpyCaptureTargetKind? Kind, string? Id)>(StringComparer.OrdinalIgnoreCase);
        var previousProximityModes = new Dictionary<string, QuestProximityMode>(StringComparer.OrdinalIgnoreCase);
        var previousPinnedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await RunOnUiAsync(() =>
        {
            foreach (var device in Devices)
            {
                previousSelections[device.Serial] = (device.SelectedCaptureTargetKind, device.SelectedCaptureTargetId);
                previousProximityModes[device.Serial] = device.ProximityMode;
                previousPinnedStates[device.Serial] = device.IsCastWindowPinned;
            }
        });

        var devices = _dependencies.HasAdb
            ? await _sessionManager.GetDevicesAsync()
            : Array.Empty<QuestDevice>();
        var captureTargetsBySerial = new Dictionary<string, IReadOnlyList<ScrcpyCaptureTarget>>(StringComparer.OrdinalIgnoreCase);
        var proximityModesBySerial = new Dictionary<string, QuestProximityMode>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices.Where(device => device.IsQuest && device.IsAvailable))
        {
            captureTargetsBySerial[device.Serial] = await _sessionManager.GetCaptureTargetsAsync(device);
            var proximityMode = await _sessionManager.GetProximityModeAsync(device);
            proximityModesBySerial[device.Serial] = proximityMode != QuestProximityMode.Unknown
                ? proximityMode
                : previousProximityModes.GetValueOrDefault(device.Serial, QuestProximityMode.Unknown);
        }

        await RunOnUiAsync(() =>
        {
            Devices.Clear();
            var sessionsBySerial = _sessionManager.GetSessions().ToDictionary(session => session.Device.Serial, StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices.Where(device => device.IsQuest))
            {
                sessionsBySerial.TryGetValue(device.Serial, out var session);
                var row = new DeviceRowViewModel(
                    device,
                    StartCastAsync,
                    StopCastAsync,
                    EnableWirelessAsync,
                    WakeAsync,
                    SwitchCaptureTargetAsync,
                    SetProximityModeAsync,
                    HandleErrorAsync);

                if (previousSelections.TryGetValue(device.Serial, out var preferredTarget))
                {
                    row.SetCaptureTargets(
                        captureTargetsBySerial.GetValueOrDefault(device.Serial),
                        preferredTarget.Kind,
                        preferredTarget.Id);
                }
                else
                {
                    row.SetCaptureTargets(captureTargetsBySerial.GetValueOrDefault(device.Serial));
                }

                row.SetProximityMode(proximityModesBySerial.GetValueOrDefault(device.Serial, QuestProximityMode.Unknown));
                row.SetCastWindowPinned(previousPinnedStates.GetValueOrDefault(device.Serial, false));
                row.Refresh(device, session);
                Devices.Add(row);
            }

            SyncSessions();
            RefreshStatusBanner();
        });

        if (devices.Count == 0 && _dependencies.HasAdb)
        {
            AppendLog(new OperatorLogEntry(DateTimeOffset.Now, "warning", "No Quest devices detected.", "Attach a headset over USB or Wi-Fi ADB."));
        }
    }

    private async Task StartCastAsync(DeviceRowViewModel row)
    {
        await _sessionManager.StartSessionAsync(row.Device, BuildLaunchProfile(row.SelectedCaptureTarget));
        await RefreshUiFromSessionsAsync();
    }

    private async Task StopCastAsync(DeviceRowViewModel row)
    {
        await _sessionManager.StopSessionAsync(row.Serial);
        await RefreshUiFromSessionsAsync();
    }

    private async Task SwitchCaptureTargetAsync(DeviceRowViewModel row, ScrcpyCaptureTarget captureTarget)
    {
        var existingSession = _sessionManager
            .GetSessions()
            .FirstOrDefault(session => string.Equals(session.Device.Serial, row.Serial, StringComparison.OrdinalIgnoreCase));
        if (existingSession is null || !existingSession.IsActive)
        {
            return;
        }

        var previousTarget = FindCaptureTarget(row, existingSession.LaunchProfile);

        try
        {
            await _sessionManager.RestartSessionAsync(row.Device, BuildLaunchProfile(captureTarget));
        }
        catch
        {
            if (previousTarget is not null)
            {
                row.SetCaptureTargets(row.CaptureTargets.ToArray(), previousTarget.Kind, previousTarget.Id);

                try
                {
                    await _sessionManager.StartSessionAsync(row.Device, BuildLaunchProfile(previousTarget));
                }
                catch
                {
                }
            }

            throw;
        }

        await RefreshUiFromSessionsAsync();
    }

    private async Task EnableWirelessAsync(DeviceRowViewModel row)
    {
        await _sessionManager.EnableWirelessAsync(row.Serial);
        await RefreshDevicesAsync();
    }

    private async Task WakeAsync(DeviceRowViewModel row)
    {
        await _sessionManager.WakeAsync(row.Serial);
    }

    private async Task SetProximityModeAsync(DeviceRowViewModel row, QuestProximityMode mode)
    {
        await _sessionManager.SetProximityModeAsync(row.Serial, mode);
        await RefreshUiFromSessionsAsync();
    }

    private async Task ArrangeWindowsAsync()
    {
        SyncSessions();
        var workArea = SystemParameters.WorkArea;
        var summary = _sessionManager.ArrangeSessions(
            new WindowLayoutBounds(
                (int)workArea.Left,
                (int)workArea.Top,
                (int)workArea.Width,
                (int)workArea.Height),
            SelectedLayoutMode);
        AppendLog(new OperatorLogEntry(DateTimeOffset.Now, "info", summary));
        await RefreshUiFromSessionsAsync();
    }

    private async Task StopAllAsync()
    {
        await _sessionManager.StopAllAsync();
        await RefreshUiFromSessionsAsync();
    }

    private Task HandleErrorAsync(Exception ex)
    {
        AppendLog(new OperatorLogEntry(DateTimeOffset.Now, "error", ex.Message, ex.GetType().Name));
        return Task.CompletedTask;
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        _sessionManager.RefreshSessions();
        _ = RefreshUiFromSessionsAsync();
    }

    private void OnLogRaised(object? sender, OperatorLogEntry entry)
        => _ = RunOnUiAsync(() => AppendLog(entry));

    private async Task RefreshUiFromSessionsAsync()
    {
        await RunOnUiAsync(() =>
        {
            SyncSessions();
            RefreshStatusBanner();
        });
    }

    private void SyncSessions()
    {
        _sessionManager.RefreshSessions();
        var sessions = _sessionManager.GetSessions();
        var sessionsBySerial = sessions.ToDictionary(session => session.Device.Serial, StringComparer.OrdinalIgnoreCase);

        foreach (var device in Devices)
        {
            sessionsBySerial.TryGetValue(device.Serial, out var session);
            device.Refresh(device.Device, session);
        }

        Sessions.Clear();
        foreach (var session in sessions.OrderByDescending(session => session.StartedAt))
        {
            Sessions.Add(new SessionCardViewModel(session));
        }

        _castControlWindowManager.Sync(sessions, Devices);
    }

    private void UpdateDependencySnapshot(DependencySnapshot snapshot)
    {
        DependencyHeadline = snapshot.IsReady
            ? "Tooling ready for casting."
            : "Setup needed before streaming.";
        DependencyDetail = snapshot.Guidance;
        ScrcpyPath = snapshot.ScrcpyPath ?? "Not detected";
        AdbPath = snapshot.AdbPath ?? "Not detected";
        RepoRoot = snapshot.RepoRoot ?? "Published app / repo root not found";
    }

    private void RefreshStatusBanner()
    {
        var activeSessions = Sessions.Count(session => string.Equals(session.StateText, nameof(QuestCastSessionState.Running), StringComparison.OrdinalIgnoreCase));
        StatusBanner = $"{Devices.Count} Quest device(s) visible · {activeSessions} live cast(s) · Layout {SelectedLayoutMode}";
    }

    private void AppendLog(OperatorLogEntry entry)
    {
        Logs.Insert(0, new LogEntryViewModel(entry));
        while (Logs.Count > 250)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private ScrcpyLaunchProfile BuildLaunchProfile(
        ScrcpyCaptureTarget? captureTarget,
        WindowLayoutBounds? initialWindowBounds = null)
    {
        captureTarget ??= ScrcpyCaptureTarget.DefaultDisplay;
        int? displayId = captureTarget.Kind == ScrcpyCaptureTargetKind.Display
            ? captureTarget.LaunchDisplayId ??
              (int.TryParse(captureTarget.Id, out var parsedDisplayId) ? parsedDisplayId : null)
            : null;
        var cameraId = captureTarget.Kind == ScrcpyCaptureTargetKind.Camera
            ? captureTarget.LaunchCameraId ?? captureTarget.Id
            : null;

        return new()
        {
            CaptureTargetId = captureTarget.Id,
            VideoSource = captureTarget.Kind,
            DisplayId = displayId,
            CameraId = cameraId,
            Crop = captureTarget.Crop,
            Angle = captureTarget.Angle,
            CaptureTargetLabel = captureTarget.SelectionText,
            MaxSize = ParsePositiveInt(MaxSizeText, 1344, 640, 4096),
            MaxFps = ParsePositiveInt(MaxFpsText, 30, 15, 144),
            VideoBitRateMbps = ParsePositiveInt(VideoBitRateText, 20, 2, 200),
            EnableAudio = EnableAudio,
            EnableControl = EnableControl,
            AlwaysOnTop = false,
            Borderless = true,
            InitialWindowBounds = initialWindowBounds,
            StayAwake = StayAwake
        };
    }

    private static ScrcpyCaptureTarget? FindCaptureTarget(DeviceRowViewModel row, ScrcpyLaunchProfile launchProfile)
    {
        if (!string.IsNullOrWhiteSpace(launchProfile.CaptureTargetId))
        {
            var exactMatch = row.CaptureTargets.FirstOrDefault(target =>
                string.Equals(target.Id, launchProfile.CaptureTargetId, StringComparison.Ordinal));
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        var expectedId = launchProfile.VideoSource == ScrcpyCaptureTargetKind.Camera
            ? launchProfile.CameraId
            : launchProfile.DisplayId?.ToString();

        return row.CaptureTargets.FirstOrDefault(target =>
            target.Kind == launchProfile.VideoSource &&
            string.Equals(target.Id, expectedId, StringComparison.Ordinal));
    }

    private static int ParsePositiveInt(string? value, int fallback, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, minimum, maximum);
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    private async Task StopSessionFromWindowAsync(string serial)
    {
        await _sessionManager.StopSessionAsync(serial);
        await RefreshUiFromSessionsAsync();
    }

    private async Task ResizeSessionFromWindowAsync(string serial, WindowLayoutBounds bounds)
    {
        var row = Devices.FirstOrDefault(device =>
            string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        var existingSession = _sessionManager
            .GetSessions()
            .FirstOrDefault(session =>
                string.Equals(session.Device.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (existingSession is null || !existingSession.IsActive)
        {
            return;
        }

        var selectedTarget = row.SelectedCaptureTarget ?? FindCaptureTarget(row, existingSession.LaunchProfile);
        await _sessionManager.RestartSessionAsync(row.Device, BuildLaunchProfile(selectedTarget, bounds));
        await RefreshUiFromSessionsAsync();
    }
}
