using System.IO;

namespace BTUnlock.Desktop.Core;

/// <summary>主程序文件日志：%APPDATA%\BTUnlock\log.txt，超 512KB 截断重开。写失败静默。</summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BTUnlock", "log.txt");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (dir is not null) System.IO.Directory.CreateDirectory(dir);
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxBytes) file.Delete();
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}\r\n");
            }
            catch (Exception)
            {
                // 日志写失败不影响功能
            }
        }
    }
}
