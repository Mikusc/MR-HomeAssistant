using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MRHomeAssistant.Runtime.Application.Settings;
using MRHomeAssistant.Runtime.Infrastructure.Persistence;
using UnityEngine;

namespace MRHomeAssistant.Runtime.Application.HomeAssistant
{
    [DisallowMultipleComponent]
    public sealed class HomeAssistantConnection : MonoBehaviour
    {
        [SerializeField] private bool autoEvaluateConfigurationOnStart = true;
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private int connectTimeoutSeconds = 10;
        [SerializeField] private int reconnectDelaySeconds = 2;
        [SerializeField] private int heartbeatSeconds = 15;

        public event Action<IReadOnlyList<HomeAssistantStateSummary>> StatesChanged;
        public event Action<HomeAssistantStateSummary> StateUpdated;
        public event Action ConnectionStateChanged;
        public bool HasValidConfiguration { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public bool IsDemo { get; private set; }
        public bool IsBusy => ConnectionPhase == HomeAssistantConnectionPhase.Connecting || ConnectionPhase == HomeAssistantConnectionPhase.Reconnecting;
        public bool IsStale => !IsConnected && !IsDemo && states.Count > 0;
        public int ReconnectAttempt { get; private set; }
        public DateTime? NextReconnectAt { get; private set; }
        public DateTime? LastContactUtc { get; private set; }
        public string MetadataStatus { get; private set; } = "";
        public string LastActionStatus { get; private set; } = "";
        public long LastActionMilliseconds { get; private set; }
        public HomeAssistantCatalog Catalog { get; } = new();
        public string CurrentUrl { get; private set; } = "";
        public string CurrentWebSocketUrl { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public DateTime? LastStateUpdate { get; private set; }
        public HomeAssistantConnectionPhase ConnectionPhase { get; private set; }
        public IReadOnlyList<HomeAssistantStateSummary> States => states;

        private readonly List<HomeAssistantStateSummary> states = new();
        private readonly Dictionary<int, TaskCompletionSource<string>> requests = new();
        private readonly HashSet<string> pendingEntities = new();
        private readonly List<string> initialEvents = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private ClientWebSocket socket;
        private CancellationTokenSource session;
        private string token = "";
        private int nextId;
        private int subscriptionId;
        private bool initializing;
        private CancellationTokenSource recovery;
        private bool wantsConnection;
        private bool suspended;
        private int connectionGeneration;

        private async void Start()
        {
            if (IsDemo || IsBusy || IsConnected) return;
            if (autoEvaluateConfigurationOnStart) EvaluateConfiguration();
            if (autoConnectOnStart && HasValidConfiguration) await ConnectAndInitializeAsync();
        }

        private void OnDestroy() => Disconnect();

        public void EvaluateConfiguration()
        {
            if (IsBusy || IsConnected || IsDemo || HasValidConfiguration) return;
            if (new AppSettingsRepository().TryLoad(out AppSettingsData settings))
            {
                CurrentUrl = settings.homeAssistantUrl ?? "";
                // Existing local installations may have a legacy token. Never write it back.
                if (string.IsNullOrWhiteSpace(token)) token = settings.homeAssistantToken ?? "";
            }
            HasValidConfiguration = TryBuildWebSocketUrl(CurrentUrl, out string url) && !string.IsNullOrWhiteSpace(token);
            CurrentWebSocketUrl = url;
            ConnectionStateChanged?.Invoke();
        }

        public bool Configure(string url, string accessToken, out string error)
        {
            error = "";
            if (IsBusy) { error = "Cancel the current connection first."; return false; }
            if (!TryBuildWebSocketUrl(url, out string websocketUrl))
            {
                error = "Enter a valid http:// or https:// server address.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                error = "Enter a Home Assistant access token.";
                return false;
            }
            Disconnect();
            CurrentUrl = url.Trim();
            CurrentWebSocketUrl = websocketUrl;
            token = accessToken.Trim();
            HasValidConfiguration = true;
            LastError = "";
            ConnectionStateChanged?.Invoke();
            return true;
        }

        public static bool TryBuildWebSocketUrl(string value, out string websocketUrl)
        {
            websocketUrl = "";
            if (string.IsNullOrWhiteSpace(value)) return false;
            string candidate = value.Trim();
            if (!candidate.Contains("://")) candidate = "http://" + candidate;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) || string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            if (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ws" && uri.Scheme != "wss") return false;
            var builder = new UriBuilder(uri) { Scheme = uri.Scheme == "https" || uri.Scheme == "wss" ? "wss" : "ws" };
            string path = uri.AbsolutePath.TrimEnd('/');
            builder.Path = path.EndsWith("/api/websocket", StringComparison.Ordinal) ? path : path + "/api/websocket";
            websocketUrl = builder.Uri.AbsoluteUri;
            return true;
        }

        [ContextMenu("Connect Home Assistant")]
        public async void ConnectFromContextMenu()
        {
            EvaluateConfiguration();
            await ConnectAndInitializeAsync();
        }

        public async Task ConnectAndInitializeAsync()
        {
            if (IsBusy || !HasValidConfiguration) return;
            Disconnect();
            int generation = connectionGeneration;
            wantsConnection = true;
            await AttemptConnectionAsync();
            if (generation == connectionGeneration && wantsConnection && !IsConnected) ScheduleRecovery();
        }

        private async Task AttemptConnectionAsync()
        {
            CloseTransport(true);
            var lifetime = new CancellationTokenSource();
            session = lifetime;
            var client = new ClientWebSocket();
            socket = client;
            LastError = "";
            ConnectionPhase = ReconnectAttempt > 0 ? HomeAssistantConnectionPhase.Reconnecting : HomeAssistantConnectionPhase.Connecting;
            ConnectionStateChanged?.Invoke();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, connectTimeoutSeconds)));
            try
            {
                await client.ConnectAsync(new Uri(CurrentWebSocketUrl), timeout.Token);
                var challenge = JsonUtility.FromJson<HomeAssistantMessageEnvelope>(await ReceiveAsync(client, timeout.Token));
                if (challenge?.type != "auth_required") throw new InvalidOperationException("Unexpected server handshake.");
                await SendAsync(client, JsonUtility.ToJson(new HomeAssistantAuthRequest { access_token = token }), timeout.Token);
                var auth = JsonUtility.FromJson<HomeAssistantMessageEnvelope>(await ReceiveAsync(client, timeout.Token));
                if (session != lifetime) return;
                if (auth?.type != "auth_ok") throw new UnauthorizedAccessException();
                IsAuthenticated = true;
                LastContactUtc = DateTime.UtcNow;
                initializing = true;
                _ = ReceiveLoopAsync(client, lifetime);

                // Subscribe first and replay events after the snapshot to retain changes during loading.
                subscriptionId = ++nextId;
                await RequestAsync(client, new HomeAssistantSubscribeEventsRequest { id = subscriptionId }, subscriptionId, timeout.Token);
                int id = ++nextId;
                string snapshot = await RequestAsync(client, new HomeAssistantGetStatesRequest { id = id }, id, timeout.Token);
                if (session != lifetime) return;
                var response = JsonUtility.FromJson<HomeAssistantGetStatesResponse>(snapshot);
                states.Clear();
                if (response.result != null) states.AddRange(response.result);
                initializing = false;
                foreach (string json in initialEvents) ApplyEvent(json);
                initialEvents.Clear();
                if (session != lifetime) return;
                IsConnected = true;
                ConnectionPhase = HomeAssistantConnectionPhase.Connected;
                NextReconnectAt = null;
                NotifyStates();
                ConnectionStateChanged?.Invoke();
                _ = LoadCatalogAsync(client, lifetime);
                _ = HeartbeatAsync(client, lifetime);
            }
            catch (Exception ex)
            {
                if (session != lifetime) return;
                string message = ex is UnauthorizedAccessException ? "Access token rejected. Check the token and try again." :
                    ex is OperationCanceledException ? "Connection timed out. Check the address and network, then retry." :
                    "Could not connect. Check the server address, network and certificate.";
                CloseTransport(true);
                if (ex is UnauthorizedAccessException) { wantsConnection = false; HasValidConfiguration = false; }
                LastError = message;
                ConnectionStateChanged?.Invoke();
            }
        }

