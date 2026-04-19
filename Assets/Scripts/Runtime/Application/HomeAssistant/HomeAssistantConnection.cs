using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MRHomeAssistant.Runtime.Application.Settings;
using MRHomeAssistant.Runtime.Infrastructure.Persistence;
using UnityEngine;

namespace MRHomeAssistant.Runtime.Application.HomeAssistant
{
    /// <summary>
    /// Phase 1 Home Assistant connector:
    /// - reads config
    /// - connects to /api/websocket
    /// - authenticates
    /// - loads initial get_states snapshot
    /// - subscribes to state_changed events
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeAssistantConnection : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField]
        private bool logOnStart = true;

        [SerializeField]
        private bool autoEvaluateConfigurationOnStart = true;

        [SerializeField]
        private bool autoConnectOnStart = true;

        [SerializeField]
        private bool logRawMessages = false;

        [Header("Timeouts")]
        [SerializeField]
        private int connectTimeoutSeconds = 10;

        [SerializeField]
        private int logStatePreviewCount = 8;

        public event Action<IReadOnlyList<HomeAssistantStateSummary>> StatesChanged;
        public event Action<HomeAssistantStateSummary> StateUpdated;
        public event Action ConnectionStateChanged;

        public bool HasValidConfiguration { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public string CurrentUrl { get; private set; } = string.Empty;
        public string CurrentToken { get; private set; } = string.Empty;
        public string CurrentWebSocketUrl { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public HomeAssistantConnectionPhase ConnectionPhase { get; private set; } = HomeAssistantConnectionPhase.Disconnected;
        public IReadOnlyList<HomeAssistantStateSummary> States => _states;

        private readonly List<HomeAssistantStateSummary> _states = new();

        private AppSettingsRepository _appSettingsRepository;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _lifetimeCancellation;
        private Task _receiveLoopTask;
        private int _nextMessageId = 1;
        private int _stateChangedSubscriptionId = -1;

        private void Awake()
        {
            _appSettingsRepository = new AppSettingsRepository();
            _lifetimeCancellation = new CancellationTokenSource();
        }

        private async void Start()
        {
            if (autoEvaluateConfigurationOnStart)
            {
                EvaluateConfiguration();
            }

            if (autoConnectOnStart && HasValidConfiguration)
            {
                await ConnectAndInitializeAsync();
            }
        }

        private void OnDestroy()
        {
            try
            {
                _lifetimeCancellation?.Cancel();
                _lifetimeCancellation?.Dispose();
                _lifetimeCancellation = null;
            }
            catch
            {
                // ignored
            }

            DisposeSocketIfAny();
        }

        [ContextMenu("Evaluate Home Assistant Configuration")]
        public void EvaluateConfiguration()
        {
            HasValidConfiguration = false;
            IsConnected = false;
            IsAuthenticated = false;
            LastError = string.Empty;
            CurrentUrl = string.Empty;
            CurrentToken = string.Empty;
            CurrentWebSocketUrl = string.Empty;
            _stateChangedSubscriptionId = -1;

            if (!_appSettingsRepository.TryLoad(out AppSettingsData settings))
            {
                if (logOnStart)
                {
                    Debug.LogWarning(
                        $"[HomeAssistantConnection] app-settings.json could not be loaded | Path={_appSettingsRepository.FilePath}",
                        this);
                }

                SetConnectionPhase(HomeAssistantConnectionPhase.Disconnected);
                NotifyConnectionStateChanged();
                return;
            }

            CurrentUrl = settings.homeAssistantUrl?.Trim() ?? string.Empty;
            CurrentToken = settings.homeAssistantToken?.Trim() ?? string.Empty;

            bool hasUrl = !string.IsNullOrWhiteSpace(CurrentUrl);
            bool hasToken = !string.IsNullOrWhiteSpace(CurrentToken);

            HasValidConfiguration = hasUrl && hasToken;

            if (HasValidConfiguration)
            {
                CurrentWebSocketUrl = BuildWebSocketUrl(CurrentUrl);
            }

            if (logOnStart)
            {
                if (HasValidConfiguration)
                {
                    Debug.Log(
                        $"[HomeAssistantConnection] Configuration ready | Url={CurrentUrl} | WsUrl={CurrentWebSocketUrl} | Token=set ({CurrentToken.Length} chars)",
                        this);
                }
                else
                {
                    Debug.LogWarning(
                        $"[HomeAssistantConnection] Configuration incomplete | Url={(hasUrl ? CurrentUrl : "<empty>")} | Token={(hasToken ? $"set ({CurrentToken.Length} chars)" : "<empty>")}",
                        this);
                }
            }

            SetConnectionPhase(HomeAssistantConnectionPhase.Disconnected);
            NotifyConnectionStateChanged();
        }

        [ContextMenu("Connect Home Assistant")]
        public async void ConnectFromContextMenu()
        {
            if (!HasValidConfiguration)
            {
                EvaluateConfiguration();
            }

            if (HasValidConfiguration)
            {
                await ConnectAndInitializeAsync();
            }
        }

        public async Task ConnectAndInitializeAsync()
        {
            if (!HasValidConfiguration)
            {
                Debug.LogWarning("[HomeAssistantConnection] Connect skipped: configuration is incomplete.", this);
                return;
            }

            LastError = string.Empty;
            IsConnected = false;
            IsAuthenticated = false;
            _stateChangedSubscriptionId = -1;
            _nextMessageId = 1;
            _states.Clear();
            DisposeSocketIfAny();

            SetConnectionPhase(HomeAssistantConnectionPhase.Connecting);
            NotifyConnectionStateChanged();

            _webSocket = new ClientWebSocket();

            using CancellationTokenSource timeoutCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeoutSeconds));
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token, timeoutCancellation.Token);

            CancellationToken cancellationToken = linkedCancellation.Token;

            try
            {
                Debug.Log($"[HomeAssistantConnection] Connecting | {CurrentWebSocketUrl}", this);
                await _webSocket.ConnectAsync(new Uri(CurrentWebSocketUrl), cancellationToken);

                string authRequiredJson = await ReceiveTextMessageAsync(_webSocket, cancellationToken);
                LogRaw("RECV", authRequiredJson);

                HomeAssistantMessageEnvelope authRequired =
                    JsonUtility.FromJson<HomeAssistantMessageEnvelope>(authRequiredJson);

                if (authRequired == null || authRequired.type != "auth_required")
                {
                    throw new InvalidOperationException($"Expected auth_required, got: {authRequiredJson}");
                }

                HomeAssistantAuthRequest authRequest = new HomeAssistantAuthRequest
                {
                    access_token = CurrentToken
                };

                string authRequestJson = JsonUtility.ToJson(authRequest);
                LogRaw("SEND", authRequestJson);
                await SendTextMessageAsync(_webSocket, authRequestJson, cancellationToken);

                string authResponseJson = await ReceiveTextMessageAsync(_webSocket, cancellationToken);
                LogRaw("RECV", authResponseJson);

                HomeAssistantMessageEnvelope authResponse =
                    JsonUtility.FromJson<HomeAssistantMessageEnvelope>(authResponseJson);

                if (authResponse == null)
                {
                    throw new InvalidOperationException("Auth response could not be parsed.");
                }

                if (authResponse.type == "auth_invalid")
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(authResponse.message)
                            ? "Home Assistant rejected the token."
                            : authResponse.message);
                }

                if (authResponse.type != "auth_ok")
                {
                    throw new InvalidOperationException($"Unexpected auth response type: {authResponse.type}");
                }

                IsAuthenticated = true;
                IsConnected = true;

                Debug.Log(
                    $"[HomeAssistantConnection] Authenticated successfully | ha_version={Safe(authResponse.ha_version)}",
                    this);

                await LoadInitialStatesAsync(cancellationToken);
                await SubscribeToStateChangedEventsAsync(cancellationToken);

                _receiveLoopTask = ReceiveLoopAsync(_lifetimeCancellation.Token);

                SetConnectionPhase(HomeAssistantConnectionPhase.Connected);
                NotifyConnectionStateChanged();

                Debug.Log(
                    $"[HomeAssistantConnection] Initial snapshot loaded | Count={_states.Count} | {BuildPreview()}",
                    this);
            }
            catch (OperationCanceledException)
            {
                LastError = $"Connection/auth timed out after {connectTimeoutSeconds} seconds.";
                Debug.LogError($"[HomeAssistantConnection] {LastError}", this);

                IsConnected = false;
                IsAuthenticated = false;
                SetConnectionPhase(HomeAssistantConnectionPhase.Disconnected);
                NotifyConnectionStateChanged();
                DisposeSocketIfAny();
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                Debug.LogError($"[HomeAssistantConnection] Connection/auth failed | {LastError}", this);

                IsConnected = false;
                IsAuthenticated = false;
                SetConnectionPhase(HomeAssistantConnectionPhase.Disconnected);
                NotifyConnectionStateChanged();
                DisposeSocketIfAny();
            }
        }

        public bool TryGetCachedState(string entityId, out HomeAssistantStateSummary state)
        {
            state = FindState(entityId);
            return state != null;
        }

        private async Task LoadInitialStatesAsync(CancellationToken cancellationToken)
        {
            HomeAssistantGetStatesRequest request = new HomeAssistantGetStatesRequest
            {
                id = GetNextMessageId()
            };

            string requestJson = JsonUtility.ToJson(request);
            LogRaw("SEND", requestJson);
            await SendTextMessageAsync(_webSocket, requestJson, cancellationToken);

            string responseJson = await ReceiveTextMessageAsync(_webSocket, cancellationToken);
            LogRaw("RECV", responseJson);

            HomeAssistantGetStatesResponse response =
                JsonUtility.FromJson<HomeAssistantGetStatesResponse>(responseJson);

            if (response == null)
            {
                throw new InvalidOperationException("get_states response could not be parsed.");
            }

            if (!response.success)
            {
                throw new InvalidOperationException("Home Assistant get_states request failed.");
            }

            _states.Clear();
            if (response.result != null)
            {
                _states.AddRange(response.result);
            }

            NotifyStatesChanged();
        }

        private async Task SubscribeToStateChangedEventsAsync(CancellationToken cancellationToken)
        {
            HomeAssistantSubscribeEventsRequest request = new HomeAssistantSubscribeEventsRequest
            {
                id = GetNextMessageId(),
                event_type = "state_changed"
            };

            string requestJson = JsonUtility.ToJson(request);
            LogRaw("SEND", requestJson);
            await SendTextMessageAsync(_webSocket, requestJson, cancellationToken);

            string responseJson = await ReceiveTextMessageAsync(_webSocket, cancellationToken);
            LogRaw("RECV", responseJson);

            HomeAssistantMessageEnvelope response =
                JsonUtility.FromJson<HomeAssistantMessageEnvelope>(responseJson);

            if (response == null)
            {
                throw new InvalidOperationException("subscribe_events response could not be parsed.");
            }

            if (!response.success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.message)
                        ? "Home Assistant rejected the state_changed subscription."
                        : response.message);
            }

            _stateChangedSubscriptionId = request.id;

            Debug.Log("[HomeAssistantConnection] Subscribed to state_changed events.", this);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       _webSocket != null &&
                       _webSocket.State == WebSocketState.Open)
                {
                    string json = await ReceiveTextMessageAsync(_webSocket, cancellationToken);
                    LogRaw("RECV", json);
                    DispatchInboundMessage(json);
                }
            }
            catch (OperationCanceledException)
            {
                // Play mode exit / object destroy
            }
            catch (Exception ex)
            {
                if (_lifetimeCancellation == null || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }

                LastError = $"Receive loop failed | {ex.GetType().Name}: {ex.Message}";
                Debug.LogWarning($"[HomeAssistantConnection] {LastError}", this);

                IsConnected = false;
                IsAuthenticated = false;
                SetConnectionPhase(HomeAssistantConnectionPhase.Disconnected);
                NotifyConnectionStateChanged();
            }
        }

        private void DispatchInboundMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            HomeAssistantMessageEnvelope envelope =
                JsonUtility.FromJson<HomeAssistantMessageEnvelope>(json);

            if (envelope == null)
            {
                return;
            }

            if (envelope.type == "event")
            {
                HandleEventMessage(json);
            }
        }

        private void HandleEventMessage(string json)
        {
            HomeAssistantEventEnvelope eventEnvelope =
                JsonUtility.FromJson<HomeAssistantEventEnvelope>(json);

            if (eventEnvelope == null)
            {
                return;
            }

            if (eventEnvelope.id != _stateChangedSubscriptionId)
            {
                return;
            }

            if (eventEnvelope.@event == null ||
                !string.Equals(eventEnvelope.@event.event_type, "state_changed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HomeAssistantStateChangedData stateChanged = eventEnvelope.@event.data;
            if (stateChanged == null || string.IsNullOrWhiteSpace(stateChanged.entity_id))
            {
                return;
            }

            if (stateChanged.new_state == null)
            {
                RemoveState(stateChanged.entity_id);

                Debug.Log(
                    $"[HomeAssistantConnection] State removed | Entity={stateChanged.entity_id}",
                    this);

                NotifyStatesChanged();
                return;
            }

            if (string.IsNullOrWhiteSpace(stateChanged.new_state.entity_id))
            {
                stateChanged.new_state.entity_id = stateChanged.entity_id;
            }

            UpsertState(stateChanged.new_state);
            StateUpdated?.Invoke(stateChanged.new_state);

            Debug.Log(
                $"[HomeAssistantConnection] State updated | Entity={stateChanged.new_state.entity_id} | State={stateChanged.new_state.state}",
                this);

            NotifyStatesChanged();
        }

        private HomeAssistantStateSummary FindState(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return null;
            }

            for (int i = 0; i < _states.Count; i++)
            {
                HomeAssistantStateSummary state = _states[i];
                if (string.Equals(state.entity_id, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }

            return null;
        }

        private void UpsertState(HomeAssistantStateSummary nextState)
        {
            if (nextState == null || string.IsNullOrWhiteSpace(nextState.entity_id))
            {
                return;
            }

            for (int i = 0; i < _states.Count; i++)
            {
                if (string.Equals(_states[i].entity_id, nextState.entity_id, StringComparison.OrdinalIgnoreCase))
                {
                    _states[i] = nextState;
                    return;
                }
            }

            _states.Add(nextState);
        }

        private void RemoveState(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return;
            }

            for (int i = _states.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_states[i].entity_id, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    _states.RemoveAt(i);
                    return;
                }
            }
        }

        private async Task SendTextMessageAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ArraySegment<byte> segment = new ArraySegment<byte>(bytes);
            await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
        }

        private async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
            StringBuilder builder = new StringBuilder();

            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(segment, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Server closed the WebSocket connection.");
                }

                if (result.Count > 0)
                {
                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }

                if (result.EndOfMessage)
                {
                    return builder.ToString();
                }
            }
        }

        private static string BuildWebSocketUrl(string configuredUrl)
        {
            string trimmed = configuredUrl.Trim().TrimEnd('/');

            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "wss://" + trimmed.Substring("https://".Length);
            }
            else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "ws://" + trimmed.Substring("http://".Length);
            }
            else if (!trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                     !trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "ws://" + trimmed;
            }

            if (!trimmed.EndsWith("/api/websocket", StringComparison.OrdinalIgnoreCase))
            {
                trimmed += "/api/websocket";
            }

            return trimmed;
        }

        private void DisposeSocketIfAny()
        {
            if (_webSocket == null)
            {
                return;
            }

            try
            {
                _webSocket.Dispose();
            }
            catch
            {
                // ignored
            }

            _webSocket = null;
        }

        private void NotifyStatesChanged()
        {
            StatesChanged?.Invoke(_states);
        }

        private void NotifyConnectionStateChanged()
        {
            ConnectionStateChanged?.Invoke();
        }

        private void SetConnectionPhase(HomeAssistantConnectionPhase nextPhase)
        {
            ConnectionPhase = nextPhase;
        }

        private int GetNextMessageId()
        {
            return _nextMessageId++;
        }

        private void LogRaw(string direction, string text)
        {
            if (!logRawMessages)
            {
                return;
            }

            Debug.Log($"[HomeAssistantConnection] {direction} {text}", this);
        }

        private string BuildPreview()
        {
            if (_states.Count == 0 || logStatePreviewCount <= 0)
            {
                return "No states returned.";
            }

            int previewCount = Mathf.Min(logStatePreviewCount, _states.Count);
            StringBuilder builder = new StringBuilder("Preview: ");

            for (int i = 0; i < previewCount; i++)
            {
                HomeAssistantStateSummary state = _states[i];
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(state.entity_id);
                builder.Append('=');
                builder.Append(state.state);
            }

            return builder.ToString();
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }
    }
}