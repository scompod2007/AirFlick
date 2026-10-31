using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using AirFlick.Services;

namespace AirFlick;

/// <summary>
/// Minimal bootstrap Application for the AirFlick backend host.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public Window? Window => _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        try
        {
            ServiceManager.Initialize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ServiceManager initialization notice: {ex.Message}");
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"AirFlick Backend Unhandled Exception: {e.Exception}");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