        public void Disconnect()
        {
            connectionGeneration++;
            wantsConnection = false;
            CancelRecovery();
            ReconnectAttempt = 0;
            NextReconnectAt = null;
            LastError = "";
            LastActionStatus = "";
            CloseTransport(false);
            NotifyConnection();
        }

        public void ForgetCredentials()
        {
            Disconnect();
            token = "";
            HasValidConfiguration = false;
            ConnectionStateChanged?.Invoke();
        }

        private void CloseTransport(bool retainSnapshot)
        {
            var old = session;
            session = null;
            old?.Cancel();
            socket?.Dispose();
            socket = null;
            foreach (var request in requests.Values) request.TrySetCanceled();
            requests.Clear();
            pendingEntities.Clear();
            initialEvents.Clear();
            initializing = false;
            if (!retainSnapshot) { states.Clear(); Catalog.Clear(); MetadataStatus = ""; LastStateUpdate = null; }
            IsDemo = false;
            IsConnected = false;
            IsAuthenticated = false;
            LastContactUtc = null;
            ConnectionPhase = HomeAssistantConnectionPhase.Disconnected;
            old?.Dispose();
        }

        private void NotifyConnection() { StatesChanged?.Invoke(states); ConnectionStateChanged?.Invoke(); }

        private void CancelRecovery()
        {
            var previous = recovery;
            recovery = null;
            previous?.Cancel();
            previous?.Dispose();
        }

