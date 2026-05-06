using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FileUnlocker;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush ColorMuted = new(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush ColorSuccess = new(System.Windows.Media.Color.FromRgb(0x10, 0x89, 0x3e));
    private static readonly SolidColorBrush ColorDanger = new(System.Windows.Media.Color.FromRgb(0xe8, 0x11, 0x23));

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要检测的文件",
            CheckFileExists = true
        };

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
            Description = "选择要检测的文件夹",
            UseDescriptionForTitle = true
        };

        var win32Owner = new Win32Owner(this);
        if (dialog.ShowDialog(win32Owner) == System.Windows.Forms.DialogResult.OK)
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
            StatusText.Text = "请输入或选择文件/文件夹路径";
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

    private void DoDetect(string path)
    {
        var isDir = System.IO.Directory.Exists(path);
        var isFile = System.IO.File.Exists(path);

        if (!isFile && !isDir)
        {
            StatusText.Text = "路径不存在: " + path;
            StatusText.Foreground = ColorDanger;
            ResultList.Items.Clear();
            BottomStatus.Text = "路径不存在";
            return;
        }

        StatusText.Text = "正在检测...";
        StatusText.Foreground = ColorMuted;
        ResultList.Items.Clear();
        BottomStatus.Text = "检测中...";

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var locks = isDir
                    ? RestartManager.GetLockingProcessesForFolder(path)
                    : RestartManager.GetLockingProcesses(path);

                if (locks.Count == 0)
                {
                    StatusText.Text = isDir ? "该文件夹未被任何进程占用" : "该文件未被任何进程占用";
                    StatusText.Foreground = ColorSuccess;
                    BottomStatus.Text = "空闲，无占用";
                }
                else
                {
                    var label = isDir ? "文件夹" : "文件";
                    StatusText.Text = $"发现 {locks.Count} 个进程占用该{label}";
                    StatusText.Foreground = ColorDanger;
                    foreach (var info in locks)
                        ResultList.Items.Add(info);
                    BottomStatus.Text = $"共 {locks.Count} 个进程占用";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "检测出错: " + ex.Message;
                StatusText.Foreground = ColorDanger;
                BottomStatus.Text = "检测失败";
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int pid }) return;

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            var name = process.ProcessName;
            process.Kill();
            StatusText.Text = $"已结束进程 {name} (PID: {pid})";
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
            StatusText.Text = $"结束进程失败: {ex.Message}";
            StatusText.Foreground = ColorDanger;
        }
    }

    private sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Owner(Window owner) => Handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
    }
}
