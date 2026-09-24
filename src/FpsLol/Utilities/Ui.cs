using System.Windows;
using System.Windows.Threading;

namespace FpsLol.Utilities;

public static class Ui
{
    private static Dispatcher Dispatcher => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public static void Post(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
    }

    public static Task RunAsync(Action action) =>
        Dispatcher.CheckAccess() ? Task.Run(action) : Dispatcher.InvokeAsync(action).Task;
}
