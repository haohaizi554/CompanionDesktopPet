using System.Windows;
using System.Windows.Input;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

public partial class DialogueEndpointWindow : Window
{
    private bool _filling;

    internal DialogueEndpointWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = DialogueEndpointSetup.Presets;
        PresetBox.DisplayMemberPath = nameof(DialogueEndpointPreset.Label);
        var existing = PersonaDialoguePaths.ExistingConfigPath();
        if (existing is not null && DialogueEndpointSetup.TryLoad(existing, out var draft))
        {
            PresetBox.SelectedItem = MatchPreset(draft);
            BaseUrlBox.Text = draft.BaseUrl;
            ModelBox.Text = draft.Model;
            ApiKeyBox.Text = draft.ApiKey == "local" ? "" : draft.ApiKey;
            return;
        }

        PresetBox.SelectedIndex = 0;
    }

    private static DialogueEndpointPreset MatchPreset(DialogueEndpointSetup.DialogueEndpointDraft draft)
    {
        var url = DialogueEndpointSetup.NormalizeBaseUrl(draft.BaseUrl);
        var matched = DialogueEndpointSetup.Presets.FirstOrDefault(preset =>
            string.Equals(DialogueEndpointSetup.NormalizeBaseUrl(preset.BaseUrl), url, StringComparison.OrdinalIgnoreCase)
            && preset.Auth == draft.Auth);
        if (matched is not null)
        {
            return matched;
        }

        var customAuth = draft.Auth == DialogueEndpointSetup.Bearer
            ? DialogueEndpointSetup.Bearer
            : DialogueEndpointSetup.ApiKeyHeader;
        return DialogueEndpointSetup.Presets.First(preset =>
            preset.Label.StartsWith("自定义", StringComparison.Ordinal) && preset.Auth == customAuth);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PresetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not DialogueEndpointPreset preset)
        {
            return;
        }

        _filling = true;
        BaseUrlBox.Text = preset.BaseUrl;
        ModelBox.Text = preset.Model;
        ApiKeyBox.Text = preset.ApiKey;
        HintText.Text = preset.Hint;
        ErrorText.Text = "";
        _filling = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        var preset = PresetBox.SelectedItem as DialogueEndpointPreset;
        var baseUrl = DialogueEndpointSetup.NormalizeBaseUrl(BaseUrlBox.Text);
        var model = ModelBox.Text.Trim();
        if (baseUrl.Length == 0 || model.Length == 0)
        {
            ErrorText.Text = "地址和模型名都要填。";
            return;
        }

        DialogueEndpointSetup.Save(
            PersonaDialoguePaths.UserConfigPath,
            baseUrl,
            model,
            ApiKeyBox.Text,
            preset?.Auth ?? DialogueEndpointSetup.Bearer);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
