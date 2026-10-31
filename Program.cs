using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AirFlick;

public static class Program
{
    private const string InstanceKey = "AirFlick.MainInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            mainInstance.RedirectActivationToAsync(activationArguments).AsTask().Wait();
            return 0;
        }

        Application.Start(initializationCallbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
