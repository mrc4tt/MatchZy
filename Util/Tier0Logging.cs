using System.Runtime.InteropServices;

namespace MatchZy;

/// <summary>
/// Direct access to the engine's tier0 logging channel flags (LoggingSystem_FindChannel /
/// LoggingSystem_GetChannelFlags / LoggingSystem_SetChannelFlags, exported by libtier0.so and
/// tier0.dll). Setting a flag this way takes effect at once and prints nothing, unlike the
/// log_flags console command (buffered until later in the frame, and it echoes "Enabling flag ...").
/// Every call is guarded: if the library or an export is missing it returns false and the caller
/// falls back to the console command.
/// </summary>
public static class Tier0Logging
{
    // LoggingChannelFlags_t (hl2sdk public/tier0/loggingtypes.h).
    public const int LCF_DO_NOT_ECHO = 0x2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FindChannelFn([MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetFlagsFn(int channelId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetFlagsFn(int channelId, int flags);

    private static bool _resolved;
    private static FindChannelFn? _find;
    private static GetFlagsFn? _get;
    private static SetFlagsFn? _set;

    private static bool Resolve()
    {
        if (_resolved)
            return _find != null && _get != null && _set != null;
        _resolved = true;
        try
        {
            IntPtr lib = IntPtr.Zero;
            string[] names = OperatingSystem.IsWindows() ? new[] { "tier0.dll", "tier0" } : new[] { "libtier0.so", "tier0" };
            foreach (var n in names)
                if (NativeLibrary.TryLoad(n, out lib) && lib != IntPtr.Zero)
                    break;
            if (lib == IntPtr.Zero)
                return false;
            if (!NativeLibrary.TryGetExport(lib, "LoggingSystem_FindChannel", out var pFind)
                || !NativeLibrary.TryGetExport(lib, "LoggingSystem_GetChannelFlags", out var pGet)
                || !NativeLibrary.TryGetExport(lib, "LoggingSystem_SetChannelFlags", out var pSet))
                return false;
            _find = Marshal.GetDelegateForFunctionPointer<FindChannelFn>(pFind);
            _get = Marshal.GetDelegateForFunctionPointer<GetFlagsFn>(pGet);
            _set = Marshal.GetDelegateForFunctionPointer<SetFlagsFn>(pSet);
            return true;
        }
        catch
        {
            _find = null; _get = null; _set = null;
            return false;
        }
    }

    /// <summary>Sets or clears one flag on a channel. False when it could not be applied.</summary>
    public static bool SetChannelFlag(string channel, int flag, bool on)
    {
        try
        {
            if (!Resolve())
                return false;
            int id = _find!(channel);
            if (id < 0)
                return false;
            int flags = _get!(id);
            int want = on ? flags | flag : flags & ~flag;
            if (want != flags)
                _set!(id, want);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
