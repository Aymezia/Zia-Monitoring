# Migration .NET 8 → .NET 10

**Pourquoi :** .NET 8 sort du support le 10 novembre 2026. .NET 10 est le LTS
suivant (support jusqu'en novembre 2028).

**État actuel :** les packages `System.*` et `Microsoft.Extensions.*` sont déjà
alignés sur la ligne de servicing 10.0.x (compatible net8). Il ne reste que la
bascule du TFM, qui nécessite le SDK .NET 10 — non installé sur cette machine
au moment de la préparation (l'installation nécessite une action manuelle).

## Étapes

1. **Installer le SDK .NET 10** : <https://dotnet.microsoft.com/download/dotnet/10.0>
   (ou `winget install Microsoft.DotNet.SDK.10`), puis vérifier :

   ```powershell
   dotnet --list-sdks   # doit lister 10.0.x
   ```

2. **Changer les TFM** :
   - `ZiaMonitoring.App.csproj` :
     `net8.0-windows10.0.26100.0` → `net10.0-windows10.0.26100.0`
   - `tests/ZiaMonitoring.Tests/ZiaMonitoring.Tests.csproj` :
     `net8.0-windows` → `net10.0-windows`
   - `installer/bootstrapper/ZiaMonitoring.SetupBootstrapper.csproj` :
     `net8.0-windows` → `net10.0-windows`

3. **CI** : dans `.github/workflows/ci.yml`, passer `dotnet-version: 8.0.x`
   à `10.0.x`.

4. **Vérifier Microsoft.WindowsAppSDK** : la version 2.2.0 doit supporter
   net10 (vérifier les release notes ; sinon monter vers la dernière 2.x).

5. **Valider** :

   ```powershell
   dotnet build ZiaMonitoring.sln -c Release -p:Platform=x64
   dotnet test tests/ZiaMonitoring.Tests/ZiaMonitoring.Tests.csproj
   powershell -File installer/Build-Portable.ps1   # test du publish complet
   ```

   Lancer l'app publiée et vérifier Dashboard (WMI), Santé (LiveCharts),
   Sécurité (WMI/SMART) — les trois zones sensibles aux montées de version.

## Points d'attention

- Les packages 10.0.x ciblent net8 **et** net10 : la bascule du TFM ne
  nécessite aucun changement de version de package.
- `LibreHardwareMonitorLib` 0.9.4 et `LiveChartsCore` 2.0.5 sont des packages
  netstandard/net6+ : compatibles tels quels.
- Si Visual Studio est utilisé, une version supportant .NET 10 est requise.
