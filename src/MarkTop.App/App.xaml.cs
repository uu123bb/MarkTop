using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MarkTop.App;

public partial class App : Application
{
    /// <summary>Set by <c>--self-test &lt;dir&gt;</c>; runs an automated end-to-end check and exits.</summary>
    public static string? SelfTestDirectory { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var index = Array.IndexOf(e.Args, "--self-test");
        if (index >= 0 && index + 1 < e.Args.Length)
        {
            SelfTestDirectory = Path.GetFullPath(e.Args[index + 1]);
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log(exception);
            }
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(
            $"MarkTop 遇到未处理的错误：\n\n{e.Exception.Message}\n\n日志已写入 %LOCALAPPDATA%\\MarkTop\\logs。",
            "MarkTop",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(Exception exception)
    {
        LogInfo(exception.ToString());
    }

    /// <summary>Diagnostics go to %LOCALAPPDATA%\MarkTop\logs\app.log — never next to the exe.</summary>
    public static void LogInfo(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkTop", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "app.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
