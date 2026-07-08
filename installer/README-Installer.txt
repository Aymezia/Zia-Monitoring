Zia Monitoring - Distribution

L'application demarre sans droits administrateur (voir app.manifest).
Les actions qui en ont reellement besoin (debloat telemetrie/taches,
kill switch, changement de DNS, DISM/SFC, capteurs materiel) proposent
une relance elevee au moment ou elles sont utilisees.

1) Build portable (dossier autonome, aucune installation requise)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-Portable.ps1

   Genere dans : .\publish\portable\ZiaMonitoring.App.exe
   Autonome (runtime .NET + Windows App SDK inclus) : copiez le dossier
   entier ou zippez-le, aucun prerequis a installer sur la machine cible.

2) Installeur (.exe unique, necessite Inno Setup 6 : https://jrsoftware.org/isdl.php)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-InnoSetup.ps1

   Genere dans : .\publish\setup\ZiaMonitoring-Setup.exe
   Installation par utilisateur par defaut (pas d'invite UAC), entree
   propre dans "Applications installees", desinstallation automatique.
