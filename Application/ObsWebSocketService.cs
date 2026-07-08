using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Client du protocole officiel obs-websocket v5 (intégré à OBS Studio 28+) :
/// connexion, authentification (SHA256 challenge/salt tel que documenté par
/// le protocole), puis bascule de scène via la requête SetCurrentProgramScene.
/// Implémentation séquentielle volontairement simple (pas de file d'attente
/// de requêtes concurrentes) : les bascules de scène sont rares (début/fin
/// de partie), pas un flux continu.
/// </summary>
public sealed class ObsWebSocketService : IDisposable
{
    private ClientWebSocket? _socket;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task<(bool Success, string Message)> ConnectAsync(string host, int port, string? password, CancellationToken ct = default)
    {
        try
        {
            Disconnect();

            var socket = new ClientWebSocket();
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectTimeout.Token);
            await socket.ConnectAsync(new Uri($"ws://{host}:{port}"), linked.Token).ConfigureAwait(false);

            var hello = await ReceiveJsonAsync(socket, ct).ConfigureAwait(false);
            if (hello is null || !hello.Value.TryGetProperty("op", out var helloOp) || helloOp.GetInt32() != 0)
                return (false, "Réponse OBS inattendue (Hello manquant).");

            string? authResponse = null;
            if (hello.Value.GetProperty("d").TryGetProperty("authentication", out var authEl))
            {
                if (string.IsNullOrEmpty(password))
                    return (false, "OBS requiert un mot de passe (authentification activée côté serveur WebSocket).");

                var challenge = authEl.GetProperty("challenge").GetString() ?? "";
                var salt = authEl.GetProperty("salt").GetString() ?? "";
                authResponse = ComputeAuthResponse(password, salt, challenge);
            }

            await SendJsonAsync(socket, new
            {
                op = 1,
                d = new { rpcVersion = 1, authentication = authResponse, eventSubscriptions = 0 }
            }, ct).ConfigureAwait(false);

            var identified = await ReceiveJsonAsync(socket, ct).ConfigureAwait(false);
            if (identified is null || !identified.Value.TryGetProperty("op", out var idOp) || idOp.GetInt32() != 2)
                return (false, "Identification refusée (mot de passe incorrect ?).");

            _socket = socket;
            Infrastructure.AppLog.Info("OBS WebSocket connecté");
            return (true, "Connecté à OBS.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Connexion OBS WebSocket impossible", ex);
            return (false, $"Connexion impossible (OBS lancé ? serveur WebSocket activé ?) : {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SetSceneAsync(string sceneName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return (false, "Nom de scène vide.");

        if (_socket is not { State: WebSocketState.Open } socket)
            return (false, "Non connecté à OBS.");

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            await SendJsonAsync(socket, new
            {
                op = 6,
                d = new { requestType = "SetCurrentProgramScene", requestId, requestData = new { sceneName } }
            }, ct).ConfigureAwait(false);

            var response = await ReceiveJsonAsync(socket, ct).ConfigureAwait(false);
            if (response is null || !response.Value.TryGetProperty("op", out var op) || op.GetInt32() != 7)
                return (false, "Réponse OBS inattendue.");

            var status = response.Value.GetProperty("d").GetProperty("requestStatus");
            var success = status.GetProperty("result").GetBoolean();
            return success
                ? (true, $"Scène '{sceneName}' activée.")
                : (false, $"OBS a refusé la bascule (code {status.GetProperty("code").GetInt32()} — nom de scène correct ?).");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Bascule de scène OBS '{sceneName}' en échec", ex);
            Disconnect(); // connexion probablement rompue
            return (false, ex.Message);
        }
    }

    public void Disconnect()
    {
        try
        {
            _socket?.Abort();
            _socket?.Dispose();
        }
        catch { }
        finally
        {
            _socket = null;
        }
    }

    /// <summary>
    /// Algorithme d'authentification documenté par obs-websocket v5 :
    /// secret = Base64(SHA256(mot_de_passe + salt)) ; réponse = Base64(SHA256(secret + challenge)).
    /// </summary>
    internal static string ComputeAuthResponse(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
    }

    private static async Task<JsonElement?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        stream.Position = 0;
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    public void Dispose() => Disconnect();
}
