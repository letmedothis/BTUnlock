using System.Runtime.InteropServices;

namespace BTUnlock.Desktop.Core;

internal static class WorkStationLocker
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public static bool TryLock()
    {
        try
        {
            return LockWorkStation();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
