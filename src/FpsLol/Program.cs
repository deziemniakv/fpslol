using System.Diagnostics;
using FpsLol.Optimizations.Privileged;

namespace FpsLol;

public static class Program
{
    private const string MutexName = @"Local\FPS.LOL.SingleInstance";
    public const string ActivateEventName = @"Local\FPS.LOL.Activate";

    public static bool StartMinimized { get; private set; }

    /// <summary>Developer switch: "--page Tweaks" opens a specific page on start.</summary>
    public static string? StartPage { get; private set; }

    /// <summary>Developer switch: "--welcome" shows the first-run experience again.</summary>
    public static bool ShowWelcome { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Short-lived elevated helper: no UI, executes validated jobs and exits.
        if (args.Length >= 3 && args[0] == "--elevated-worker" && int.TryParse(args[2], out var parentPid))
            return ElevatedWorker.Run(args[1], parentPid);

        // When restarting elevated, wait for the previous (non-elevated) instance to exit first.
        var waitIndex = Array.IndexOf(args, "--wait-for");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var oldPid))
        {
            try
            {
                using var old = Process.GetProcessById(oldPid);
                old.WaitForExit(10_000);
            }
            catch (ArgumentException)
            {
                // Already exited.
            }
        }

        StartMinimized = args.Contains("--minimized");
        ShowWelcome = args.Contains("--welcome");
        var pageIndex = Array.IndexOf(args, "--page");
        if (pageIndex >= 0 && pageIndex + 1 < args.Length) StartPage = args[pageIndex + 1];

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is running: ask it to show its window, then exit.
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
