using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AirFlick.Services;
using AirFlick.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AirFlick;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly GestureProcessLauncher _cameraLauncher = new();

    public MainWindow()
    {
        InitializeComponent();

        var client = new AirFlickBackendClient();
        _ = client.InitializeAsync();

        ViewModel = new MainViewModel(client, DispatcherQueue.GetForCurrentThread());

        // Wire GestureProcessLauncher events to MainViewModel
        _cameraLauncher.StateChanged += (s, state) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CameraState = state;
            });
        };

        _cameraLauncher.StatusMessageUpdated += (s, msg) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CameraStatusMessage = msg;
                ViewModel.GestureBridgeStatus = msg;
            });
        };

        _cameraLauncher.CameraActivated += (s, info) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CameraDetails = $"Cam #{info.CameraIndex} ({info.Backend} {info.Resolution})";
            });
        };

        _cameraLauncher.HeartbeatReceived += (s, hb) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CameraFrameCount = hb.FrameCount;
                ViewModel.CameraFps = hb.Fps;
            });
        };

        _cameraLauncher.GestureEventReceived += (s, gesture) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.DispatchGesture(gesture);
            });
        };

        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _cameraLauncher.Dispose();
        ViewModel.Dispose();
    }

    private void OnToggleGestureCameraClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsCameraActive || ViewModel.IsCameraStarting)
        {
            _cameraLauncher.Stop();
        }
        else
        {
            _cameraLauncher.Start(showPreview: true);
        }
    }

    private void OnSimulatePinClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string digit)
        {
            ViewModel.DispatchGesture($"PIN{digit}");
        }
    }

    private async void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var openPicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            openPicker.FileTypeFilter.Add("*");

            // Associate the picker with this WinUI window handle (required for WinUI 3)
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(openPicker, hwnd);

            var file = await openPicker.PickSingleFileAsync();
            if (file != null && File.Exists(file.Path))
            {
                ViewModel.SelectedFilePath = file.Path;
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportError($"Failed to pick file: {ex.Message}");
        }
    }

    private void OnErrorInfoBarClose(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ReportError(null);
    }

    private void OnSuccessInfoBarClose(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ReportSuccess(null);
    }
}
