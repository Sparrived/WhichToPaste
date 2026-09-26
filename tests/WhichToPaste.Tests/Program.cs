using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhichToPaste.Models;
using WhichToPaste.Native;
using WhichToPaste.Services;
using WhichToPaste.UI;

namespace WhichToPaste.Tests;

/// <summary>
/// 不依赖测试框架的自检：跑完打印结果，有失败就以退出码 1 结束。
/// 覆盖快捷键解析、条目语义、历史去重/裁剪/落盘、图片清理与选择框摆放这些不看界面也能验的逻辑。
/// </summary>
internal static class Program
{
    private static int _failed;
    private static string _home = string.Empty;

    private static void Main()
    {
        // 必须在碰 SettingsStore 之前设好，自检绝不写真实的历史与设置。
        _home = Path.Combine(Path.GetTempPath(), "WhichToPaste.Tests", Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("WHICTOPASTE_HOME", _home);

        Console.WriteLine($"数据目录：{SettingsStore.DataDirectory}");
        Console.WriteLine();

        KeyComboChecks();
        ClipItemChecks();
        HistoryChecks();
        ImageStoreChecks();
        PickerPlacementChecks();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "全部通过" : $"失败 {_failed} 项");

        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }

        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    // ---------- 快捷键 ----------

    private static void KeyComboChecks()
    {
        Section("KeyCombo");

        var paste = KeyCombo.Parse("Ctrl+V");
        Eq("解析 Ctrl+V 的虚拟键", (uint)0x56, paste!.VirtualKey);
        Eq("解析 Ctrl+V 的修饰键", HotkeyModifiers.Control, paste.Modifiers);
        Check("匹配大小写不同的写法", KeyCombo.Parse("ctrl+v")!.Matches(HotkeyModifiers.Control, 0x56));
        Check("修饰键不符时不匹配", !paste.Matches(HotkeyModifiers.None, 0x56));
        Check("虚拟键不符时不匹配", !paste.Matches(HotkeyModifiers.Control, 0x43));

        Eq("Esc 无修饰键", HotkeyModifiers.None, KeyCombo.Parse("Esc")!.Modifiers);
        Eq("Esc 归一化写法", "Esc", KeyCombo.Parse("escape")!.ToString());
        Eq("多功能键往返", "Ctrl+Shift+F5", KeyCombo.Parse(KeyCombo.Parse("Ctrl+Shift+F5")!.ToString())!.ToString());
        Eq("符号键往返", "Ctrl+;", KeyCombo.Parse(KeyCombo.Parse("Ctrl+;")!.ToString())!.ToString());
        Eq("数字键", (uint)0x37, KeyCombo.Parse("7")!.VirtualKey);

        Check("空串解析失败", KeyCombo.Parse("") is null);
        Check("只有修饰键解析失败", KeyCombo.Parse("Ctrl+") is null);
        Check("两个主键解析失败", KeyCombo.Parse("Ctrl+A+B") is null);
        Check("未知键名解析失败", KeyCombo.Parse("Ctrl+Nope") is null);
    }

    // ---------- 条目语义 ----------

    private static void ClipItemChecks()
    {
        Section("ClipItem");

        var text = new ClipItem { Kind = ClipKind.Text, Text = "第一行\n第二行" };
        Check("多行文本被识别", text.IsMultiLine);
        Check("文本可用", text.IsUsable);
        Eq("文本去重键", "T:第一行\n第二行", text.Key);
        Eq("文本主行压平换行", "第一行 第二行", text.Title);

        var single = new ClipItem { Kind = ClipKind.Files, Files = [@"C:\a\报表.xlsx"] };
        Eq("单个文件显示文件名", "报表.xlsx", single.Title);
        Eq("单个文件显示所在目录", @"C:\a", single.Subtitle);

        var many = new ClipItem { Kind = ClipKind.Files, Files = [@"C:\a\报表.xlsx", @"C:\b\图.png"] };
        Eq("多个文件显示数量", "报表.xlsx 等 2 个文件", many.Title);
        Check("文件列表顺序不同即不同条目", single.Key != many.Key);
        Check("相同文件列表去重键一致", many.Key == new ClipItem { Kind = ClipKind.Files, Files = [@"C:\a\报表.xlsx", @"C:\b\图.png"] }.Key);

        var noFiles = new ClipItem { Kind = ClipKind.Files, Files = [] };
        Check("空文件列表不可用", !noFiles.IsUsable);

        var image = new ClipItem { Kind = ClipKind.Image, ImageFile = "abc.png", PixelWidth = 360, PixelHeight = 220 };
        Eq("图片主行显示尺寸", "360 × 220 像素", image.Title);
        Eq("图片去重键", "I:abc.png", image.Key);
        Check("图片文件不存在时不可用", !image.IsUsable);
    }

    // ---------- 历史 ----------

