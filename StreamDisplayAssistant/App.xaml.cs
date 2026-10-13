using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace StreamDisplayAssistant;

public partial class App : System.Windows.Application
{
    public App()
    {
        LogTrace("App.ctor started");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogException("AppDomain.UnhandledException", ex);
            System.Windows.MessageBox.Show(
                $"Fatal System Error:\n\n{ex?.Message}\n\n{ex?.StackTrace}",
                "StreamDisplay Assistant - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogException("DispatcherUnhandledException", e.Exception);
            System.Windows.MessageBox.Show(
                $"Application Error:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "StreamDisplay Assistant - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogTrace($"OnStartup started. Args: {string.Join(" ", e.Args)}");
        base.OnStartup(e);

        try
        {
            bool startMinimized = e.Args.Any(a =>
                a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-minimized", StringComparison.OrdinalIgnoreCase));

            LogTrace($"Creating MainWindow (startMinimized={startMinimized})...");
            var mainWindow = new MainWindow(startMinimized);
            MainWindow = mainWindow;
            LogTrace("MainWindow created successfully.");

            if (!startMinimized)
            {
                LogTrace("Calling mainWindow.Show()...");
                mainWindow.Show();
                mainWindow.Activate();
                LogTrace("mainWindow.Show() completed.");
            }
            else
            {
                LogTrace("MainWindow started minimized to tray.");
            }
        }
        catch (Exception ex)
        {
            LogException("OnStartup.Catch", ex);
            System.Windows.MessageBox.Show(
                $"Startup Error:\n\n{ex.Message}\n\nDetails:\n{ex.StackTrace}",
                "StreamDisplay Assistant - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogTrace($"Application OnExit triggered with code {e.ApplicationExitCode}. Call stack:\n{Environment.StackTrace}");
        base.OnExit(e);
    }

    public static void LogTrace(string msg)
    {
        try
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDisplayAssistant");
            Directory.CreateDirectory(appData);
            string file = Path.Combine(appData, "debug_trace.log");
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TRACE] {msg}\r\n");
        }
        catch { }
    }

    private static void LogException(string source, Exception? ex)
    {
        try
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDisplayAssistant");
            Directory.CreateDirectory(appData);
            string file = Path.Combine(appData, "startup_error.log");
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex?.ToString()}\n--------------------------------------------------\n";
            File.AppendAllText(file, msg);
        }
        catch { }
    }
}
