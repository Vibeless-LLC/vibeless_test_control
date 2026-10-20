using System.IO;
using System.Windows;

namespace Gridline.App;

public partial class App : Application
{
    /// <summary>Best-effort append to %LOCALAPPDATA%\Gridline\error.log; never throws.</summary>
    private static void LogUnhandled(string source, Exception? ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gridline");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"),
                DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " [" + source + "] " + ex + "\n");
        }
        catch { /* logging must not fail the handler */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Last-chance handlers (§11): UI-thread exceptions are reported and swallowed; others are only logged.
        DispatcherUnhandledException += (_, args) =>
        {
            LogUnhandled("Dispatcher", args.Exception);
            args.Handled = true;
            try { MessageBox.Show("Unexpected error: " + args.Exception.Message, "Gridline", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { /* nothing more can be done */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnhandled("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => LogUnhandled("Task", args.Exception);
        var session = new DocumentSession();
        if (e.Args.Length > 0)
        {
            try { session.Open(e.Args[0]); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open " + e.Args[0] + ": " + ex.Message, "Gridline",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        var window = new MainWindow(session, new RecoveryService(RecoveryService.ResolveDir(), session.SessionId));
        window.Show();
        window.Dispatcher.BeginInvoke(window.OfferRecovery, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
