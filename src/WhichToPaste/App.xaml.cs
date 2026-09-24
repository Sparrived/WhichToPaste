using System.Windows;

namespace WhichToPaste;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\WhichToPaste.SingleInstance";

    private Mutex? _instanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool created);
        if (!created)
        {
            // 已经在托盘里跑着了：把它唤到前台再退出，避免出现两个托盘图标。
            AppController.SignalExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true; // 常驻小工具不该因为一次异常就消失
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void Log(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            Directory.CreateDirectory(Services.SettingsStore.DataDirectory);
            File.AppendAllText(
                Path.Combine(Services.SettingsStore.DataDirectory, "error.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}