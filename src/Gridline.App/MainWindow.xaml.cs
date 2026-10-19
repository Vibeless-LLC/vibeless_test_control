using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Gridline.App;

public partial class MainWindow : Window
{
    private const string FileFilter = "Gridline files (*.gridline)|*.gridline|All files (*.*)|*.*";
    private readonly DocumentSession _session;

    public MainWindow() : this(new DocumentSession()) { }

    public MainWindow(DocumentSession session)
    {
        _session = session;
        InitializeComponent();
        _session.StateChanged += (_, _) => Title = _session.Title;
        Title = _session.Title;
    }

    private void OnNew(object s, ExecutedRoutedEventArgs e) => _session.New();

    private void OnOpen(object s, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        if (dlg.ShowDialog(this) != true) return;
        Run(() => _session.Open(dlg.FileName));
    }

    private void OnSave(object s, ExecutedRoutedEventArgs e)
    {
        if (_session.FilePath is null) OnSaveAs(s, e);
        else Run(_session.Save);
    }

    private void OnSaveAs(object s, ExecutedRoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = FileFilter, DefaultExt = ".gridline" };
        if (dlg.ShowDialog(this) != true) return;
        Run(() => _session.SaveAs(dlg.FileName));
    }

    private void OnUndo(object s, ExecutedRoutedEventArgs e) => _session.Workbook.Undo();
    private void OnRedo(object s, ExecutedRoutedEventArgs e) => _session.Workbook.Redo();
    private void CanUndo(object s, CanExecuteRoutedEventArgs e) => e.CanExecute = _session.Workbook.CanUndo;
    private void CanRedo(object s, CanExecuteRoutedEventArgs e) => e.CanExecute = _session.Workbook.CanRedo;
    private void OnExit(object s, ExecutedRoutedEventArgs e) => Close();

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or FormatException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Gridline", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
