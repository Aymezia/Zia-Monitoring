using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Pages;

public sealed partial class NetworkPage : Page
{
    private readonly App _app;
    private readonly GameServerLatencyCollector _packetLossCollector = new();

    public NetworkPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        DataContext = _app.State;
        GeoStatusLabel.Text = "Lookup à la demande via ip-api.com (aucune requête automatique).";
        RegionStatusLabel.Text = "Cliquez sur 'Mesurer les régions' pour lancer le test (8 mesures en parallèle).";
        PacketLossStatusLabel.Text = "Mesure à la demande (10 pings par cible, ~1 s).";
        TracerouteStatusLabel.Text = "Sélectionnez une cible et cliquez sur 'Tracer'.";

        foreach (var option in DnsService.Providers)
            DnsProviderCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Provider });
        DnsProviderCombo.SelectedIndex = 0;

        var vpn = ZiaMonitoring_App.Application.DnsService.DetectVpn();
        VpnStatusLabel.Text = vpn.Label;
        DnsStatusLabel.Text = "Sélectionnez un fournisseur pour basculer le DNS de l'interface active.";
    }

    private bool _dnsLoading = true;

    private async void DnsProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_dnsLoading)
        {
            _dnsLoading = false;
            return;
        }

        if (DnsProviderCombo.SelectedItem is not ComboBoxItem { Tag: DnsProvider provider })
            return;

        DnsProviderCombo.IsEnabled = false;
        DnsStatusLabel.Text = "Application en cours…";
        try
        {
            var (success, message) = await Task.Run(() => _app.Dns.SetProvider(provider));
            DnsStatusLabel.Text = message;
            if (!success)
                Infrastructure.AppLog.Warn($"Bascule DNS vers '{provider}' en échec: {message}");
        }
        finally
        {
            DnsProviderCombo.IsEnabled = true;
        }
    }

    private async void Doh_Toggled(object sender, RoutedEventArgs e)
    {
        DohToggle.IsEnabled = false;
        try
        {
            var (success, message) = await Task.Run(() => _app.Dns.SetDnsOverHttps(DohToggle.IsOn));
            DnsStatusLabel.Text = message;
            if (!success)
                DohToggle.IsOn = !DohToggle.IsOn;
        }
        finally
        {
            DohToggle.IsEnabled = true;
        }
    }

    private async void MeasureDns_Click(object sender, RoutedEventArgs e)
    {
        MeasureDnsButton.IsEnabled = false;
        DnsStatusLabel.Text = "Mesure en cours…";
        try
        {
            var results = await Task.Run(ZiaMonitoring_App.Application.DnsService.MeasureLatencies);
            DnsLatencyList.ItemsSource = results;

            var best = results.FirstOrDefault(r => r.IsBest);
            DnsStatusLabel.Text = best is not null
                ? $"Le plus rapide : {best.Label} ({best.RttLabel})."
                : "Aucun fournisseur n'a répondu (hors ligne ?).";
        }
        catch (Exception ex)
        {
            DnsStatusLabel.Text = "Mesure impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Comparaison de latence DNS en échec", ex);
        }
        finally
        {
            MeasureDnsButton.IsEnabled = true;
        }
    }

    private static readonly Dictionary<string, string> TracerouteHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Riot"] = "riotgames.com",
        ["Valve"] = "steamcommunity.com",
        ["EA"] = "ea.com",
        ["Epic"] = "epicgames.com"
    };

    private async void Traceroute_Click(object sender, RoutedEventArgs e)
    {
        var provider = (TracerouteTargetCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Riot";
        if (!TracerouteHosts.TryGetValue(provider, out var host))
            return;

        TracerouteButton.IsEnabled = false;
        TracerouteStatusLabel.Text = $"Traceroute vers {host} en cours (jusqu'à 30 s)…";
        TracerouteResultsList.ItemsSource = null;
        try
        {
            var hops = await Task.Run(() => TracerouteService.Trace(host));
            TracerouteResultsList.ItemsSource = hops;

            var destination = hops.FirstOrDefault(h => h.IsDestination);
            TracerouteStatusLabel.Text = destination is not null
                ? $"Destination atteinte en {hops.Count} saut(s)."
                : $"Destination non confirmée après {hops.Count} saut(s) (ICMP probablement filtré en fin de chemin).";
        }
        catch (Exception ex)
        {
            TracerouteStatusLabel.Text = "Traceroute impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Traceroute en échec", ex);
        }
        finally
        {
            TracerouteButton.IsEnabled = true;
        }
    }

    private async void MeasurePacketLoss_Click(object sender, RoutedEventArgs e)
    {
        MeasurePacketLossButton.IsEnabled = false;
        PacketLossStatusLabel.Text = "Mesure en cours…";
        try
        {
            var results = await Task.Run(() => _packetLossCollector.MeasureAllPacketLoss());
            PacketLossResultsList.ItemsSource = results;

            var worst = results.Where(r => r.HasData).OrderByDescending(r => r.LossPercent).FirstOrDefault();
            PacketLossStatusLabel.Text = worst is { LossPercent: > 0 }
                ? $"Perte la plus élevée : {worst.Provider} ({worst.LossLabel})."
                : "Aucune perte détectée sur les cibles mesurées.";
        }
        catch (Exception ex)
        {
            PacketLossStatusLabel.Text = "Mesure impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Mesure de perte de paquets en échec", ex);
        }
        finally
        {
            MeasurePacketLossButton.IsEnabled = true;
        }
    }

    private async void MeasureRegions_Click(object sender, RoutedEventArgs e)
    {
        MeasureRegionsButton.IsEnabled = false;
        RegionStatusLabel.Text = "Mesure en cours…";
        try
        {
            var results = await Task.Run(() => _app.RegionalLatency.MeasureAll());
            RegionResultsList.ItemsSource = results;

            var best = results.FirstOrDefault(r => r.IsBest);
            RegionStatusLabel.Text = best is not null
                ? $"Région la plus proche : {best.Region} ({best.PingLabel})."
                : "Aucune région n'a répondu (hors ligne ?).";
        }
        catch (Exception ex)
        {
            RegionStatusLabel.Text = "Mesure impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Mesure de latence régionale en échec", ex);
        }
        finally
        {
            MeasureRegionsButton.IsEnabled = true;
        }
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
