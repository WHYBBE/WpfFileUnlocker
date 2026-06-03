using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileUnlocker;

public partial class MainWindow : Window
{
    private SolidColorBrush ColorMuted => (SolidColorBrush)FindResource("TextFillColorSecondaryBrush");
    private SolidColorBrush ColorSuccess => (SolidColorBrush)FindResource("SystemFillColorSuccessBrush");
    private SolidColorBrush ColorDanger => (SolidColorBrush)FindResource("SystemFillColorCriticalBrush");

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _loadingTimer;

    public MainWindow()
    {
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
        SettingsBtn.ToolTip = L.SettingsTitle;

        BrowseFileBtn.Content = L.BrowseFile;
        BrowseFolderBtn.Content = L.BrowseFolder;
        DetectBtn.Content = L.Detect;
        ScanDepthLabel.Text = L.ScanDepthLabel;
        ScanCurrent.Content = L.ScanCurrent;
        ScanOneLevel.Content = L.ScanOneLevel;
        ScanRecursive.Content = L.ScanRecursive;
        IgnoreGit.Content = L.SkipGit;

        if (EmptyPanel.Visibility == Visibility.Visible && LoadingSpinner.Visibility != Visibility.Visible)
            EmptyText.Text = L.DragHint;

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
    }

    private enum StatusType { Ready, Muted, Success, Danger }

    private void SetStatus(string text, StatusType type)
    {
        BottomStatus.Text = text;
        BottomStatus.Foreground = type switch
        {
            StatusType.Success => ColorSuccess,
            StatusType.Danger => ColorDanger,
            _ => ColorMuted
        };
    }

    private void ShowEmptyState(string icon, string text)
    {
        EmptyIcon.Text = icon;
        EmptyText.Text = text;
        EmptyIcon.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Visible;
        LoadingSpinner.Visibility = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    private void ShowLoadingState()
    {
        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _loadingTimer.Tick += (_, _) =>
        {
            _loadingTimer.Stop();
            EmptyIcon.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            LoadingSpinner.Visibility = Visibility.Visible;
            LoadingText.Text = L.Detecting;
            LoadingText.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Visible;
        };
        _loadingTimer.Start();
    }

    private void HideLoading()
    {
        _loadingTimer?.Stop();
        _loadingTimer = null;
        EmptyPanel.Visibility = Visibility.Collapsed;
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(this);
        win.ShowDialog();
        if (win.LanguageChanged)
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
            SetStatus(L.EnterPath, StatusType.Danger);
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
            SetStatus(L.PathNotExist + path, StatusType.Danger);
            ResultList.Items.Clear();
            ShowEmptyState("&#xE783;", L.PathNotExist);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        ResultList.Items.Clear();
        SetStatus(L.Detecting, StatusType.Muted);
        ShowLoadingState();

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

            HideLoading();

            if (locks.Count == 0)
            {
                SetStatus(isDir ? L.NotOccupiedFolder : L.NotOccupiedFile, StatusType.Success);
                ShowEmptyState("&#xE73E;", isDir ? L.NotOccupiedFolder : L.NotOccupiedFile);
            }
            else
            {
                var label = isDir ? L.Folder : L.File;
                SetStatus(L.FoundLocks(locks.Count) + label, StatusType.Danger);
                foreach (var info in locks)
                    ResultList.Items.Add(info);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            HideLoading();
            SetStatus(L.DetectError(ex.Message), StatusType.Danger);
            ShowEmptyState("&#xE783;", L.DetectFailed);
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
            SetStatus(L.KilledProcess(name, pid), StatusType.Success);

            var currentPath = FilePathBox.Text.Trim();
            if (!string.IsNullOrEmpty(currentPath))
            {
                ResultList.Items.Clear();
                DoDetect(currentPath);
            }
        }
        catch (Exception ex)
        {
            SetStatus(L.KillFailed(ex.Message), StatusType.Danger);
        }
    }

    private sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Owner(Window owner) => Handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
    }
}
