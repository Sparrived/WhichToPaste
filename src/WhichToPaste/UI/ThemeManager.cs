using System.Windows;
using Application = System.Windows.Application;

namespace WhichToPaste.UI;

/// <summary>配色字典整体替换：索引 0 就是配色，控件样式全用 DynamicResource，换肤即时生效。</summary>
internal static class ThemeManager
{
    private const string LightPalette = "pack://application:,,,/UI/Theme.Light.xaml";
    private const string DarkPalette = "pack://application:,,,/UI/Theme.Dark.xaml";

    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        if (Application.Current is not { } app) return;

        IsDark = dark;
        var palette = new ResourceDictionary { Source = new Uri(dark ? DarkPalette : LightPalette) };
        var merged = app.Resources.MergedDictionaries;

        if (merged.Count == 0) merged.Add(palette);
        else merged[0] = palette;
    }
}