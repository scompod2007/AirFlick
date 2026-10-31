using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AirFlick.Common;
using AirFlick.Services;
using Microsoft.UI.Dispatching;

namespace AirFlick.ViewModels;

public sealed class MainViewModel : ViewModelBase, IGestureBridgeTarget, IDisposable
{
    private readonly IAirFlickBackendClient _client;
    private readonly DispatcherQueue _dispatcherQueue;

    // Sender state
    private string? _selectedFilePath;
    private string _selectedFileName = "No file selected";
    private string _selectedFileSizeText = string.Empty;
    private string? _pendingSharePin;
    private string? _pendingShareAuthId;
    private bool _hasPendingShare;
    private int _pinSecondsRemaining = (int)PinAuthorizationService.DefaultLifetime.TotalSeconds;
    private Timer? _pinCountdownTimer;

    // Receiver state
    private string? _selectedSenderDevice;
    private string _receiverPinInput = string.Empty;

    // Common workflow state
    private string _statusMessage = "Ready";
    private string _connectionStateText = "Disconnected";
    private bool _isBusy;
    private int _transferProgress;
    private string _transferProgressText = string.Empty;
    private bool _isTransferActive;
    private string? _activeTransferFileName;
    private bool _hasError;
    private string? _errorMessage;
    private bool _hasSuccess;
    private string? _successMessage;
    private string _lastDetectedGesture = "None";
    private string _gestureBridgeStatus = "Gesture Bridge Active (GRAB / DROP / PIN1-5 / CONFIRM)";
    private bool _isGestureCameraRunning;

    private CancellationTokenSource? _activeCts;
    private bool _isDisposed;

    public GestureBridgeService GestureBridge { get; }

    public MainViewModel(IAirFlickBackendClient client, DispatcherQueue dispatcherQueue)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

        DiscoveredDevices = new ObservableCollection<string>();

        // Commands
        StartDiscoveryCommand = new RelayCommand(async () => await StartDiscoveryAsync(), () => !IsBusy);
        StopDiscoveryCommand = new RelayCommand(async () => await StopDiscoveryAsync(), () => !IsBusy);
        PrepareShareCommand = new RelayCommand(async () => await PrepareShareAsync(), () => !string.IsNullOrEmpty(SelectedFilePath) && !IsBusy);
        CancelShareCommand = new RelayCommand(CancelActiveOperation, () => HasPendingShare || IsTransferActive || IsBusy);
        ReceiveFileCommand = new RelayCommand(
            async () => await ReceiveFileAsync(),
            () => !string.IsNullOrWhiteSpace(SelectedSenderDevice)
                  && ReceiverPinInput?.Length == 3
                  && PinAuthorizationService.IsValidPinFormat(ReceiverPinInput)
                  && !IsBusy);
        ClearMessageCommand = new RelayCommand(ClearFeedback);

        // Gesture Bridge Integration
        GestureBridge = new GestureBridgeService(this, RunOnUI);
        GestureBridge.GestureReceived += (s, e) =>
        {
            LastDetectedGesture = $"{e.RawGesture} ({e.Action})";
        };

        SimulateGrabCommand = new RelayCommand(() => DispatchGesture("GRAB"));
        SimulateDropCommand = new RelayCommand(() => DispatchGesture("DROP"));
        SimulateConfirmCommand = new RelayCommand(() => DispatchGesture("CONFIRM"));

