using System.Collections.ObjectModel;
using System.Windows.Media;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.App.ViewModels;

public sealed class DeviceRowViewModel : ObservableObject
{
    private readonly Func<DeviceRowViewModel, ScrcpyCaptureTarget, Task> _switchCaptureTargetAsync;
    private readonly Func<DeviceRowViewModel, QuestProximityMode, Task> _setProximityModeAsync;
    private readonly Func<Exception, Task> _handleErrorAsync;
    private QuestDevice _device;
    private QuestCastSession? _session;
    private Brush _statusBrush = UiPalette.Muted;
    private string _deviceSummary;
    private string _sessionStateText = "Idle";
    private string _sessionDetailText = "No active scrcpy session.";
    private string _endpointText;
    private ScrcpyCaptureTarget? _selectedCaptureTarget;
    private bool _suppressCaptureTargetChange;
    private bool _isApplyingCaptureTarget;
    private QuestProximityMode _proximityMode = QuestProximityMode.Unknown;
    private bool _isCastWindowPinned;

    public DeviceRowViewModel(
        QuestDevice device,
        Func<DeviceRowViewModel, Task> startAsync,
        Func<DeviceRowViewModel, Task> stopAsync,
        Func<DeviceRowViewModel, Task> enableWirelessAsync,
        Func<DeviceRowViewModel, Task> wakeAsync,
        Func<DeviceRowViewModel, ScrcpyCaptureTarget, Task> switchCaptureTargetAsync,
        Func<DeviceRowViewModel, QuestProximityMode, Task> setProximityModeAsync,
        Func<Exception, Task> handleErrorAsync)
    {
        _switchCaptureTargetAsync = switchCaptureTargetAsync ?? throw new ArgumentNullException(nameof(switchCaptureTargetAsync));
        _setProximityModeAsync = setProximityModeAsync ?? throw new ArgumentNullException(nameof(setProximityModeAsync));
        _handleErrorAsync = handleErrorAsync ?? throw new ArgumentNullException(nameof(handleErrorAsync));
        _device = device;
        _deviceSummary = string.Empty;
        _endpointText = string.Empty;
        CaptureTargets = new ObservableCollection<ScrcpyCaptureTarget>();

        StartCastCommand = new AsyncRelayCommand(() => startAsync(this), () => CanStart, handleErrorAsync);
        StopCastCommand = new AsyncRelayCommand(() => stopAsync(this), () => CanStop, handleErrorAsync);
        EnableWirelessCommand = new AsyncRelayCommand(() => enableWirelessAsync(this), () => CanOperate, handleErrorAsync);
        WakeCommand = new AsyncRelayCommand(() => wakeAsync(this), () => CanOperate, handleErrorAsync);
        ToggleProximityCommand = new AsyncRelayCommand(ToggleProximityAsync, () => CanOperate, handleErrorAsync);
        ToggleCastWindowPinCommand = new AsyncRelayCommand(ToggleCastWindowPinAsync, onError: handleErrorAsync);

        SetCaptureTargets([ScrcpyCaptureTarget.DefaultDisplay]);
        Refresh(device, null);
    }

    public QuestDevice Device => _device;

    public string DisplayName => _device.DisplayName;

    public string Serial => _device.Serial;

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public string DeviceSummary
    {
        get => _deviceSummary;
        private set => SetProperty(ref _deviceSummary, value);
    }

    public string SessionStateText
    {
        get => _sessionStateText;
        private set => SetProperty(ref _sessionStateText, value);
    }

    public string SessionDetailText
    {
        get => _sessionDetailText;
        private set => SetProperty(ref _sessionDetailText, value);
    }

    public string EndpointText
    {
        get => _endpointText;
        private set => SetProperty(ref _endpointText, value);
    }

    public ObservableCollection<ScrcpyCaptureTarget> CaptureTargets { get; }

