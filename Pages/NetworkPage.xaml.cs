using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Pages;

public sealed partial class NetworkPage : Page
{
    private readonly App _app;

    public NetworkPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        DataContext = _app.State;
        GeoStatusLabel.Text = "Lookup à la demande via ip-api.com (aucune requête automatique).";
    }

    private sealed record GeoRow(string Ip, string GeoLabel, string Processes);

    private async void GeoLocate_Click(object sender, RoutedEventArgs e)
    {
        GeoLocateButton.IsEnabled = false;
        GeoStatusLabel.Text = "Géolocalisation en cours…";
        try
        {
            // Snapshot des connexions courantes : IP distante → processus.
            var connections = _app.State.ActiveTcpConnections.ToList();
            var processesByIp = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var connection in connections)
            {
                var ip = GeoIpService.ExtractPublicIp(connection.RemoteEndpoint);
                if (ip is null)
                    continue;

                if (!processesByIp.TryGetValue(ip, out var set))
                    processesByIp[ip] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(connection.ProcessName);
            }

            if (processesByIp.Count == 0)
            {
                GeoStatusLabel.Text = "Aucune destination publique dans les connexions actuelles.";
                GeoResultsList.ItemsSource = null;
                return;
            }

            var geo = await _app.GeoIp.LookupAsync(processesByIp.Keys);

            GeoResultsList.ItemsSource = processesByIp
                .Select(kvp => new GeoRow(
                    kvp.Key,
                    geo.TryGetValue(kvp.Key, out var info) ? info.Label : "Localisation inconnue",
                    string.Join(", ", kvp.Value.Take(3))))
                .OrderBy(r => r.GeoLabel)
                .ToList();

            GeoStatusLabel.Text = $"{processesByIp.Count} destination(s) publique(s), {geo.Count} localisée(s).";
        }
        catch (Exception ex)
        {
            GeoStatusLabel.Text = "Géolocalisation impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Géolocalisation des connexions en échec", ex);
        }
        finally
        {
            GeoLocateButton.IsEnabled = true;
        }
    }
}
