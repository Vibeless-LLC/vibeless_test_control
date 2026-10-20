using System.Windows;

namespace Gridline.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
