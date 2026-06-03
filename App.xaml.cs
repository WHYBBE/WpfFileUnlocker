using System.Windows;

namespace FileUnlocker;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings.Load();
        ApplyTheme();
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.ToString(), "未处理异常", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                System.Windows.MessageBox.Show(ex.ToString(), "致命异常", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    public static void ApplyTheme()
    {
        Current.ThemeMode = Settings.Theme switch
        {
            Settings.AppTheme.Light => ThemeMode.Light,
            Settings.AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };
    }
}
