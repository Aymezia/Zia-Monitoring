using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ZiaMonitoring_App.Pages;

public sealed partial class BoostPage : Page
{
    private readonly App _app;
    private Storyboard? _pulseStoryboard;

    public BoostPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        DataContext = _app.State;
        Loaded += BoostPage_Loaded;
    }

    private void BoostPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pulseStoryboard = Resources["PulseStoryboard"] as Storyboard;
        _app.State.SetOptimizationState(false, 0, "Pret");
    }

    private async void GameMode_Click(object sender, RoutedEventArgs e)
    {
        _app.State.SetOptimizationState(true, 0, "Initialisation du moteur boost");
        _pulseStoryboard?.Begin();

        try
        {
            await StepAsync(10, "Analyse de l'etat systeme");
            var preview = await Task.Run(() => _app.BoostEngine.BuildPreview());

            var lines = string.Join(Environment.NewLine, preview.PlannedActions.Select(x => $"- {x}"));
            await ShowMessageAsync(
                "Preview Boost (Mode Jeu)",
                $"{lines}{Environment.NewLine}{Environment.NewLine}Startup candidates: {preview.StartupCandidates.Count}{Environment.NewLine}Service candidates: {preview.ServiceCandidates.Count}");

            await StepAsync(35, "Preparation nettoyage temporaire");
            await StepAsync(55, "Optimisation startup/services");
            var execution = await Task.Run(() => _app.BoostEngine.ExecuteSafeBoost());

            await StepAsync(82, "Verification rollback et securite");

            var applied = string.Join(Environment.NewLine, execution.AppliedActions.Select(x => $"- {x}"));
            var warnings = execution.Warnings.Count == 0
                ? "No warning"
                : string.Join(Environment.NewLine, execution.Warnings.Select(x => $"- {x}"));

            await StepAsync(100, execution.Success ? "Optimisation terminee" : "Optimisation terminee avec avertissements");

            await ShowMessageAsync(
                "Boost Execute",
                $"Success: {execution.Success}{Environment.NewLine}Rollback ID: {execution.RollbackId}{Environment.NewLine}{Environment.NewLine}{applied}{Environment.NewLine}{Environment.NewLine}Warnings:{Environment.NewLine}{warnings}");
        }
        finally
        {
            _pulseStoryboard?.Stop();
            _app.State.SetOptimizationState(false, 100, "Pret");
        }
    }

    private async void WorkMode_Click(object sender, RoutedEventArgs e)
    {
        _app.State.SetOptimizationState(true, 0, "Mode travail: analyse ciblee");
        _pulseStoryboard?.Begin();

        try
        {
            await StepAsync(30, "Lecture des applications demarrage");
            var preview = await Task.Run(() => _app.BoostEngine.BuildPreview());
            await StepAsync(100, "Preview termine");

            var details = string.Join(Environment.NewLine, preview.StartupCandidates.Take(8).Select(x => $"- Startup: {x}"));
            await ShowMessageAsync(
                "Mode Travail - Preview",
                $"Temp candidates: {preview.TempFileCandidates}{Environment.NewLine}Approx size: {preview.TempBytesCandidates / 1024d / 1024d:F1} MB{Environment.NewLine}{Environment.NewLine}{details}");
        }
        finally
        {
            _pulseStoryboard?.Stop();
            _app.State.SetOptimizationState(false, 0, "Pret");
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        var rollback = _app.BoostEngine.RollbackLastBoost();
        var restored = rollback.RestoredActions.Count == 0
            ? "No restored action"
            : string.Join(Environment.NewLine, rollback.RestoredActions.Select(x => $"- {x}"));
        var warnings = rollback.Warnings.Count == 0
            ? "No warning"
            : string.Join(Environment.NewLine, rollback.Warnings.Select(x => $"- {x}"));

        await ShowMessageAsync(
            "Rollback",
            $"Success: {rollback.Success}{Environment.NewLine}Rollback ID: {rollback.RollbackId}{Environment.NewLine}{Environment.NewLine}{restored}{Environment.NewLine}{Environment.NewLine}Warnings:{Environment.NewLine}{warnings}");
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

    private async Task StepAsync(int progress, string stageText)
    {
        _app.State.SetOptimizationState(true, progress, stageText);
        await Task.Delay(220);
    }
}