    public ScrcpyCaptureTarget? SelectedCaptureTarget
    {
        get => _selectedCaptureTarget;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedCaptureTarget, value))
            {
                OnPropertyChanged(nameof(SelectedCaptureTargetText));
                OnPropertyChanged(nameof(CaptureTargetLabelText));
                OnPropertyChanged(nameof(CaptureTargetHintText));
                OnPropertyChanged(nameof(CanStart));
                StartCastCommand.RaiseCanExecuteChanged();

                if (!_suppressCaptureTargetChange && _session?.IsActive == true)
                {
                    _ = ApplyCaptureTargetChangeAsync(value);
                }
            }
        }
    }

    public string SelectedCaptureTargetText => _selectedCaptureTarget?.SelectionText
        ?? ScrcpyCaptureTarget.DefaultDisplay.SelectionText;

    public string CaptureTargetLabelText => CanEditCaptureTarget
        ? "Capture source"
        : "Capture source (reloading)";

    public string CaptureTargetHintText => CanEditCaptureTarget
        ? _session?.IsActive == true
            ? "Changing the source reloads the feed in the same cast window."
            : "Pick the feed before you click Start Cast."
        : "Reloading the cast window with the selected source.";

    public ScrcpyCaptureTargetKind? SelectedCaptureTargetKind => _selectedCaptureTarget?.Kind;

    public string? SelectedCaptureTargetId => _selectedCaptureTarget?.Id;

    public bool CanStart => _device.IsAvailable && _selectedCaptureTarget is not null && (_session is null || !_session.IsActive);

    public bool CanStop => _session?.IsActive == true;

    public bool CanOperate => _device.IsAvailable;

    public bool CanEditCaptureTarget => _device.IsAvailable && !_isApplyingCaptureTarget;

    public QuestProximityMode ProximityMode => _proximityMode;

    public string ProximityStatusText => _proximityMode switch
    {
        QuestProximityMode.KeepAwake => "Proximity: Keep awake override on",
        QuestProximityMode.NormalSensor => "Proximity: Normal sensor mode",
        _ => "Proximity: State unknown"
    };

    public string ToggleProximityText => _proximityMode == QuestProximityMode.KeepAwake
        ? "Keep Awake: On"
        : "Keep Awake: Off";

    public string ToggleProximityHintText => _proximityMode == QuestProximityMode.KeepAwake
        ? "Restores the normal forehead sensor."
        : "Pretends the headset is being worn so the displays stay awake.";

    public bool IsCastWindowPinned => _isCastWindowPinned;

    public string CastWindowPinText => _isCastWindowPinned
        ? "Pinned: On"
        : "Pinned: Off";

    public string CastWindowPinHintText => _isCastWindowPinned
        ? "This cast window stays above other desktop windows."
        : "Turns on always-on-top for this cast window.";

    public AsyncRelayCommand StartCastCommand { get; }

    public AsyncRelayCommand StopCastCommand { get; }

    public AsyncRelayCommand EnableWirelessCommand { get; }

    public AsyncRelayCommand WakeCommand { get; }

    public AsyncRelayCommand ToggleProximityCommand { get; }

    public AsyncRelayCommand ToggleCastWindowPinCommand { get; }

    public void SetCaptureTargets(
        IReadOnlyList<ScrcpyCaptureTarget>? captureTargets,
        ScrcpyCaptureTargetKind? preferredKind = null,
        string? preferredId = null)
    {
        _suppressCaptureTargetChange = true;
        CaptureTargets.Clear();

        var targets = captureTargets is { Count: > 0 }
            ? captureTargets
            : [ScrcpyCaptureTarget.DefaultDisplay];

        foreach (var captureTarget in targets)
        {
            CaptureTargets.Add(captureTarget);
        }

        var selected = targets.FirstOrDefault(target =>
                target.Kind == preferredKind &&
                string.Equals(target.Id, preferredId, StringComparison.Ordinal))
            ?? targets.FirstOrDefault(target =>
                target.Kind == ScrcpyCaptureTargetKind.Display &&
                string.Equals(target.Id, "0", StringComparison.Ordinal))
            ?? targets[0];

        SelectedCaptureTarget = selected;
        _suppressCaptureTargetChange = false;
        OnPropertyChanged(nameof(CanStart));
    }

    public void SetProximityMode(QuestProximityMode proximityMode)
    {
        if (SetProperty(ref _proximityMode, proximityMode, nameof(ProximityMode)))
        {
            OnPropertyChanged(nameof(ProximityStatusText));
            OnPropertyChanged(nameof(ToggleProximityText));
            OnPropertyChanged(nameof(ToggleProximityHintText));
        }
    }

    public void SetCastWindowPinned(bool isPinned)
    {
        if (SetProperty(ref _isCastWindowPinned, isPinned, nameof(IsCastWindowPinned)))
        {
            OnPropertyChanged(nameof(CastWindowPinText));
            OnPropertyChanged(nameof(CastWindowPinHintText));
        }
    }

    public void Refresh(QuestDevice device, QuestCastSession? session)
    {
        _device = device;
        _session = session;

        DeviceSummary = BuildDeviceSummary(device);
        EndpointText = device.ConnectionKind == QuestDeviceConnectionKind.TcpIp
            ? $"Endpoint {device.Serial}"
            : "USB attached. Use Wi-Fi ADB to remove the cable.";

        if (session is null)
        {
            SessionStateText = device.IsAvailable ? "Idle" : device.State;
            SessionDetailText = device.IsAvailable
                ? "Ready to launch a cast."
                : "Device is not ready for casting.";
            StatusBrush = device.IsAvailable ? UiPalette.Muted : UiPalette.Warning;
        }
        else
        {
            SessionStateText = session.State.ToString();
            SessionDetailText = session.State switch
            {
                QuestCastSessionState.Running => $"PID {session.ProcessId} · {session.LastMessage}",
                QuestCastSessionState.Starting => "Waiting for the scrcpy window.",
                QuestCastSessionState.Failed => session.LastMessage,
                _ => session.LastMessage
            };
            StatusBrush = session.State switch
            {
                QuestCastSessionState.Running => UiPalette.Success,
                QuestCastSessionState.Starting => UiPalette.Accent,
                QuestCastSessionState.Failed => UiPalette.Failure,
                _ => UiPalette.Muted
            };
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Serial));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanOperate));
        OnPropertyChanged(nameof(CanEditCaptureTarget));
        OnPropertyChanged(nameof(SelectedCaptureTargetText));
        OnPropertyChanged(nameof(CaptureTargetLabelText));
        OnPropertyChanged(nameof(CaptureTargetHintText));
        OnPropertyChanged(nameof(ProximityStatusText));
        OnPropertyChanged(nameof(ToggleProximityText));
        OnPropertyChanged(nameof(ToggleProximityHintText));
        OnPropertyChanged(nameof(IsCastWindowPinned));
        OnPropertyChanged(nameof(CastWindowPinText));
        OnPropertyChanged(nameof(CastWindowPinHintText));

        StartCastCommand.RaiseCanExecuteChanged();
        StopCastCommand.RaiseCanExecuteChanged();
        EnableWirelessCommand.RaiseCanExecuteChanged();
        WakeCommand.RaiseCanExecuteChanged();
        ToggleProximityCommand.RaiseCanExecuteChanged();
        ToggleCastWindowPinCommand.RaiseCanExecuteChanged();
    }

    private static string BuildDeviceSummary(QuestDevice device)
    {
        var model = device.ModelName ?? device.ProductName ?? device.DeviceCodeName ?? "Unknown model";
        return $"{model} · {device.ConnectionLabel} · {device.State}";
    }

    private async Task ApplyCaptureTargetChangeAsync(ScrcpyCaptureTarget captureTarget)
    {
        _isApplyingCaptureTarget = true;
        OnPropertyChanged(nameof(CanEditCaptureTarget));
        OnPropertyChanged(nameof(CaptureTargetLabelText));
        OnPropertyChanged(nameof(CaptureTargetHintText));

        try
        {
            await _switchCaptureTargetAsync(this, captureTarget);
        }
        catch (Exception ex)
        {
            await _handleErrorAsync(ex);
        }
        finally
        {
            _isApplyingCaptureTarget = false;
            OnPropertyChanged(nameof(CanEditCaptureTarget));
            OnPropertyChanged(nameof(CaptureTargetLabelText));
            OnPropertyChanged(nameof(CaptureTargetHintText));
        }
    }

    private async Task ToggleProximityAsync()
    {
        var targetMode = _proximityMode == QuestProximityMode.KeepAwake
            ? QuestProximityMode.NormalSensor
            : QuestProximityMode.KeepAwake;

        await _setProximityModeAsync(this, targetMode);
        SetProximityMode(targetMode);
    }

    private Task ToggleCastWindowPinAsync()
    {
        SetCastWindowPinned(!_isCastWindowPinned);
        return Task.CompletedTask;
    }
}
