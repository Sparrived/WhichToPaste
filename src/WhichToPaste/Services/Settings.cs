using System.Text.Json;
using System.Text.Json.Serialization;
using WhichToPaste.Native;

namespace WhichToPaste.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>用户可改的全部配置，直接序列化成 settings.json。</summary>
public sealed class Settings
{
    public string PasteKey { get; set; } = "Ctrl+V";
    public string UpKey { get; set; } = "Ctrl+C";
    public string DownKey { get; set; } = "Ctrl+B";
    public string CancelKey { get; set; } = "Esc";
    public string CancelAltKey { get; set; } = "Ctrl+Z";

    public bool TakeOverRightClick { get; set; } = true;
    public bool Paused { get; set; }
    public int HistoryLimit { get; set; } = 30;
    public bool AutoStart { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;

    [JsonIgnore]
    public KeyCombo Paste => KeyCombo.Parse(PasteKey) ?? new KeyCombo(HotkeyModifiers.Control, VK.KEY_V);

    [JsonIgnore]
    public KeyCombo Up => KeyCombo.Parse(UpKey) ?? new KeyCombo(HotkeyModifiers.Control, VK.KEY_C);

    [JsonIgnore]
    public KeyCombo Down => KeyCombo.Parse(DownKey) ?? new KeyCombo(HotkeyModifiers.Control, VK.KEY_B);

    [JsonIgnore]
    public KeyCombo Cancel => KeyCombo.Parse(CancelKey) ?? new KeyCombo(HotkeyModifiers.None, VK.ESCAPE);

    [JsonIgnore]
    public KeyCombo CancelAlt => KeyCombo.Parse(CancelAltKey) ?? new KeyCombo(HotkeyModifiers.Control, VK.KEY_Z);

    /// <summary>用户是否选择了深色外观（System 时读注册表）。</summary>
    public bool ResolveDark()
    {
        if (Theme == AppTheme.Dark) return true;
        if (Theme == AppTheme.Light) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// 数据目录。默认 %APPDATA%\WhichToPaste；设了环境变量 WHICTOPASTE_HOME 就改用
    /// 它，方便便携部署，也让自检可以指向临时目录而不碰真实数据。
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("WHICTOPASTE_HOME") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhichToPaste");

    public static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static bool HasSettings => File.Exists(FilePath);

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 配置损坏不该让程序起不来，退回默认值。
        }
        return new Settings();
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}