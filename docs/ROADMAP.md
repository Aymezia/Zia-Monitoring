# Roadmap — fonctionnalités différées

Quatre idées de la liste d'améliorations demandent chacune un chantier
dédié ; elles sont documentées ici plutôt que livrées à moitié.

## 1. Vrais FPS via ETW / PresentMon

**Aujourd'hui :** `EstimatedFpsCollector` additionne les compteurs
« GPU Engine » (PerformanceCounter), ce qui donne une *estimation* d'activité,
pas des FPS réels.

**Cible :** consommer les événements ETW `Microsoft-Windows-DxgKrnl` /
`Present` comme le font PresentMon, MSI Afterburner et l'overlay Xbox.

**Approche recommandée :**
- Intégrer [PresentMon](https://github.com/GameTechDev/PresentMon) en
  sous-processus (`PresentMon --output_stdout --process_id <pid>`) plutôt que
  réimplémenter une session ETW (le service `PresentMonService` requiert
  des privilèges et l'API ETW est piégeuse).
- Le PID cible est déjà fourni par `ActiveGameDetector`.
- Fallback sur l'estimation actuelle si PresentMon absent/refusé.

**Attention :** nécessite des droits élevés pour certains jeux (anticheat) ;
prévoir une dégradation propre.

## 2. Localisation FR/EN (.resw)

**Aujourd'hui :** ~300 chaînes en dur dans les XAML et le code, en français
sans accents (« Pret », « Aucun jeu detecte ») pour esquiver les problèmes
d'encodage — la localisation réglerait les deux problèmes.

**Approche recommandée :**
- `Strings/fr-FR/Resources.resw` + `Strings/en-US/Resources.resw` ;
- XAML : `x:Uid` sur chaque contrôle (gros travail mécanique, ~17 pages) ;
- code : `ResourceLoader` (Windows App SDK) via un helper `Loc.Get("Key")` ;
- commencer par les pages les plus visibles (Dashboard, Paramètres, Boost).

**Effort :** 1 à 2 jours de travail mécanique ; à faire *avant* d'ajouter
beaucoup de nouvelles chaînes.

## 3. Benchmark avant/après boost

**Aujourd'hui :** `BoostHistoryService` enregistre déjà le score de santé
avant/après chaque boost (visible dans Paramètres → Historique). C'est un
score synthétique (CPU/RAM/alertes), pas une mesure de performance.

**Cible :** un micro-benchmark reproductible exécuté avant et après le boost :
- charge CPU multi-thread chronométrée (~5 s),
- latence disque (écriture/lecture d'un fichier témoin),
- délai de réponse UI (timer dispatcher).

**Intégration :** bouton « Mesurer l'impact » sur la page Boost, résultats
stockés à côté de l'historique existant (mêmes entrées JSON, champs
additionnels optionnels pour rester rétrocompatible).

## 4. Overlay personnalisable

**Aujourd'hui :** `PerformanceOverlayWindow` a une taille/position fixes et
un jeu de métriques figé ; seule l'opacité est réglable.

**Cible :**
- choix des métriques affichées (CPU, GPU, temps, FPS, ping, RAM) ;
- position (4 coins + glisser-déposer) et taille (S/M/L) ;
- persistance dans `AppSettings` (le pipeline settings est prêt :
  ajouter des champs avec valeurs par défaut suffit, la rétrocompatibilité
  JSON est testée).

**Prérequis UI :** transformer le XAML de l'overlay en `ItemsRepeater`
piloté par une liste de métriques sélectionnées plutôt qu'une grille fixe.
