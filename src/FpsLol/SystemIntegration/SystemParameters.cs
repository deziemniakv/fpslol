using System.ComponentModel;
using System.Runtime.InteropServices;
using static FpsLol.SystemIntegration.NativeMethods;

namespace FpsLol.SystemIntegration;

public enum SpiSetting
{
    /// <summary>[threshold1, threshold2, acceleration] — "Enhance pointer precision".</summary>
    MouseAcceleration,
    /// <summary>[0|1] — "Animate controls and elements inside windows".</summary>
    ClientAreaAnimation,
    /// <summary>[0|1] — "Animate windows when minimizing and maximizing".</summary>
    WindowAnimation,
    /// <summary>[flags] — Sticky Keys (SKF_*).</summary>
    StickyKeys,
    /// <summary>[flags] — Filter Keys (FKF_*).</summary>
    FilterKeys,
    /// <summary>[flags] — Toggle Keys (TKF_*).</summary>
    ToggleKeys,
}

/// <summary>Typed wrapper around SystemParametersInfo. Changes are persisted to the user profile and broadcast.</summary>
public static class SystemParametersUtil
{
    /// <summary>SKF_HOTKEYACTIVE / FKF_HOTKEYACTIVE / TKF_HOTKEYACTIVE share the same bit.</summary>
    public const int HotkeyActiveFlag = 0x4;

    public static int[] Get(SpiSetting setting)
    {
        switch (setting)
        {
            case SpiSetting.MouseAcceleration:
            {
                var values = new int[3];
                Check(SystemParametersInfo(SPI_GETMOUSE, 0, values, 0));
                return values;
            }
            case SpiSetting.ClientAreaAnimation:
            {
                var ptr = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(ptr, 0);
                    Check(SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ptr, 0));
                    return [Marshal.ReadInt32(ptr) != 0 ? 1 : 0];
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            case SpiSetting.WindowAnimation:
            {
                var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                Check(SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0));
                return [info.iMinAnimate != 0 ? 1 : 0];
            }
            case SpiSetting.StickyKeys:
                return [(int)GetFlags(SPI_GETSTICKYKEYS)];
            case SpiSetting.ToggleKeys:
                return [(int)GetFlags(SPI_GETTOGGLEKEYS)];
            case SpiSetting.FilterKeys:
            {
                var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
                Check(SystemParametersInfo(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0));
                return [(int)fk.dwFlags];
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(setting));
        }
    }

    public static void Set(SpiSetting setting, int[] values)
    {
        switch (setting)
        {
            case SpiSetting.MouseAcceleration:
                Check(SystemParametersInfo(SPI_SETMOUSE, 0, values, SPIF_UPDATEINIFILE_SENDCHANGE));
                break;
            case SpiSetting.ClientAreaAnimation:
                // For this action the BOOL is passed by value in pvParam.
                Check(SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, new IntPtr(values[0] != 0 ? 1 : 0), SPIF_UPDATEINIFILE_SENDCHANGE));
                break;
            case SpiSetting.WindowAnimation:
            {
                var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = values[0] != 0 ? 1 : 0 };
                Check(SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE_SENDCHANGE));
                break;
            }
            case SpiSetting.StickyKeys:
                SetFlags(SPI_SETSTICKYKEYS, (uint)values[0]);
                break;
            case SpiSetting.ToggleKeys:
                SetFlags(SPI_SETTOGGLEKEYS, (uint)values[0]);
                break;
            case SpiSetting.FilterKeys:
            {
                var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
                Check(SystemParametersInfo(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0));
                fk.dwFlags = (uint)values[0];
                Check(SystemParametersInfo(SPI_SETFILTERKEYS, fk.cbSize, ref fk, SPIF_UPDATEINIFILE_SENDCHANGE));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(setting));
        }
    }

    /// <summary>Compares only the part of the value that FPS.LOL changes.</summary>
    public static bool Matches(SpiSetting setting, int[] expected, int[] actual) => setting switch
    {
        SpiSetting.StickyKeys or SpiSetting.FilterKeys or SpiSetting.ToggleKeys =>
            (expected[0] & HotkeyActiveFlag) == (actual[0] & HotkeyActiveFlag),
        _ => expected.SequenceEqual(actual),
    };

    private static uint GetFlags(uint action)
    {
        var s = new FLAGS_STRUCT { cbSize = (uint)Marshal.SizeOf<FLAGS_STRUCT>() };
        Check(SystemParametersInfo(action, s.cbSize, ref s, 0));
        return s.dwFlags;
    }

    private static void SetFlags(uint action, uint flags)
    {
        var s = new FLAGS_STRUCT { cbSize = (uint)Marshal.SizeOf<FLAGS_STRUCT>(), dwFlags = flags };
        Check(SystemParametersInfo(action, s.cbSize, ref s, SPIF_UPDATEINIFILE_SENDCHANGE));
    }

    private static void Check(bool ok)
    {
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
