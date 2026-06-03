using System.Windows;
using System.Windows.Controls;

namespace FileUnlocker;

public partial class SettingsWindow : Window
{
    public bool LanguageChanged { get; private set; }
    private bool _themeChanged;

    private Settings.Language _origLang;
    private Settings.AppTheme _origTheme;

    public SettingsWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        _origLang = Settings.Lang;
        _origTheme = Settings.Theme;
        LoadCombos();
        ApplyLanguage();
    }

    private void LoadCombos()
    {
        LangCombo.Items.Clear();
        LangCombo.Items.Add(L.LangZh);
        LangCombo.Items.Add(L.LangEn);
        LangCombo.SelectedIndex = Settings.Lang == Settings.Language.Zh ? 0 : 1;

        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(L.ThemeSystem);
        ThemeCombo.Items.Add(L.ThemeLight);
        ThemeCombo.Items.Add(L.ThemeDark);
        ThemeCombo.SelectedIndex = Settings.Theme switch
        {
            Settings.AppTheme.Light => 1,
            Settings.AppTheme.Dark => 2,
            _ => 0
        };
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var newLang = LangCombo.SelectedIndex == 1 ? Settings.Language.En : Settings.Language.Zh;
        var newTheme = ThemeCombo.SelectedIndex switch
        {
            1 => Settings.AppTheme.Light,
            2 => Settings.AppTheme.Dark,
            _ => Settings.AppTheme.System
        };

        if (newLang != Settings.Lang)
        {
            Settings.Lang = newLang;
            LanguageChanged = true;
        }

        if (newTheme != Settings.Theme)
        {
            Settings.Theme = newTheme;
            _themeChanged = true;
        }

        Settings.Save();

        if (_themeChanged)
            App.ApplyTheme();

        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplyLanguage()
    {
        Title = L.SettingsTitle;
        LangLabel.Text = L.SettingsLangLabel;
        ThemeLabel.Text = L.SettingsThemeLabel;
        SaveBtn.Content = L.Save;
        CancelBtn.Content = L.Cancel;
    }
}
