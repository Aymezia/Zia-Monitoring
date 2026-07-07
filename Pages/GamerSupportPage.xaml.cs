using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Pages;

public sealed partial class GamerSupportPage : Page
{
    private readonly App _app;
    private AutoCollectReport? _latestCollect;
    private IReadOnlyList<SuggestedFix> _latestSuggestions = Array.Empty<SuggestedFix>();

    public GamerSupportPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        LoadSessions();
        LoadGraphicsPreset();
        InitializeWrappedYears();
    }

    private void InitializeWrappedYears()
    {
        var currentYear = DateTime.Now.Year;
        for (var year = currentYear; year >= currentYear - 4; year--)
            WrappedYearCombo.Items.Add(new ComboBoxItem { Content = year.ToString() });

        WrappedYearCombo.SelectedIndex = 0;
    }

    private void GenerateWrapped_Click(object sender, RoutedEventArgs e)
    {
        var yearText = (WrappedYearCombo.SelectedItem as ComboBoxItem)?.Content as string;
        if (!int.TryParse(yearText, out var year))
            return;

        try
        {
            var summary = _app.GameSessions.GetYearlyWrapped(year);
            if (summary is null)
            {
                WrappedContent.Visibility = Visibility.Collapsed;
                WrappedStatusLabel.Text = $"Aucune session enregistrée en {year}.";
                return;
            }

            WrappedPlaytimeLabel.Text = summary.PlaytimeLabel;
            WrappedMostPlayedLabel.Text = summary.MostPlayedLabel;
            WrappedSessionsLabel.Text = summary.TotalSessions.ToString();
            WrappedFpsLabel.Text = summary.AvgFpsLabel;
            WrappedTempLabel.Text = summary.MaxTempLabel;
            WrappedLongestLabel.Text = summary.LongestSessionLabel;

            WrappedContent.Visibility = Visibility.Visible;
            WrappedStatusLabel.Text = $"Récapitulatif {year} généré.";
        }
        catch (Exception ex)
        {
            WrappedStatusLabel.Text = "Génération impossible.";
            Infrastructure.AppLog.Warn("Génération du récap annuel en échec", ex);
        }
    }

    private void LoadGraphicsPreset()
    {
        try
        {
            var state = _app.State;
            var tier = _app.GraphicsPreset.DetectTier(state.VramTotalMb, state.LogicalCores, state.InstalledRamGb);
            var preset = _app.GraphicsPreset.GetPreset(tier);

            HardwareTierLabel.Text = preset.TierLabel;
            PresetResolutionLabel.Text = preset.Resolution;
            PresetTextureLabel.Text = preset.TextureQuality;
            PresetShadowLabel.Text = preset.ShadowQuality;
            PresetAaLabel.Text = preset.AntiAliasing;
            PresetEffectsLabel.Text = preset.EffectsQuality;
            PresetFpsLabel.Text = preset.TargetFps;
            PresetNotesLabel.Text = preset.Notes;
            CompetitiveTipLabel.Text = ZiaMonitoring_App.Application.GraphicsPresetService.CompetitiveGamingTip;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Calcul du preset graphique impossible", ex);
        }
    }

    private void RefreshSessions_Click(object sender, RoutedEventArgs e) => LoadSessions();

    private async void InstallPresentMon_Click(object sender, RoutedEventArgs e)
    {
        InstallPresentMonButton.IsEnabled = false;
        PresentMonStatusLabel.Text = "Téléchargement de PresentMon…";
        try
        {
            var (success, message) = await _app.PresentMon.DownloadAsync();
            PresentMonStatusLabel.Text = message;
            UpdatePresentMonStatus();
        }
        finally
        {
            InstallPresentMonButton.IsEnabled = true;
        }
    }

    private void UpdatePresentMonStatus()
    {
        if (_app.PresentMon.IsAvailable)
        {
            InstallPresentMonButton.Visibility = Visibility.Collapsed;
            PresentMonStatusLabel.Text = _app.PresentMon.IsRunning
                ? "FPS réels : capture PresentMon en cours"
                : "FPS réels : PresentMon prêt (capture au prochain jeu)";
        }
        else
        {
            InstallPresentMonButton.Visibility = Visibility.Visible;
            PresentMonStatusLabel.Text = "FPS estimés (compteurs GPU)";
        }
    }

    private void LoadSessions()
    {
        try
        {
            SessionsList.ItemsSource = _app.GameSessions.GetRecentSessions();

            var weekly = _app.GameSessions.GetWeeklyPlaytime();
            var byGame = _app.GameSessions.GetWeeklyPlaytimeByGame();
            var goal = _app.SettingsService.Load().WeeklyPlaytimeGoalHours;

            var summary = $"Cette semaine : {(int)weekly.TotalHours}h{weekly.Minutes:D2}";
            if (goal > 0)
                summary += $" / objectif {goal:F0} h";
            if (byGame.Count > 0)
                summary += "  —  " + string.Join(", ", byGame.Take(3).Select(g => $"{g.Game} {(int)g.Total.TotalHours}h{g.Total.Minutes:D2}"));

            WeeklyPlaytimeLabel.Text = summary;
            UpdatePresentMonStatus();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Chargement des sessions de jeu impossible", ex);
        }
    }

    private async void AutoCollect_Click(object sender, RoutedEventArgs e)
    {
        var product = GetSelectedProduct();
        _latestCollect = _app.GamerTroubleshootingService.AutoCollectLogs(product);

        var observations = new List<string>
        {
            $"Auto collect execute pour {product} a {_latestCollect.CollectedAt:HH:mm:ss}",
            $"OBS findings: {_latestCollect.ObsLogFindings.Count}",
            $"Windows events: {_latestCollect.WindowsEvents.Count}"
        };

        observations.AddRange(_latestCollect.CorrelationTags.Select(x => $"Tag: {x}"));
        ObservationsList.ItemsSource = observations;
        SourcesList.ItemsSource = _latestCollect.UsefulLinks;

        await ShowMessageAsync("Auto collect", "Collecte des logs terminee. Tu peux lancer un diagnostic maintenant.");
    }

    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        var product = GetSelectedProduct();
        var query = ErrorText.Text?.Trim() ?? string.Empty;

        var report = _app.GamerTroubleshootingService.Diagnose(product, query, _latestCollect);
        _latestSuggestions = report.Suggestions;

        SuggestionsList.ItemsSource = report.Suggestions.Select(x => new
        {
            x.Title,
            x.Why,
            ConfidenceScore = $"Score confiance: {x.ConfidenceScore}%",
            ConfidenceLabel = $"Niveau: {x.ConfidenceLabel}",
            SafeActionKey = x.IsSafeOneClick ? $"Safe fix: {x.SafeActionKey}" : "Safe fix: non disponible"
        }).ToList();

        ObservationsList.ItemsSource = report.Observations;
        SourcesList.ItemsSource = report.Sources;

        await ShowMessageAsync("Diagnostic termine", $"{report.Suggestions.Count} solution(s) triee(s) par probabilite.");
    }

    private async void ApplyBestSafeFix_Click(object sender, RoutedEventArgs e)
    {
        var best = _latestSuggestions.FirstOrDefault(x => x.IsSafeOneClick && !string.IsNullOrWhiteSpace(x.SafeActionKey));
        if (best is null)
        {
            await ShowMessageAsync("Safe fix", "Aucun correctif safe disponible pour le contexte actuel.");
            return;
        }

        var result = _app.GamerTroubleshootingService.ApplySafeFix(best.SafeActionKey!);
        await ShowMessageAsync("Safe fix", $"{result.Message} (success: {result.Success})");
    }

    private string GetSelectedProduct()
    {
        if (ProductCombo.SelectedItem is ComboBoxItem item && item.Content is string content)
        {
            return content;
        }

        return "Valorant";
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "OK",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}
