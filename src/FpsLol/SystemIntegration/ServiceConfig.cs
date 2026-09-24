using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using static FpsLol.SystemIntegration.NativeMethods;

namespace FpsLol.SystemIntegration;

/// <summary>Changes a service start type through the Service Control Manager (never deletes services).</summary>
public static class ServiceConfig
{
    public static bool Exists(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static ServiceStartMode GetStartMode(string name)
    {
        using var sc = new ServiceController(name);
        return sc.StartType;
    }

    public static bool IsRunning(string name)
    {
        using var sc = new ServiceController(name);
        return sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    public static void SetStartMode(string name, ServiceStartMode mode)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(scm, name, SERVICE_QUERY_CONFIG | SERVICE_CHANGE_CONFIG);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!ChangeServiceConfig(service, SERVICE_NO_CHANGE, (uint)mode, SERVICE_NO_CHANGE,
                        null, null, IntPtr.Zero, null, null, null, null))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static void Stop(string name, TimeSpan timeout)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
        if (!sc.CanStop) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }

    public static void Start(string name, TimeSpan timeout)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }
}
