using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Décision pure "faut-il alerter sur une connexion Wi-Fi pendant une
/// partie ?", isolée d'AlertNotificationService pour rester testable sans
/// dépendre du Windows App SDK (Microsoft.Windows.AppNotifications).
/// </summary>
public static class WifiAlertPolicy
{
    public static bool ShouldAlert(ActiveGameSession? activeGame, ActiveConnectionKind connectionKind, bool toastsEnabled)
        => toastsEnabled && activeGame is not null && connectionKind == ActiveConnectionKind.Wireless;
}
