using System.Management;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Crée un point de restauration Windows avant une action système risquée
/// (Boost, Débloat). Windows limite la fréquence réelle de création à une
/// fois toutes les 24h par défaut (SystemRestorePointCreationFrequency) :
/// un appel rapproché retourne succès sans rien créer de nouveau, ce qui
/// est un comportement normal de l'OS, pas une erreur de cette méthode.
/// Nécessite la Protection du système activée sur le disque système et des
/// droits administrateur (déjà requis par l'app via app.manifest).
/// </summary>
public sealed class RestorePointService
{
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public (bool Success, string Message) CreateRestorePoint(string description)
    {
        try
        {
            using var restoreClass = new ManagementClass(@"\\.\root\default:SystemRestore");
            using var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = ModifySettings;
            inParams["EventType"] = BeginSystemChange;

            using var outParams = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
            var returnValue = outParams is null ? 1u : Convert.ToUInt32(outParams["ReturnValue"]);

            if (returnValue == 0)
            {
                Infrastructure.AppLog.Info($"Point de restauration créé : {description}");
                return (true, "Point de restauration créé (ou déjà existant dans les dernières 24h).");
            }

            Infrastructure.AppLog.Warn($"Création du point de restauration refusée par Windows (code {returnValue})");
            return (false, "Windows a refusé la création (Protection du système désactivée sur ce disque ?).");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Création du point de restauration impossible", ex);
            return (false, $"Impossible de créer le point de restauration : {ex.Message}");
        }
    }
}