        private void ScheduleRecovery(bool immediate = false)
        {
            if (!wantsConnection || suspended || recovery != null || IsConnected || !HasValidConfiguration) return;
            var lifetime = new CancellationTokenSource();
            recovery = lifetime;
            _ = RecoverAsync(lifetime, immediate);
        }

        private async Task RecoverAsync(CancellationTokenSource lifetime, bool immediate)
        {
            CancellationToken ct = lifetime.Token;
            try
            {
                for (int attempt = 1; attempt <= 5 && wantsConnection && !suspended; attempt++)
                {
                    ReconnectAttempt = attempt;
                    int delay = immediate && attempt == 1 ? 0 : Math.Min(30, Math.Max(1, reconnectDelaySeconds) * (1 << (attempt - 1)));
                    NextReconnectAt = DateTime.UtcNow.AddSeconds(delay);
                    ConnectionPhase = HomeAssistantConnectionPhase.Reconnecting;
                    NotifyConnection();
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    if (ct.IsCancellationRequested || !wantsConnection || suspended) return;
                    await AttemptConnectionAsync();
                    if (ct.IsCancellationRequested || IsConnected || !wantsConnection) return;
                }
                if (!ct.IsCancellationRequested && wantsConnection)
                {
                    wantsConnection = false;
                    ConnectionPhase = HomeAssistantConnectionPhase.Disconnected;
                    NextReconnectAt = null;
                    LastError = "Could not restore the connection. Check the network and reconnect.";
                    NotifyConnection();
                }
            }
            catch (OperationCanceledException) { }
            finally { if (recovery == lifetime) { recovery = null; lifetime.Dispose(); } }
        }

        private void ConnectionLost(CancellationTokenSource lifetime)
        {
            if (session != lifetime) return;
            CloseTransport(true);
            LastError = "Connection lost. Cached states are read-only until reconnected.";
            NotifyConnection();
            ScheduleRecovery();
        }

        private void OnApplicationPause(bool paused) => SetApplicationPaused(paused);

        public void SetApplicationPaused(bool paused)
        {
            suspended = paused;
            if (paused && wantsConnection)
            {
                CancelRecovery();
                CloseTransport(true);
                NextReconnectAt = null;
                LastError = "Connection paused. States will refresh on resume.";
                NotifyConnection();
            }
            else if (!paused && wantsConnection) ScheduleRecovery(true);
        }