    private static void HistoryChecks()
    {
        Section("HistoryStore");

        var settings = new Settings { HistoryLimit = 30 };
        var history = new HistoryStore(settings);

        Check("初始为空", history.IsEmpty);
        history.Add(new ClipItem { Kind = ClipKind.Text, Text = "一" });
        history.Add(new ClipItem { Kind = ClipKind.Text, Text = "二" });
        history.Add(new ClipItem { Kind = ClipKind.Text, Text = "三" });

        var items = history.Snapshot();
        Eq("新条目排在最前", "三", items[0].Text);
        Eq("条数", 3, items.Count);
        Check("同内容不重复记录", history.Add(new ClipItem { Kind = ClipKind.Text, Text = "二" }) && history.Snapshot().Count == 3);
        Eq("重复的内容被提到最前", "二", history.Snapshot()[0].Text);

        settings.HistoryLimit = 2;
        history.Add(new ClipItem { Kind = ClipKind.Text, Text = "四" });
        Eq("超出上限后裁剪到上限", 2, history.Snapshot().Count);
        Eq("保留最新的两条", "四", history.Snapshot()[0].Text);

        Check("空文本不记录", !history.Add(new ClipItem { Kind = ClipKind.Text, Text = "   " }));

        string path = HistoryStore.FilePath;
        Check("落盘文件已写出", WaitFor(() => File.Exists(path), 3000));

        var reloaded = new HistoryStore(settings);
        Check("重新载入内容与内存一致", WaitFor(
            () =>
            {
                reloaded.Load();
                return reloaded.Snapshot() is [{ Text: "四" }, { Text: "二" }];
            },
            3000));

        reloaded.Clear();
        Check("清空后为空", reloaded.Snapshot().Count == 0);
    }

    // ---------- 图片落盘与清理 ----------

    private static void ImageStoreChecks()
    {
        Section("ImageStore");

        var first = Solid(8, 8, Colors.MediumPurple);
        var second = Solid(8, 8, Colors.Teal);

        var saved = ImageStore.Save(first);
        Check("图片编码落盘成功", saved is not null);
        Check("文件存在", ImageStore.Exists(saved!.Value.File));
        Eq("记录像素尺寸", 8, saved.Value.Width);

        var again = ImageStore.Save(Solid(8, 8, Colors.MediumPurple));
        Eq("同内容图片复用同一个文件", saved.Value.File, again!.Value.File);

        var other = ImageStore.Save(second);
        Check("不同内容图片是不同文件", other!.Value.File != saved.Value.File);

        Check("能按缩略图尺寸解码", ImageStore.Load(saved.Value.File, 4)?.PixelWidth == 4);

        var history = new HistoryStore(new Settings { HistoryLimit = 1 });
        history.Add(new ClipItem { Kind = ClipKind.Image, ImageFile = saved.Value.File, PixelWidth = 8, PixelHeight = 8 });
        history.Add(new ClipItem { Kind = ClipKind.Image, ImageFile = other.Value.File, PixelWidth = 8, PixelHeight = 8 });

        Check("被裁掉的图片文件被清掉", WaitFor(() => !ImageStore.Exists(saved.Value.File), 3000));
        Check("仍在历史里的图片文件保留", ImageStore.Exists(other.Value.File));

        history.Clear();
        Check("清空历史会清空图片目录", WaitFor(() => !ImageStore.Exists(other.Value.File), 3000));
    }

    // ---------- 选择框摆放 ----------

    private static void PickerPlacementChecks()
    {
        Section("PickerPlacement");

        // 1000×800 的工作区；框连阴影 460×300，四周阴影留白 14（与 PickerWindow.ShadowMargin 一致）。
        var work = new Win32.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 800 };

        Eq("指针下方放得下就贴下方", (486, 292), Placed(500, 300, work));
        Eq("贴右边时整框夹回工作区", (540, 292), Placed(950, 300, work));
        Eq("下方放不下就翻到指针上方", (486, 408), Placed(500, 700, work));
        Eq("鼠标在卡片外的空隙里，不压卡片", (486, 212), Placed(500, 220, work));
        Eq("框比工作区还高时挪到指针的空侧", (492, 0), TallPlaced(500, 300));

        // 最高 560 的框落到 300 高的工作区：贴底被夹回来正好罩住指针，必须换到指针上方。
        static (int X, int Y) TallPlaced(int x, int y) =>
            PickerPlacement.Choose(new Win32.POINT(x, y), 460, 620, 14, new Win32.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 300 });
    }

    private static (int X, int Y) Placed(int x, int y, Win32.RECT work) =>
        PickerPlacement.Choose(new Win32.POINT(x, y), 460, 300, 14, work);

    // ---------- 小工具 ----------

    private static WriteableBitmap Solid(int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        for (int waited = 0; waited < milliseconds; waited += 50)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    private static void Section(string name) => Console.WriteLine($"[{name}]");

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {name}");
        if (!ok) _failed++;
    }

    private static void Eq<T>(string name, T expected, T actual) =>
        Check($"{name}｜期望 {expected}，实际 {actual}", EqualityComparer<T>.Default.Equals(expected, actual));
}
