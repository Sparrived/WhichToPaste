using System.Reflection;
using Microsoft.Win32;

namespace WhichToPaste.Services;

/// <summary>开机自启动 = 往 HKCU 的 Run 项写一条命令行，不需要管理员权限。</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WhichToPaste";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Apply(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null) return;

        if (enabled)
        {
            string command = BuildCommand();
            if (command.Length > 0) key.SetValue(ValueName, command);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildCommand()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return string.Empty;

        // dotnet run / dotnet xxx.dll 时进程是 dotnet 宿主，要把入口 dll 一起写进去。
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? dll = Assembly.GetEntryAssembly()?.Location;
            return string.IsNullOrEmpty(dll) ? string.Empty : $"\"{exe}\" \"{dll}\"";
        }

        return $"\"{exe}\"";
    }
}