        SubscribeToBackend();
    }

    public ObservableCollection<string> DiscoveredDevices { get; }
    IReadOnlyList<string> IGestureBridgeTarget.DiscoveredDevices => DiscoveredDevices;

    public bool HasNoDiscoveredDevices => DiscoveredDevices.Count == 0;
    public Microsoft.UI.Xaml.Visibility NoDevicesDiscoveredVisibility =>
        DiscoveredDevices.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    #region Properties

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                if (!string.IsNullOrEmpty(value) && File.Exists(value))
                {
                    var fileInfo = new FileInfo(value);
                    SelectedFileName = fileInfo.Name;
                    SelectedFileSizeText = FormatBytes(fileInfo.Length);
                }
                else
                {
                    SelectedFileName = "No file selected";
                    SelectedFileSizeText = string.Empty;
                }
                ((RelayCommand)PrepareShareCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedFileName
    {
        get => _selectedFileName;
        private set => SetProperty(ref _selectedFileName, value);
    }

    public string SelectedFileSizeText
    {
        get => _selectedFileSizeText;
        private set => SetProperty(ref _selectedFileSizeText, value);
    }

    public string? PendingSharePin
    {
        get => _pendingSharePin;
        private set => SetProperty(ref _pendingSharePin, value);
    }

    public bool HasPendingShare
    {
        get => _hasPendingShare;
        private set
        {
            if (SetProperty(ref _hasPendingShare, value))
            {
                OnPropertyChanged(nameof(PendingShareVisibility));
                ((RelayCommand)CancelShareCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility PendingShareVisibility =>
        _hasPendingShare ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public int PinSecondsRemaining
    {
        get => _pinSecondsRemaining;
        private set => SetProperty(ref _pinSecondsRemaining, value);
    }

    public string? SelectedSenderDevice
    {
        get => _selectedSenderDevice;
        set
        {
            if (SetProperty(ref _selectedSenderDevice, value))
            {
                ((RelayCommand)ReceiveFileCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ReceiverPinInput
    {
        get => _receiverPinInput;
        set
        {
            var raw = value?.Trim() ?? string.Empty;
            // Restrict input to digits 1-5 only and up to 3 digits
            var filtered = new StringBuilder(3);
            foreach (var ch in raw)
            {
                if (ch >= '1' && ch <= '5')
                {
                    filtered.Append(ch);
                    if (filtered.Length == 3) break;
                }
            }
            var cleaned = filtered.ToString();
            if (SetProperty(ref _receiverPinInput, cleaned))
            {
                ((RelayCommand)ReceiveFileCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ConnectionStateText
    {
        get => _connectionStateText;
        private set => SetProperty(ref _connectionStateText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((RelayCommand)StartDiscoveryCommand).NotifyCanExecuteChanged();
                ((RelayCommand)StopDiscoveryCommand).NotifyCanExecuteChanged();
                ((RelayCommand)PrepareShareCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ReceiveFileCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelShareCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public int TransferProgress
    {
        get => _transferProgress;
        private set => SetProperty(ref _transferProgress, value);
    }

    public string TransferProgressText
    {
        get => _transferProgressText;
        private set => SetProperty(ref _transferProgressText, value);
    }

    public bool IsTransferActive
    {
        get => _isTransferActive;
        private set
        {
            if (SetProperty(ref _isTransferActive, value))
            {
                OnPropertyChanged(nameof(TransferActiveVisibility));
                ((RelayCommand)CancelShareCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility TransferActiveVisibility =>
        _isTransferActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string? ActiveTransferFileName
    {
        get => _activeTransferFileName;
        private set => SetProperty(ref _activeTransferFileName, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                HasError = !string.IsNullOrEmpty(value);
            }
        }
    }

    public bool HasSuccess
    {
        get => _hasSuccess;
        private set => SetProperty(ref _hasSuccess, value);
    }

    public string? SuccessMessage
    {
        get => _successMessage;
        private set
        {
            if (SetProperty(ref _successMessage, value))
            {
                HasSuccess = !string.IsNullOrEmpty(value);
            }
        }
    }

    public string LastDetectedGesture
    {
        get => _lastDetectedGesture;
        set => SetProperty(ref _lastDetectedGesture, value);
    }

    public string GestureBridgeStatus
    {
        get => _gestureBridgeStatus;
        set => SetProperty(ref _gestureBridgeStatus, value);
    }

    public bool IsGestureCameraRunning
    {
        get => _isGestureCameraRunning;
        set => SetProperty(ref _isGestureCameraRunning, value);
    }

    private CameraServiceState _cameraState = CameraServiceState.Stopped;
    public CameraServiceState CameraState
    {
        get => _cameraState;
        set
        {
            if (SetProperty(ref _cameraState, value))
            {
                IsGestureCameraRunning = (_cameraState == CameraServiceState.Active);
                OnPropertyChanged(nameof(IsCameraStarting));
                OnPropertyChanged(nameof(IsCameraActive));
                OnPropertyChanged(nameof(IsCameraUnavailable));
                OnPropertyChanged(nameof(IsCameraStopped));
                OnPropertyChanged(nameof(CameraToggleButtonText));
                OnPropertyChanged(nameof(SimulationModeNotice));
            }
        }
    }

    private string _cameraStatusMessage = "Camera stopped";
    public string CameraStatusMessage
    {
        get => _cameraStatusMessage;
        set => SetProperty(ref _cameraStatusMessage, value);
    }

    private string _cameraDetails = "No camera active";
    public string CameraDetails
    {
        get => _cameraDetails;
        set => SetProperty(ref _cameraDetails, value);
    }

    private int _cameraFrameCount;
    public int CameraFrameCount
    {
        get => _cameraFrameCount;
        set => SetProperty(ref _cameraFrameCount, value);
    }

    private double _cameraFps;
    public double CameraFps
    {
        get => _cameraFps;
        set => SetProperty(ref _cameraFps, value);
    }

    public bool IsCameraStarting => CameraState == CameraServiceState.Starting;
    public bool IsCameraActive => CameraState == CameraServiceState.Active;
    public bool IsCameraUnavailable => CameraState == CameraServiceState.Unavailable || CameraState == CameraServiceState.Error;
    public bool IsCameraStopped => CameraState == CameraServiceState.Stopped;
    public string CameraToggleButtonText => (IsCameraActive || IsCameraStarting) ? "Stop Camera" : "Start Real Camera";
    public string SimulationModeNotice => IsCameraActive
        ? "LIVE PHYSICAL WEBCAM ACTIVE (Physical Frames Streaming)"
        : "SIMULATION / MANUAL TEST FALLBACK (Physical Camera Inactive)";

    #endregion

    #region Commands

    public ICommand StartDiscoveryCommand { get; }
    public ICommand StopDiscoveryCommand { get; }
    public ICommand PrepareShareCommand { get; }
    public ICommand CancelShareCommand { get; }
    public ICommand ReceiveFileCommand { get; }
    public ICommand ClearMessageCommand { get; }
    public ICommand SimulateGrabCommand { get; }
    public ICommand SimulateDropCommand { get; }
    public ICommand SimulateConfirmCommand { get; }

    #endregion

    private void SubscribeToBackend()
    {
        _client.StateChanged += OnBackendStateChanged;
        _client.SessionChanged += OnSessionChanged;
        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.TransferProgress += OnTransferProgress;
        _client.TransferCompleted += OnTransferCompleted;
        _client.PendingShareCreated += OnPendingShareCreated;
    }

    private void RunOnUI(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    private void OnBackendStateChanged(object? sender, AirFlickStateChangedEventArgs e)
    {
        RunOnUI(() =>
        {
            StatusMessage = $"State: {e.State}" + (string.IsNullOrEmpty(e.Detail) ? string.Empty : $" ({e.Detail})");

            switch (e.State)
            {
                case AirFlickState.Transferring:
                    IsTransferActive = true;
                    break;
                case AirFlickState.TransferComplete:
                case AirFlickState.Waiting:
                    IsTransferActive = false;
                    break;
                case AirFlickState.Failed:
                    IsTransferActive = false;
                    ErrorMessage = e.Detail ?? "Transfer failed.";
                    break;
            }
        });
    }

    private void OnSessionChanged(object? sender, AirFlickSessionChangedEventArgs e)
    {
        RunOnUI(() =>
        {
            ConnectionStateText = string.IsNullOrEmpty(e.PeerName)
                ? $"{e.Phase}"
                : $"{e.Phase} - {e.PeerName}";
        });
    }

    private void OnDeviceDiscovered(object? sender, string deviceName)
    {
        RunOnUI(() =>
        {
            if (!string.IsNullOrWhiteSpace(deviceName) && !DiscoveredDevices.Contains(deviceName))
            {
                DiscoveredDevices.Add(deviceName);
                OnPropertyChanged(nameof(NoDevicesDiscoveredVisibility));
            }
        });
    }

    private void OnTransferProgress(object? sender, AirFlickTransferProgressEventArgs e)
    {
        RunOnUI(() =>
        {
            IsTransferActive = true;
            ActiveTransferFileName = e.FileName;
            TransferProgress = Math.Clamp(e.Percentage, 0, 100);
            TransferProgressText = $"{FormatBytes(e.BytesTransferred)} / {FormatBytes(e.TotalBytes)} ({e.Percentage}%)";
        });
    }

    private void OnTransferCompleted(object? sender, AirFlickTransferResultEventArgs e)
    {
        RunOnUI(() =>
        {
            IsTransferActive = false;
            IsBusy = false;
            ResetPinTimer();

            if (e.Success)
            {
                HasPendingShare = false;
                PendingSharePin = null;
                if (!string.IsNullOrWhiteSpace(e.DestinationPath))
                {
                    SuccessMessage = $"Transfer complete! Saved to: {e.DestinationPath}";
                }
                else
                {
                    SuccessMessage = $"Transfer complete: {e.FileName}";
                }
            }
            else
            {
                ErrorMessage = $"Transfer error: {e.ErrorMessage ?? "Unknown failure"}";
            }
        });
    }

    private void OnPendingShareCreated(object? sender, AirFlickPendingShareEventArgs e)
    {
        RunOnUI(() =>
        {
            _pendingShareAuthId = e.AuthorizationId;
            PendingSharePin = e.Pin;
            HasPendingShare = true;
            ActiveTransferFileName = e.FileName;
            StartPinCountdown();
        });
    }

    public async Task StartDiscoveryAsync()
    {
        ClearFeedback();
        try
        {
            IsBusy = true;
            StatusMessage = "Scanning for nearby devices...";
            await _client.StartDiscoveryAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start discovery: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopDiscoveryAsync()
    {
        try
        {
            IsBusy = true;
            await _client.StopDiscoveryAsync();
            _client.CancelShare();
            StatusMessage = "Discovery stopped.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to stop discovery: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PrepareShareAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath))
        {
            ErrorMessage = "Please select a valid existing file first.";
            return;
        }

        ClearFeedback();
        IsBusy = true;
        StatusMessage = "Preparing offline share and generating PIN...";

        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        try
        {
            var result = await _client.InitiatePendingShareAsync(SelectedFilePath, _activeCts.Token);
            if (result.Success)
            {
                PendingSharePin = result.Pin;
                _pendingShareAuthId = result.AuthorizationId;
                HasPendingShare = true;
                ActiveTransferFileName = result.FileName;
                StatusMessage = "Share ready! Waiting for nearby receiver to connect and enter PIN.";
                StartPinCountdown();
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "Could not prepare file share.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Share preparation error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReceiveFileAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSenderDevice))
        {
            ErrorMessage = "Please select a nearby sender device.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ReceiverPinInput)
            || ReceiverPinInput.Length != 3
            || !PinAuthorizationService.IsValidPinFormat(ReceiverPinInput))
        {
            ErrorMessage = "Enter the 3-digit numeric PIN (000-999) shown on the sender's screen.";
            return;
        }

        ClearFeedback();
        IsBusy = true;
        StatusMessage = $"Connecting to {SelectedSenderDevice} and authenticating...";

        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        try
        {
            var outcome = await _client.ReceiveFileFromSenderAsync(SelectedSenderDevice, ReceiverPinInput, _activeCts.Token);
            if (outcome.Success)
            {
                if (!string.IsNullOrWhiteSpace(outcome.DestinationPath))
                {
                    SuccessMessage = $"Successfully received '{outcome.FileName}'! Saved to: {outcome.DestinationPath}";
                }
                else
                {
                    SuccessMessage = $"Successfully received '{outcome.FileName}'!";
                }
                StatusMessage = "Transfer complete.";
            }
            else
            {
                ErrorMessage = outcome.Error ?? "Receive transfer failed.";
                StatusMessage = "Transfer failed.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Receive cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Receive error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelActiveOperation()
    {
        _activeCts?.Cancel();
        ResetPinTimer();
        HasPendingShare = false;
        PendingSharePin = null;
        IsTransferActive = false;
        IsBusy = false;

        _client.CancelShare();

        _ = Task.Run(async () =>
        {
            try
            {
                await _client.DisconnectAsync();
            }
            catch
            {
            }
        });

        StatusMessage = "Operation cancelled.";
    }

    private void StartPinCountdown()
    {
        ResetPinTimer();
        PinSecondsRemaining = (int)PinAuthorizationService.DefaultLifetime.TotalSeconds;
        _pinCountdownTimer = new Timer(OnPinCountdownTick, null, 1000, 1000);
    }

    private void OnPinCountdownTick(object? state)
    {
        RunOnUI(() =>
        {
            if (PinSecondsRemaining > 0)
            {
                PinSecondsRemaining--;
            }
            else
            {
                ResetPinTimer();
                HasPendingShare = false;
                PendingSharePin = null;
                _client.CancelShare();
                ErrorMessage = "PIN expired (2-minute limit). Please prepare the share again.";
                StatusMessage = "Share PIN expired.";
            }
        });
    }

    private void ResetPinTimer()
    {
        _pinCountdownTimer?.Dispose();
        _pinCountdownTimer = null;
    }

    public void ClearFeedback()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    public void DispatchGesture(string gestureName, string? hand = null)
    {
        GestureBridge.ProcessGesture(gestureName, hand);
    }

    public void UpdateStatus(string message)
    {
        StatusMessage = message;
    }

    public void ReportError(string? message)
    {
        ErrorMessage = message;
    }

    public void ReportSuccess(string? message)
    {
        SuccessMessage = message;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        GestureBridge.Dispose();
        ResetPinTimer();
        _activeCts?.Cancel();
        _activeCts?.Dispose();

        _client.StateChanged -= OnBackendStateChanged;
        _client.SessionChanged -= OnSessionChanged;
        _client.DeviceDiscovered -= OnDeviceDiscovered;
        _client.TransferProgress -= OnTransferProgress;
        _client.TransferCompleted -= OnTransferCompleted;
        _client.PendingShareCreated -= OnPendingShareCreated;
    }
}

