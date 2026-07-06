<div align="center">

# ⚡ Zia Monitoring

**Logiciel de monitoring PC Windows — temps réel, jeux, sécurité et optimisation**

![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)
![UI](https://img.shields.io/badge/UI-WinUI%203-violet?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![Open Source](https://img.shields.io/badge/Open%20Source-Yes-brightgreen?style=flat-square)

</div>

---

## Aperçu

Zia Monitoring est un logiciel de monitoring système complet pour Windows 10/11, développé en **C# / WinUI 3**. Il surveille en temps réel les performances de votre PC, détecte les jeux installés, propose des optimisations intelligentes et analyse la sécurité de votre machine — sans dépendance tierce obligatoire.

---

## Fonctionnalités

### Dashboard temps réel
- CPU, RAM, température CPU/GPU, usage GPU, VRAM
- Vitesse des ventilateurs (RPM)
- Bande passante réseau (upload/download) et latence ping
- Débit disque I/O (lecture/écriture MB/s)
- Score de santé système et niveau de risque

### Cartographie PC
- Inventaire matériel complet : CPU, GPU, carte mère, BIOS, RAM, disques, uptime
- Inventaire des jeux installés (Steam, Epic, Riot, Battle.net, Windows)
- Temps de jeu Steam lu depuis `localconfig.vdf`
- Lancement direct des jeux depuis l'app

### Sante et historique
- Graphiques LiveCharts2 : historique CPU 24h et tendance 7 jours
- Historique **persisté en SQLite** (un échantillon / 15 s, rétention 8 jours) : les graphes survivent aux redémarrages
- Alertes dynamiques en temps réel

### Recommandations intelligentes
- Suggestions automatiques basées sur charge CPU, RAM, températures, espace disque
- Détection surchauffe CPU/GPU

### Boost et optimisation
- Moteur de boost avec preview sécurisé avant exécution
- Rollback automatique de toutes les actions
- 4 profils prédéfinis : Gaming Max, Travail Silencieux, Streaming, Equilibre
- Profils personnalisés avec **export/import JSON** (partage entre machines)
- Nettoyage cache navigateurs (Chrome, Edge, Firefox) directement depuis l'app
- Désactivation/restauration des animations Windows

### Sécurité et santé disques
- Vérification pare-feu Windows et UAC
- Scan S.M.A.R.T via WMI (défaillance disque prévue)
- Détection des pilotes obsolètes (> 3 ans)
- Scan des entrées de démarrage suspectes

### Gamer & Streamer
- Détection du jeu actif en cours (30+ titres reconnus)
- Mode silencieux automatique à l'ouverture d'un jeu
- Diagnostics spécifiques (Valorant, OBS, encodeurs GPU)

### Notifications et alertes
- Toast Windows natif : CPU, surchauffe CPU/GPU, disque plein — **seuils configurables** dans les Paramètres
- Résumé de santé quotidien
- Notification au démarrage si une nouvelle version est publiée (GitHub Releases)

### Export et rapports
- Export rapport HTML complet (machine, métriques, jeux, alertes)
- Export diagnostic ZIP (JSON + résumé)

### Paramètres
- Intervalle de rafraîchissement configurable (1–10s)
- Activation individuelle des alertes, résumé quotidien, mode silencieux, scheduler
- Historique des optimisations avec score avant/après

---

## Installation

### Option 1 — Portable (recommandé, sans installation)
Téléchargez `ZiaMonitoring.App.exe` depuis les [Releases](../../releases) et lancez directement.

### Option 2 — Installateur MSI
Téléchargez `ZiaMonitoring-Setup.msi` et exécutez-le.  
L'app s'installe dans `%LOCALAPPDATA%\ZiaMonitoring\app` sans droits administrateur.

### Option 3 — Bootstrap Setup.exe
Téléchargez `ZiaMonitoring-SetupBootstrap.exe` **et** `ZiaMonitoring-Setup.msi` dans le même dossier, puis lancez le `.exe`.

---

## Build depuis les sources

**Prérequis :** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8), Windows 10/11

```powershell
git clone https://github.com/Aymezia/Zia-Monitoring.git
cd "Zia-Monitoring/ZiaMonitoring.App"

# Build debug (solution complète : app + bootstrapper + tests)
dotnet build ZiaMonitoring.sln

# Tests unitaires
dotnet test tests/ZiaMonitoring.Tests/ZiaMonitoring.Tests.csproj

# Build portable (release)
powershell -ExecutionPolicy Bypass -File .\installer\Build-Portable.ps1

# Build MSI
powershell -ExecutionPolicy Bypass -File .\installer\Build-Msi.ps1
```

La CI GitHub Actions (`.github/workflows/ci.yml`) build l'app et exécute les
tests sur chaque push/PR vers `main`. La migration vers .NET 10 est préparée :
voir [docs/MIGRATION-NET10.md](docs/MIGRATION-NET10.md). Les fonctionnalités
différées (FPS via PresentMon, localisation, benchmarks, overlay
personnalisable) sont détaillées dans [docs/ROADMAP.md](docs/ROADMAP.md).

L'exécutable portable se trouve dans `publish\portable\ZiaMonitoring.App.exe`.

---

## Structure du projet

```
ZiaMonitoring.App/
├── ZiaMonitoring.sln                  # Solution (app + bootstrapper + tests)
├── App.xaml.cs                        # Bootstrap, conteneur DI, gestion exceptions
├── MainWindow.xaml(.cs)               # Shell navigation + boucle de monitoring en arrière-plan
├── Core/
│   └── Models/MonitoringModels.cs     # Modèles domaine (snapshot, profile, settings...)
├── Infrastructure/
│   ├── AppLog.cs                      # Logger partagé (rotation 2 Mo, déduplication)
│   └── Collectors/                    # Collecteurs WMI/Win32 (CPU, GPU, réseau, disques...)
├── Application/                       # Services métier (boost, sécurité, profils, historique SQLite...)
├── ViewModels/
│   └── AppStateViewModel.cs           # State partagé MVVM (CommunityToolkit.Mvvm)
├── Pages/                             # Pages WinUI 3 (Dashboard, Mapping, Health, Boost...)
├── tests/ZiaMonitoring.Tests/         # Tests unitaires xUnit
├── docs/                              # Guide migration .NET 10, roadmap
└── installer/                         # Scripts PowerShell + WiX pour packaging
```

---

## Technologies

| Composant | Technologie |
|---|---|
| Framework | .NET 8 / C# |
| UI | WinUI 3 (Windows App SDK 2.2) |
| Graphiques | LiveChartsCore 2 (SkiaSharp) |
| Packaging | WiX Toolset v7 |
| Collecte hardware | WMI / Win32 / PerformanceCounters |
| Stockage paramètres | JSON (`%LOCALAPPDATA%\ZiaMonitoring`), clé API chiffrée DPAPI |
| Historique métriques | SQLite (`metrics.db`) |
| Tests / CI | xUnit + GitHub Actions |

---

## Contribuer

Les contributions sont les bienvenues !

1. Forkez le dépôt
2. Créez une branche : `git checkout -b feature/ma-fonctionnalite`
3. Committez : `git commit -m "feat: description"`
4. Poussez : `git push origin feature/ma-fonctionnalite`
5. Ouvrez une Pull Request

---

## Licence

Distribué sous licence **MIT**. Voir [LICENSE](LICENSE) pour plus d'informations.
