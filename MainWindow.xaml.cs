using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileUnlocker;

public partial class MainWindow : Window
{
    private SolidColorBrush ColorMuted => (SolidColorBrush)FindResource("TextFillColorSecondaryBrush");
    private SolidColorBrush ColorSuccess => (SolidColorBrush)FindResource("SystemFillColorSuccessBrush");
    private SolidColorBrush ColorDanger => (SolidColorBrush)FindResource("SystemFillColorCriticalBrush");

    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        Settings.Load();
        InitializeComponent();
        ApplySettings();
        ApplyLanguage();
    }

    private void ApplySettings()
    {
        ScanCurrent.IsChecked = Settings.ScanDepth == RestartManager.ScanDepth.CurrentOnly;
        ScanOneLevel.IsChecked = Settings.ScanDepth == RestartManager.ScanDepth.OneLevel;
        ScanRecursive.IsChecked = Settings.ScanDepth == RestartManager.ScanDepth.Recursive;
        IgnoreGit.IsChecked = Settings.IgnoreGit;
    }

    private void SaveSettings()
    {
        Settings.ScanDepth = ScanRecursive.IsChecked == true ? RestartManager.ScanDepth.Recursive
            : ScanOneLevel.IsChecked == true ? RestartManager.ScanDepth.OneLevel
            : RestartManager.ScanDepth.CurrentOnly;
        Settings.IgnoreGit = IgnoreGit.IsChecked == true;
        Settings.Save();
    }

    private void ApplyLanguage()
    {
        Title = L.AppTitle;
        LangToggle.Content = L.Language;

        BrowseFileBtn.Content = L.BrowseFile;
        BrowseFolderBtn.Content = L.BrowseFolder;
        DetectBtn.Content = L.Detect;
        StatusText.Text = L.DragHint;
        ScanDepthLabel.Text = L.ScanDepthLabel;
        ScanCurrent.Content = L.ScanCurrent;
        ScanOneLevel.Content = L.ScanOneLevel;
        ScanRecursive.Content = L.ScanRecursive;
        IgnoreGit.Content = L.SkipGit;
        BottomStatus.Text = L.Ready;

        var view = ResultList.View as GridView;
        if (view != null)
        {
            view.Columns[0].Header = L.ColPID;
            view.Columns[1].Header = L.ColProcess;
            view.Columns[2].Header = L.ColApp;
            view.Columns[3].Header = L.ColLockedFile;
            view.Columns[4].Header = L.ColAction;
        }

        foreach (var item in ResultList.Items)
            if (item is RestartManager.LockInfo info)
                RefreshRow(info);
    }

    private void RefreshRow(RestartManager.LockInfo info)
    {
        // Items remain the same data, just language in Kill button updates via template
    }

    private void LangToggle_Click(object sender, RoutedEventArgs e)
    {
        Settings.Lang = Settings.Lang == Settings.Language.Zh ? Settings.Language.En : Settings.Language.Zh;
        Settings.Save();
        ApplyLanguage();
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.SelectFileTitle, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            FilePathBox.Text = dialog.FileName;
            DoDetect(dialog.FileName);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = L.SelectFolderTitle,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(new Win32Owner(this)) == System.Windows.Forms.DialogResult.OK)
        {
            FilePathBox.Text = dialog.SelectedPath;
            DoDetect(dialog.SelectedPath);
        }
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = L.EnterPath;
            StatusText.Foreground = ColorDanger;
            return;
        }
        DoDetect(path);
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files.Length > 0)
            {
                FilePathBox.Text = files[0];
                DoDetect(files[0]);
            }
        }
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void DoDetect(string path)
    {
        var isDir = Directory.Exists(path);
        var isFile = File.Exists(path);

        if (!isFile && !isDir)
        {
            StatusText.Text = L.PathNotExist + path;
            StatusText.Foreground = ColorDanger;
            ResultList.Items.Clear();
            BottomStatus.Text = L.PathNotExistShort;
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        StatusText.Text = L.Detecting;
        StatusText.Foreground = ColorMuted;
        ResultList.Items.Clear();
        BottomStatus.Text = L.DetectingStatus;

        var scanDepth = ScanRecursive.IsChecked == true ? RestartManager.ScanDepth.Recursive
            : ScanOneLevel.IsChecked == true ? RestartManager.ScanDepth.OneLevel
            : RestartManager.ScanDepth.CurrentOnly;
        var ignoreGit = IgnoreGit.IsChecked == true;

        SaveSettings();

        try
        {
            var locks = await Task.Run(() =>
                isDir
                    ? RestartManager.GetLockingProcessesForFolder(path, scanDepth, ignoreGit)
                    : RestartManager.GetLockingProcesses(path), token);

            if (token.IsCancellationRequested) return;

            if (locks.Count == 0)
            {
                StatusText.Text = isDir ? L.NotOccupiedFolder : L.NotOccupiedFile;
                StatusText.Foreground = ColorSuccess;
                BottomStatus.Text = L.Idle;
            }
            else
            {
                var label = isDir ? L.Folder : L.File;
                StatusText.Text = L.FoundLocks(locks.Count) + label;
                StatusText.Foreground = ColorDanger;
                foreach (var info in locks)
                    ResultList.Items.Add(info);
                BottomStatus.Text = L.TotalLocks(locks.Count);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            StatusText.Text = L.DetectError(ex.Message);
            StatusText.Foreground = ColorDanger;
            BottomStatus.Text = L.DetectFailed;
        }
    }

    private void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int pid }) return;

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            var name = process.ProcessName;
            process.Kill();
            StatusText.Text = L.KilledProcess(name, pid);
            StatusText.Foreground = ColorSuccess;

            var currentPath = FilePathBox.Text.Trim();
            if (!string.IsNullOrEmpty(currentPath))
            {
                ResultList.Items.Clear();
                DoDetect(currentPath);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = L.KillFailed(ex.Message);
            StatusText.Foreground = ColorDanger;
        }
    }

    private sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Owner(Window owner) => Handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
    }
}
