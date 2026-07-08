using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Discord Rich Presence via l'IPC local de Discord (named pipe
/// discord-ipc-0..9) — aucune dépendance externe, protocole documenté :
/// trames [opcode int32 LE][longueur int32 LE][JSON UTF-8]. Nécessite un
/// Application ID Discord (gratuit sur discord.com/developers) car Discord
/// n'affiche une présence qu'au nom d'une application déclarée.
/// Tout échec est silencieux : Discord absent/fermé ne doit rien casser.
/// </summary>
public sealed class DiscordRichPresenceService : IDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private string? _connectedAppId;
    private DateTime _lastUpdate = DateTime.MinValue;
    private string? _lastPayloadSignature;

    public void UpdatePresence(string applicationId, string details, string state, DateTime? startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return;

        lock (_gate)
        {
            var signature = $"{details}|{state}";
            if (DateTime.UtcNow - _lastUpdate < UpdateInterval && signature == _lastPayloadSignature)
                return;

            try
            {
                EnsureConnected(applicationId.Trim());
                if (_pipe is null)
                    return;

                var activity = new Dictionary<string, object?>
                {
                    ["details"] = details,
                    ["state"] = state
                };
                if (startedAtUtc is { } start)
                    activity["timestamps"] = new Dictionary<string, object> { ["start"] = new DateTimeOffset(start).ToUnixTimeSeconds() };

                var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["nonce"] = Guid.NewGuid().ToString(),
                    ["args"] = new Dictionary<string, object?>
                    {
                        ["pid"] = Environment.ProcessId,
                        ["activity"] = activity
                    }
                });

                SendFrame(1, payload);
                _lastUpdate = DateTime.UtcNow;
                _lastPayloadSignature = signature;
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Mise à jour Discord Rich Presence impossible (Discord fermé ?)", ex);
                DisposePipe();
            }
        }
    }

    public void ClearPresence()
    {
        lock (_gate)
        {
            if (_pipe is null)
                return;

            try
            {
                var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["nonce"] = Guid.NewGuid().ToString(),
                    ["args"] = new Dictionary<string, object?> { ["pid"] = Environment.ProcessId, ["activity"] = null }
                });
                SendFrame(1, payload);
                _lastPayloadSignature = null;
            }
            catch
            {
                DisposePipe();
            }
        }
    }

    private void EnsureConnected(string applicationId)
    {
        if (_pipe is { IsConnected: true } && _connectedAppId == applicationId)
            return;

        DisposePipe();

        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(200);
                _pipe = pipe;
                break;
            }
            catch
            {
                pipe.Dispose();
            }
        }

        if (_pipe is null)
            return;

        // Handshake (opcode 0) puis lecture de la réponse pour rester synchronisé sur le flux.
        SendFrame(0, JsonSerializer.Serialize(new Dictionary<string, object> { ["v"] = 1, ["client_id"] = applicationId }));
        ReadFrame();
        _connectedAppId = applicationId;
    }

    private void SendFrame(int opcode, string json)
    {
        if (_pipe is null)
            return;

        var frame = BuildFrame(opcode, json);
        _pipe.Write(frame, 0, frame.Length);
        _pipe.Flush();
    }

    /// <summary>Encodage de trame pur et testable : opcode + longueur en little-endian, puis JSON UTF-8.</summary>
    internal static byte[] BuildFrame(int opcode, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BitConverter.GetBytes(opcode).CopyTo(frame, 0);
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
        payload.CopyTo(frame, 8);
        return frame;
    }

    private void ReadFrame()
    {
        if (_pipe is null)
            return;

        var header = new byte[8];
        var read = _pipe.Read(header, 0, 8);
        if (read < 8)
            return;

        var length = BitConverter.ToInt32(header, 4);
        if (length is <= 0 or > 1_000_000)
            return;

        var buffer = new byte[length];
        var total = 0;
        while (total < length)
        {
            var chunk = _pipe.Read(buffer, total, length - total);
            if (chunk <= 0)
                break;
            total += chunk;
        }
    }

    private void DisposePipe()
    {
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _connectedAppId = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ClearPresence();
            DisposePipe();
        }
    }
}
