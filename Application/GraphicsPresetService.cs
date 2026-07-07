namespace ZiaMonitoring_App.Application;

public enum HardwareTier { Entry, MidRange, HighEnd, Enthusiast }

public sealed record GraphicsPreset(
    HardwareTier Tier,
    string TierLabel,
    string Resolution,
    string TextureQuality,
    string ShadowQuality,
    string AntiAliasing,
    string EffectsQuality,
    string TargetFps,
    string Notes);

/// <summary>
/// Recommande un preset graphique générique selon le palier matériel détecté
/// (VRAM, cœurs CPU, RAM) — façon « Optimal Playable Settings » de GeForce
/// Experience, mais sans base de données propriétaire par jeu : nous n'avons
/// pas accès à des benchmarks vérifiés par jeu+GPU, donc les recommandations
/// portent sur des réglages génériques (résolution, textures, ombres...)
/// dont le rapport qualité/coût est documenté de façon stable, pas des
/// chiffres de performance spécifiques à un jeu que nous ne pouvons pas
/// vérifier.
/// </summary>
public sealed class GraphicsPresetService
{
    public const string CompetitiveGamingTip =
        "Pour les jeux compétitifs (Valorant, CS2, Apex, Fortnite en mode compétitif), un framerate élevé et stable compte plus que la qualité visuelle : baissez ombres et effets même sur du matériel haut de gamme.";

    public HardwareTier DetectTier(double vramTotalMb, int logicalCores, double totalRamGb)
    {
        var score = 0;

        score += vramTotalMb switch
        {
            >= 16000 => 40,
            >= 10000 => 30,
            >= 6000 => 20,
            >= 4000 => 10,
            _ => 0
        };

        score += logicalCores switch
        {
            >= 16 => 20,
            >= 8 => 15,
            >= 6 => 10,
            >= 4 => 5,
            _ => 0
        };

        score += totalRamGb switch
        {
            >= 32 => 20,
            >= 16 => 15,
            >= 8 => 5,
            _ => 0
        };

        return score switch
        {
            >= 70 => HardwareTier.Enthusiast,
            >= 45 => HardwareTier.HighEnd,
            >= 25 => HardwareTier.MidRange,
            _ => HardwareTier.Entry
        };
    }

    public GraphicsPreset GetPreset(HardwareTier tier) => tier switch
    {
        HardwareTier.Entry => new GraphicsPreset(tier, "Entrée de gamme",
            "1080p (900p si besoin)", "Basse/Moyenne", "Basse", "Désactivé ou FXAA", "Basse", "60 fps stable",
            "Priorisez la fluidité : ombres et post-traitement sont les réglages les plus coûteux pour le moins d'impact visuel en jeu."),

        HardwareTier.MidRange => new GraphicsPreset(tier, "Milieu de gamme",
            "1080p / 1440p", "Moyenne/Haute", "Moyenne", "TAA ou FXAA", "Moyenne", "60-100 fps",
            "Les textures ont un fort impact visuel pour un coût faible si la VRAM suit : montez-les en priorité."),

        HardwareTier.HighEnd => new GraphicsPreset(tier, "Haut de gamme",
            "1440p", "Haute/Ultra", "Haute", "TAA", "Haute", "100+ fps",
            "Ray tracing activable au cas par cas selon l'impact sur le framerate du jeu concerné."),

        HardwareTier.Enthusiast => new GraphicsPreset(tier, "Enthousiaste",
            "1440p / 4K", "Ultra", "Ultra", "TAA/DLAA", "Ultra", "144+ fps",
            "Ray tracing complet exploitable ; DLSS/FSR en mode qualité pour garder la netteté en haute résolution."),

        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };
}