        private async Task HeartbeatAsync(ClientWebSocket client, CancellationTokenSource lifetime)
        {
            CancellationToken ct = lifetime.Token;
            try
            {
                while (session == lifetime && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, heartbeatSeconds)), ct);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    int id = ++nextId;
                    await RequestAsync(client, new HomeAssistantCommand { id = id, type = "ping" }, id, timeout.Token);
                }
            }
            catch (Exception) { ConnectionLost(lifetime); }
        }

        private async Task LoadCatalogAsync(ClientWebSocket client, CancellationTokenSource lifetime)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            MetadataStatus = "Loading rooms...";
            try
            {
                var areas = await RegistryAsync<HomeAssistantArea>(client, "config/area_registry/list", timeout.Token);
                var devices = await RegistryAsync<HomeAssistantDevice>(client, "config/device_registry/list", timeout.Token);
                var entities = await RegistryAsync<HomeAssistantEntity>(client, "config/entity_registry/list", timeout.Token);
                if (session != lifetime) return;
                Catalog.Load(areas, devices, entities);
                MetadataStatus = "";
            }
            catch (Exception)
            {
                if (session != lifetime) return;
                Catalog.Clear();
                MetadataStatus = "Room metadata unavailable. Showing individual entities.";
            }
            StatesChanged?.Invoke(states);
        }

        private async Task<T[]> RegistryAsync<T>(ClientWebSocket client, string type, CancellationToken ct)
        {
            int id = ++nextId;
            string json = await RequestAsync(client, new HomeAssistantCommand { id = id, type = type }, id, ct);
            return JsonUtility.FromJson<HomeAssistantRegistryResponse<T>>(json)?.result ?? Array.Empty<T>();
        }

        public void StartDemo()
        {
            Disconnect();
            IsDemo = true;
            LastError = "";
            states.AddRange(HomeAssistantDevices.DemoStates());
            Catalog.LoadDemo();
            NotifyStates();
            ConnectionStateChanged?.Invoke();
        }

        public bool IsPending(string entityId) => pendingEntities.Contains(entityId);

        public bool TryGetCachedState(string entityId, out HomeAssistantStateSummary state)
        {
            state = states.Find(item => item.entity_id == entityId);
            return state != null;
        }

        public Task<string> SetPowerAsync(string entityId, bool on)
        {
            return ControlAsync(entityId, id => new HomeAssistantServiceRequest
            {
                id = id, domain = entityId.Split('.')[0], service = on ? "turn_on" : "turn_off",
                target = new HomeAssistantTarget { entity_id = entityId }
            }, state => state.state == (on ? "on" : "off"), state => state.state = on ? "on" : "off");
        }

        public Task<string> SetLightAsync(string entityId, int? brightness, int? temperature)
        {
            if (!TryGetCachedState(entityId, out var state) || (!brightness.HasValue && !temperature.HasValue) ||
                (brightness.HasValue && (!HomeAssistantDevices.SupportsBrightness(state) || brightness < 1 || brightness > 255)) ||
                (temperature.HasValue && (!HomeAssistantDevices.SupportsTemperature(state) || temperature < state.attributes.min_color_temp_kelvin || temperature > state.attributes.max_color_temp_kelvin)))
                return Task.FromResult("This light does not support those settings.");
            var target = new HomeAssistantTarget { entity_id = entityId };
            return ControlAsync(entityId, id => brightness.HasValue && temperature.HasValue
                ? (object)new HomeAssistantLightRequest<LightData> { id = id, target = target, service_data = new LightData { brightness = brightness.Value, color_temp_kelvin = temperature.Value } }
                : brightness.HasValue ? new HomeAssistantLightRequest<BrightnessData> { id = id, target = target, service_data = new BrightnessData { brightness = brightness.Value } }
                : new HomeAssistantLightRequest<TemperatureData> { id = id, target = target, service_data = new TemperatureData { color_temp_kelvin = temperature.Value } },
                updated => updated.state == "on" && (!brightness.HasValue || Math.Abs(updated.attributes.brightness - brightness.Value) <= 3) &&
                    (!temperature.HasValue || (updated.attributes.color_mode == "color_temp" && Math.Abs(updated.attributes.color_temp_kelvin - temperature.Value) <= 100)),
                updated => { updated.state = "on"; if (brightness.HasValue) updated.attributes.brightness = brightness.Value;
                    if (temperature.HasValue) { updated.attributes.color_temp_kelvin = temperature.Value; updated.attributes.color_mode = "color_temp"; } });
        }

        private async Task<string> ControlAsync(string entityId, Func<int, object> payload, Predicate<HomeAssistantStateSummary> confirmed, Action<HomeAssistantStateSummary> simulate)
        {
            if (!TryGetCachedState(entityId, out var state) || !HomeAssistantDevices.CanControl(state)) return "This device cannot be controlled here.";
            if ((!IsConnected && !IsDemo) || IsPending(entityId)) return "Device is not ready.";
            var currentSession = session;
            bool wasDemo = IsDemo;
            var timer = Stopwatch.StartNew();
            string outcome = "Not confirmed";
            pendingEntities.Add(entityId);
            StatesChanged?.Invoke(states);
            try
            {
                if (wasDemo)
                {
                    await Task.Delay(250);
                    if (!IsDemo || !states.Contains(state)) return "Demo ended.";
                    simulate(state);
                    state.last_updated = DateTime.UtcNow.ToString("O");
                    StateUpdated?.Invoke(state);
                    NotifyStates();
                    outcome = "Demo confirmed";
                    return "";
                }
                int id = ++nextId;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(currentSession.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await RequestAsync(socket, payload(id), id, timeout.Token);
                // A service result is acceptance, not proof that the physical state changed.
                while (!timeout.IsCancellationRequested)
                {
                    if (session != currentSession || !IsConnected) return "Connection changed. Check the device before retrying.";
                    if (TryGetCachedState(entityId, out var updated) && confirmed(updated)) { outcome = "State confirmed"; return ""; }
                    await Task.Delay(100, timeout.Token);
                }
                return "No updated state received. Check the device before retrying.";
            }
            catch (OperationCanceledException) { return "No confirmed update. Check the connection and device before retrying."; }
            catch (Exception) { return "Home Assistant could not complete the action. Check device availability and permissions."; }
            finally
            {
                if ((wasDemo && IsDemo && states.Contains(state)) || (!wasDemo && session == currentSession))
                {
                    pendingEntities.Remove(entityId);
                    LastActionStatus = outcome;
                    LastActionMilliseconds = timer.ElapsedMilliseconds;
                    StatesChanged?.Invoke(states);
                }
            }
        }

        private async Task<string> RequestAsync(ClientWebSocket client, object payload, int id, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add(id, completion);
            using var registration = ct.Register(() => completion.TrySetCanceled());
            try
            {
                await SendAsync(client, JsonUtility.ToJson(payload), ct);
                string json = await completion.Task;
                var result = JsonUtility.FromJson<HomeAssistantMessageEnvelope>(json);
                if (result.type != "pong" && !result.success) throw new InvalidOperationException("Home Assistant rejected the request.");
                return json;
            }
            finally { requests.Remove(id); }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationTokenSource lifetime)
        {
            try
            {
                CancellationToken ct = lifetime.Token;
                while (!ct.IsCancellationRequested)
                {
                    string json = await ReceiveAsync(client, ct);
                    if (session != lifetime) return;
                    LastContactUtc = DateTime.UtcNow;
                    var envelope = JsonUtility.FromJson<HomeAssistantMessageEnvelope>(json);
                    if ((envelope?.type == "result" || envelope?.type == "pong") && requests.TryGetValue(envelope.id, out var completion)) completion.TrySetResult(json);
                    else if (envelope?.type == "event" && envelope.id == subscriptionId)
                    {
                        if (initializing) initialEvents.Add(json);
                        else { ApplyEvent(json); NotifyStates(); }
                    }
                }
            }
            catch (Exception)
            {
                ConnectionLost(lifetime);
            }
        }

        private void ApplyEvent(string json)
        {
            var data = JsonUtility.FromJson<HomeAssistantEventEnvelope>(json)?.@event?.data;
            if (data == null || string.IsNullOrEmpty(data.entity_id)) return;
            int index = states.FindIndex(item => item.entity_id == data.entity_id);
            if (data.new_state == null) { if (index >= 0) states.RemoveAt(index); return; }
            data.new_state.entity_id = data.entity_id;
            if (index >= 0) states[index] = data.new_state;
            else states.Add(data.new_state);
            StateUpdated?.Invoke(data.new_state);
        }

        private async Task SendAsync(ClientWebSocket client, string json, CancellationToken ct)
        {
            await sendLock.WaitAsync(ct);
            try { await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, ct); }
            finally { sendLock.Release(); }
        }

        private static async Task<string> ReceiveAsync(ClientWebSocket client, CancellationToken ct)
        {
            byte[] buffer = new byte[8192];
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException();
                message.Write(buffer, 0, result.Count);
                if (message.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Message too large.");
            } while (!result.EndOfMessage);
            // Decode after reassembly; a UTF-8 character may straddle WebSocket fragments.
            return Encoding.UTF8.GetString(message.ToArray());
        }

        private void NotifyStates()
        {
            LastStateUpdate = DateTime.Now;
            StatesChanged?.Invoke(states);
        }
    }
}
