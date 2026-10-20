using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using MRHomeAssistant.Runtime.Application.Settings;
using MRHomeAssistant.Runtime.Infrastructure.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.SceneManagement;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    [DisallowMultipleComponent]
    public sealed class HomeAssistantHudPresenter : MonoBehaviour
    {
        [SerializeField] private HomeAssistantConnection connection;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private bool autoFindConnection = true;
        [SerializeField, Range(0.6f, 1.5f)] private float panelDistance = 0.9f;

        private Canvas canvas;
        private RectTransform root;
        private RectTransform desktopBackground;
        private RectTransform devicesPage;
        private RectTransform settingsPage;
        private TMP_Text status;
        private TMP_Text summary;
        private TMP_Text feedback;
        private TMP_Text formMessage;
        private TMP_Text empty;
        private TMP_Text pageText;
        private TMP_InputField address;
        private TMP_InputField accessToken;
        private TMP_InputField search;
        private Button connectButton;
        private Button disconnectButton;
        private Button previous;
        private Button next;
        private Button devicesTab;
        private Button settingsTab;
        private Button demoButton;
        private DashboardKeyboard keyboard;
        private DashboardDeviceDetails details;
        private RectTransform roomsPage;
        private Button roomsButton, roomPrevious, roomNext;
        private TMP_Text roomPageText;
        private readonly List<Button> roomOptions = new();
        private readonly Dictionary<string, Button> views = new();
        private DashboardPreferencesRepository preferencesRepository;
        private DashboardPreferences preferences;
        private DashboardPreferences demoPreferences = new() { server = "demo" };
        private string areaId = "", view = "All";
        private bool showingRooms, refreshQueued;
        private int roomPage;
        private readonly List<DeviceRow> rows = new();
        private readonly Dictionary<string, Button> filters = new();
        private string category = "All";
        private int page;
        private bool showingSettings = true;
        private bool subscribed;
        private bool? xrMode;
        private float noticeUntil;
        private InputSystemUIInputModule desktopInput;
        private Behaviour pointableModule;
        private Transform shell;
        private bool placedWithTracking;

        private sealed class DeviceRow
        {
            public RectTransform Root;
            public TMP_Text Name;
            public TMP_Text Id;
            public TMP_Text State;
            public TMP_Text Access;
            public Toggle Power;
            public string EntityId;
            public HomeAssistantDeviceGroup Group;
            public Button Favorite;
        }

        private void Awake()
        {
            if (connection == null && autoFindConnection) connection = FindAnyObjectByType<HomeAssistantConnection>();
            BuildView();
            // These are the original standalone interaction examples, not household devices.
            foreach (var item in gameObject.scene.GetRootGameObjects())
                if (item.name == "Poke Interaction" || item.name == "[BuildingBlock] Cube") item.SetActive(false);
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (subscribed && connection != null)
            {
                connection.ConnectionStateChanged -= Refresh;
                connection.StatesChanged -= OnStatesChanged;
            }
            subscribed = false;
            keyboard?.Close();
        }

        private IEnumerator Start()
        {
            if (connection != null) connection.EvaluateConfiguration();
            LoadAddress();
            yield return null;
            ApplyDisplayMode();
            Refresh();
        }

        private void Subscribe()
        {
            if (subscribed || connection == null) return;
            connection.ConnectionStateChanged += Refresh;
            connection.StatesChanged += OnStatesChanged;
            subscribed = true;
        }

        // Also used by the editor preview/validation command, without opening a server connection.
        public void BuildView()
        {
            if (root != null) return;
            if (connection == null) connection = FindAnyObjectByType<HomeAssistantConnection>();
            canvas = titleText != null ? titleText.GetComponentInParent<Canvas>() : GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;
            shell = canvas.transform.parent;
            foreach (var layout in canvas.GetComponents<LayoutGroup>()) layout.enabled = false;
            Transform backplate = canvas.transform.Find("UIBackplate");
            if (backplate != null) backplate.gameObject.SetActive(false);
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.sizeDelta = new Vector2(DashboardUI.Width, DashboardUI.Height);
            canvasRect.localScale = Vector3.one * 0.0008f;
            desktopBackground = DashboardUI.Rect(canvas.transform, "Desktop background", 0, 0, 1000, 720);
            DashboardUI.Stretch(desktopBackground);
            DashboardUI.Fill(desktopBackground, DashboardUI.Background);
            desktopBackground.gameObject.SetActive(false);
            root = DashboardUI.Rect(canvas.transform, "Home dashboard", 0, 0, 1000, 720);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            DashboardUI.Fill(root, DashboardUI.Background, true);
            DashboardUI.Label(root, "Product", "MR Home Assistant", 32, 20, 360, 44, 30);
            status = DashboardUI.Label(root, "Connection status", "Not connected", 424, 20, 340, 44, 20, DashboardUI.Muted);
            DashboardUI.Button(root, "Recenter", "Recenter", 808, 20, 160, 48, Recenter);
            devicesTab = DashboardUI.Button(root, "Devices tab", "Devices", 32, 80, 180, 44, () => ShowSettings(false));
            settingsTab = DashboardUI.Button(root, "Connection tab", "Connection", 220, 80, 180, 44, () => ShowSettings(true));
            DashboardUI.Fill(DashboardUI.Rect(root, "Header rule", 32, 135, 936, 1), DashboardUI.Border);
            devicesPage = DashboardUI.Rect(root, "Devices page", 32, 150, 936, 504);
            settingsPage = DashboardUI.Rect(root, "Connection page", 32, 150, 936, 504);
            BuildDevices();
            BuildSettings();
            BuildRooms();
            feedback = DashboardUI.Label(root, "Feedback", "", 32, 668, 936, 36, 18, DashboardUI.Muted);
            keyboard = new DashboardKeyboard(root, Refresh);
            details = new DashboardDeviceDetails(root, connection, () => { details.Close(); Refresh(); }, ShowNotice, RecordUse);
            Subscribe();
            LoadAddress();
            Refresh();
        }

        private void BuildDevices()
        {
            search = DashboardUI.Input(devicesPage, "Search devices", "Search devices", 0, 0, 420);
            search.onValueChanged.AddListener(_ => { page = 0; RefreshDevices(); });
            search.onSelect.AddListener(_ => { if (UseXR()) OpenKeyboard(search); });
            DashboardUI.Button(devicesPage, "Clear search", "Clear", 432, 0, 96, 54, () => search.text = "");
            roomsButton = DashboardUI.Button(devicesPage, "Rooms", "All rooms", 544, 0, 392, 54,
                () => { showingRooms = true; roomPage = 0; Refresh(); });
            summary = DashboardUI.Label(root, "Device summary", "", 424, 80, 544, 44, 20, DashboardUI.Muted, TextAlignmentOptions.MidlineRight);
            int i = 0;
            foreach (string name in new[] { "All", "Lights", "Switches", "Sensors" })
            {
                string selected = name;
                filters[name] = DashboardUI.Button(devicesPage, "Filter " + name, name, i++ * 124, 66, 116, 44,
                    () => { category = selected; page = 0; RefreshDevices(); });
            }
            i = 0;
            foreach (string name in new[] { "All", "Favorites", "Recent" })
            {
                string selected = name;
                views[name] = DashboardUI.Button(devicesPage, "View " + name, name, 520 + i++ * 140, 66, 136, 44,
                    () => { view = selected; page = 0; RefreshDevices(); });
            }
            for (int rowIndex = 0; rowIndex < 5; rowIndex++)
            {
                var row = new DeviceRow();
                row.Root = DashboardUI.Rect(devicesPage, "Device row " + rowIndex, 0, 126 + rowIndex * 64, 936, 60);
                DashboardUI.Fill(row.Root, DashboardUI.Surface);
                row.Favorite = DashboardUI.Button(row.Root, "Favorite device", "\u2606", 4, 8, 48, 44, () => ToggleFavorite(row));
                row.Name = DashboardUI.Label(row.Root, "Device name", "", 64, 0, 410, 34, 22);
                row.Id = DashboardUI.Label(row.Root, "Entity ID", "", 64, 34, 410, 24, 16, DashboardUI.Muted);
                row.State = DashboardUI.Label(row.Root, "State", "", 488, 0, 264, 60, 22);
                row.Access = DashboardUI.Label(row.Root, "Access", "Read only", 756, 0, 120, 60, 17, DashboardUI.Muted, TextAlignmentOptions.Center);
                row.Power = DashboardUI.Power(row.Root, 770, 4, on => SetPower(row, on));
                DashboardUI.Button(row.Root, "Open device details", ">", 884, 8, 48, 44, () =>
                { if (row.Group != null) { details.Open(row.Group); Refresh(); } });
                rows.Add(row);
            }
            empty = DashboardUI.Label(devicesPage, "Empty state", "", 24, 178, 888, 172, 26, DashboardUI.Muted, TextAlignmentOptions.Center);
            empty.textWrappingMode = TextWrappingModes.Normal;
            previous = DashboardUI.Button(devicesPage, "Previous page", "<", 0, 458, 64, 44, () => { page--; RefreshDevices(); });
            pageText = DashboardUI.Label(devicesPage, "Page", "", 80, 458, 420, 44, 18, DashboardUI.Muted);
            next = DashboardUI.Button(devicesPage, "Next page", ">", 872, 458, 64, 44, () => { page++; RefreshDevices(); });
        }

        private void BuildSettings()
        {
            DashboardUI.Label(settingsPage, "Heading", "Connect your home", 0, 0, 936, 44, 28);
            DashboardUI.Label(settingsPage, "Server label", "Server address", 0, 64, 936, 28, 20, DashboardUI.Muted);
            address = DashboardUI.Input(settingsPage, "Server address", "http://homeassistant.local:8123", 0, 98, 820);
            DashboardUI.Button(settingsPage, "Edit address", "Edit", 836, 98, 100, 54, () => OpenKeyboard(address));
            DashboardUI.Label(settingsPage, "Token label", "Access token", 0, 176, 936, 28, 20, DashboardUI.Muted);
            accessToken = DashboardUI.Input(settingsPage, "Access token", "Long-lived access token", 0, 210, 820, true);
            DashboardUI.Button(settingsPage, "Edit token", "Edit", 836, 210, 100, 54, () => OpenKeyboard(accessToken));
            address.onSelect.AddListener(_ => { if (UseXR()) OpenKeyboard(address); });
            accessToken.onSelect.AddListener(_ => { if (UseXR()) OpenKeyboard(accessToken); });
            DashboardUI.Label(settingsPage, "Credential lifetime", "Token is used for this session only.", 0, 276, 936, 28, 18, DashboardUI.Muted);
            formMessage = DashboardUI.Label(settingsPage, "Form status", "", 0, 312, 936, 58, 20, DashboardUI.Muted);
            formMessage.textWrappingMode = TextWrappingModes.Normal;
            connectButton = DashboardUI.Button(settingsPage, "Connect", "Connect", 0, 384, 292, 56, Connect, true);
            disconnectButton = DashboardUI.Button(settingsPage, "Disconnect", "Disconnect", 308, 384, 292, 56, () =>
            {
                connection?.Disconnect();
                ShowNotice("Disconnected.", false);
            });
            demoButton = DashboardUI.Button(settingsPage, "Demo", "Open demo", 616, 384, 320, 56, () =>
            {
                keyboard.Close();
                accessToken.text = "";
                connection?.StartDemo();
                category = "All";
                search.text = "";
                page = 0;
                ShowSettings(false);
            });
            DashboardUI.Button(settingsPage, "Forget credentials", "Forget credentials", 0, 456, 292, 48, () =>
            {
                connection?.ForgetCredentials();
                accessToken.text = "";
                try { new AppSettingsRepository().Save(new AppSettingsData { homeAssistantUrl = connection?.CurrentUrl ?? "" }); }
                catch (Exception) { ShowNotice("Session cleared. Could not remove legacy settings from disk.", true); return; }
                ShowNotice("Credentials removed from this device.", false);
            });
        }

        private void BuildRooms()
        {
            roomsPage = DashboardUI.Rect(root, "Rooms page", 32, 150, 936, 504);
            DashboardUI.Button(roomsPage, "Back from rooms", "<", 0, 0, 64, 48, () => { showingRooms = false; Refresh(); });
            DashboardUI.Label(roomsPage, "Rooms heading", "Rooms", 80, 0, 856, 48, 28);
            for (int i = 0; i < 5; i++)
                roomOptions.Add(DashboardUI.Button(roomsPage, "Room option " + i, "", 0, 72 + i * 68, 936, 56, () => { }));
            roomPrevious = DashboardUI.Button(roomsPage, "Previous rooms", "<", 0, 458, 64, 44, () => { roomPage--; RefreshRooms(); });
            roomPageText = DashboardUI.Label(roomsPage, "Room page", "", 80, 458, 720, 44, 18, DashboardUI.Muted);
            roomNext = DashboardUI.Button(roomsPage, "Next rooms", ">", 872, 458, 64, 44, () => { roomPage++; RefreshRooms(); });
        }

        private void RefreshRooms()
        {
            var options = new List<(string id, string name)> { ("", "All rooms"), ("__unassigned", "Unassigned") };
            if (connection != null) options.AddRange(connection.Catalog.Areas.OrderBy(a => a.name).Select(a => (a.area_id, a.name)));
            int pages = Mathf.Max(1, Mathf.CeilToInt(options.Count / 5f));
            roomPage = Mathf.Clamp(roomPage, 0, pages - 1);
            for (int i = 0; i < roomOptions.Count; i++)
            {
                var button = roomOptions[i];
                int index = roomPage * 5 + i;
                button.gameObject.SetActive(index < options.Count);
                if (index >= options.Count) continue;
                var option = options[index];
                button.GetComponentInChildren<TMP_Text>().text = option.name;
                button.image.color = option.id == areaId ? DashboardUI.Border : DashboardUI.Surface;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => { areaId = option.id; page = 0; showingRooms = false; Refresh(); });
            }
            roomPrevious.interactable = roomPage > 0; roomNext.interactable = roomPage + 1 < pages;
            roomPageText.text = "Page " + (roomPage + 1) + " of " + pages;
        }

        private void LoadPreferences()
        {
            preferencesRepository ??= new DashboardPreferencesRepository();
            string key = connection == null ? "" : connection.IsDemo ? "demo" : connection.CurrentWebSocketUrl;
            if (preferences?.server == key) return;
            preferences = key == "demo" ? demoPreferences : preferencesRepository.Load(key);
            areaId = ""; page = 0; view = "All";
        }

        private void ToggleFavorite(DeviceRow row)
        {
            if (row.Group == null) return;
            preferences.ToggleFavorite(row.Group.Id);
            SavePreferences(); RefreshDevices();
        }

        private void RecordUse(string id)
        {
            preferences.RecordUse(id); SavePreferences(); RefreshDevices();
        }

        private void SavePreferences()
        {
            try { preferencesRepository.Save(preferences); }
            catch (Exception) { ShowNotice("Preference changed for this session; local save failed.", true); }
        }

        private void OpenKeyboard(TMP_InputField field)
        {
            if (keyboard == null || keyboard.IsOpen || !field.interactable) return;
            devicesPage.gameObject.SetActive(false);
            settingsPage.gameObject.SetActive(false);
            details?.Close(); showingRooms = false; roomsPage.gameObject.SetActive(false);
            keyboard.Open(field);
        }

        private void LoadAddress()
        {
            if (address != null && new AppSettingsRepository().TryLoad(out AppSettingsData data))
                address.SetTextWithoutNotify(data.homeAssistantUrl ?? "");
        }

        public void ShowSettings(bool value)
        {
            keyboard?.Close();
            details?.Close(); showingRooms = false;
            showingSettings = value;
            Refresh();
        }

        private async void Connect()
        {
            if (connection == null || connection.IsBusy) return;
            keyboard.Close();
            bool reconnect = string.IsNullOrWhiteSpace(accessToken.text) && connection.HasValidConfiguration &&
                address.text.Trim() == connection.CurrentUrl;
            if (!reconnect && !connection.Configure(address.text, accessToken.text, out string error))
            {
                formMessage.text = error;
                formMessage.color = DashboardUI.Error;
                return;
            }
            accessToken.text = "";
            ShowNotice("Connecting...", false);
            await connection.ConnectAndInitializeAsync();
            if (this == null || root == null) return;
            if (connection.IsConnected)
            {
                try
                {
                    new AppSettingsRepository().Save(new AppSettingsData
                    {
                        homeAssistantUrl = connection.CurrentUrl, onboardingCompleted = true
                    });
                    ShowNotice("Connected. Device states are live.", false);
                }
                catch (Exception) { ShowNotice("Connected. Could not remember the server address on this device.", true); }
                ShowSettings(false);
            }
            else ShowNotice(string.IsNullOrEmpty(connection.LastError) ? "Connection cancelled." : connection.LastError, true);
        }

        private async void SetPower(DeviceRow row, bool on)
        {
            string id = row.EntityId;
            string groupId = row.Group?.Id;
            var profile = preferences;
            if (string.IsNullOrEmpty(id) || connection == null) return;
            // Reset the switch to the last confirmed state until Home Assistant reports the result.
            RefreshDevices();
            string error = await connection.SetPowerAsync(id, on);
            if (this == null || root == null) return;
            if (string.IsNullOrEmpty(error) && profile == preferences && groupId != null) RecordUse(groupId);
            ShowNotice(string.IsNullOrEmpty(error) ? (connection.IsDemo ? "Demo device updated." : "Device state confirmed.") : error,
                !string.IsNullOrEmpty(error));
            RefreshDevices();
        }

        private void ShowNotice(string value, bool error)
        {
            noticeUntil = Time.unscaledTime + (error ? 15 : 5);
            if (feedback != null) { feedback.text = value; feedback.color = error ? DashboardUI.Error : DashboardUI.Accent; }
        }

        private void OnStatesChanged(IReadOnlyList<HomeAssistantStateSummary> _)
        {
            if (UnityEngine.Application.isPlaying) refreshQueued = true;
            else Refresh();
        }

        public void Refresh()
        {
            if (root == null) return;
            LoadPreferences();
            bool demo = connection != null && connection.IsDemo;
            bool connected = connection != null && connection.IsConnected;
            bool busy = connection != null && connection.IsBusy;
            status.text = demo ? "DEMO / Sample devices" : connected ? "Connected / Live" :
                connection?.ReconnectAttempt > 0 && busy ? "Reconnecting " + connection.ReconnectAttempt + "/5" : busy ? "Connecting..." : "Not connected";
            status.color = demo ? DashboardUI.Warning : connected ? DashboardUI.Accent : DashboardUI.Muted;
            devicesTab.image.color = !showingSettings ? DashboardUI.Border : DashboardUI.Surface;
            settingsTab.image.color = showingSettings ? DashboardUI.Border : DashboardUI.Surface;
            bool typing = keyboard != null && keyboard.IsOpen;
            devicesPage.gameObject.SetActive(!typing && !showingSettings && !showingRooms && details?.IsOpen != true);
            settingsPage.gameObject.SetActive(!typing && showingSettings);
            roomsPage?.gameObject.SetActive(!typing && !showingSettings && showingRooms);
            details?.SetVisible(!typing && !showingSettings && !showingRooms);
            details?.Refresh();
            if (showingRooms) RefreshRooms();
            connectButton.interactable = connection != null && !busy;
            connectButton.GetComponentInChildren<TMP_Text>().text = connected ? "Reconnect" : busy ? "Connecting..." : "Connect";
            disconnectButton.interactable = connection != null && (busy || connected || demo || connection.IsStale);
            disconnectButton.GetComponentInChildren<TMP_Text>().text = busy ? "Cancel" : demo ? "Exit demo" : "Disconnect";
            demoButton.interactable = connection != null && !busy && !connected;
            address.interactable = accessToken.interactable = !busy;
            formMessage.text = connection == null ? "Connection component is missing." :
                !string.IsNullOrEmpty(connection.LastError) ? connection.LastError :
                busy ? "Contacting Home Assistant..." : connected ? "Connected to " + connection.CurrentUrl :
                demo ? "Demo session. No physical devices are connected." : "Ready to connect.";
            formMessage.color = connection != null && !string.IsNullOrEmpty(connection.LastError) ? DashboardUI.Error : DashboardUI.Muted;
            RefreshDevices();
            RefreshFooter();
        }

        private void RefreshDevices()
        {
            if (root == null || search == null || summary == null) return;
            var all = connection != null ? connection.States : Array.Empty<HomeAssistantStateSummary>();
            LoadPreferences();
            var list = (connection?.Catalog ?? new HomeAssistantCatalog()).Group(all, category, search.text, areaId);
            if (view == "Favorites") list = list.Where(g => preferences.IsFavorite(g.Id)).ToList();
            if (view == "Recent") list = list.Where(g => preferences.recent.Contains(g.Id)).OrderBy(g => preferences.recent.IndexOf(g.Id)).ToList();
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(list.Count / 5f));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            summary.text = all.Count + " entities / " + all.Count(s => HomeAssistantDevices.CanControl(s) && s.state == "on") + " on";
            foreach (var filter in filters) filter.Value.image.color = filter.Key == category ? DashboardUI.Border : DashboardUI.Surface;
            foreach (var item in views) item.Value.image.color = item.Key == view ? DashboardUI.Border : DashboardUI.Surface;
            roomsButton.GetComponentInChildren<TMP_Text>().text = areaId == "" ? "All rooms" : areaId == "__unassigned" ? "Unassigned" :
                connection?.Catalog.Areas.FirstOrDefault(a => a.area_id == areaId)?.name ?? "Room unavailable";
            for (int i = 0; i < rows.Count; i++)
            {
                DeviceRow row = rows[i];
                int index = page * 5 + i;
                row.Root.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) { row.EntityId = null; row.Group = null; continue; }
                row.Group = list[index];
                var item = row.Group.Entities.First(s => HomeAssistantDevices.InCategory(s, category));
                row.EntityId = item.entity_id;
                row.Name.text = row.Group.Name;
                row.Id.text = row.Group.AreaName + " / " + (row.Group.Entities.Count > 1 ? row.Group.Entities.Count + " functions" : item.entity_id);
                row.Favorite.GetComponentInChildren<TMP_Text>().text = preferences.IsFavorite(row.Group.Id) ? "\u2605" : "\u2606";
                bool pending = connection.IsPending(item.entity_id);
                bool available = HomeAssistantDevices.Available(item);
                bool controlDomain = HomeAssistantDevices.Domain(item) == "light" || HomeAssistantDevices.Domain(item) == "switch" ||
                    HomeAssistantDevices.Domain(item) == "input_boolean";
                row.State.text = connection.IsStale ? "Cached / " + HomeAssistantDevices.StateLabel(item) : pending ? "Updating..." : HomeAssistantDevices.StateLabel(item);
                row.State.color = pending || !available ? DashboardUI.Warning : item.state == "on" ? DashboardUI.Accent : DashboardUI.Text;
                row.Power.gameObject.SetActive(controlDomain);
                row.Access.gameObject.SetActive(!controlDomain);
                row.Power.SetIsOnWithoutNotify(item.state == "on");
                row.Power.transform.Find("Thumb").GetComponent<RectTransform>().anchoredPosition = new Vector2(item.state == "on" ? 52 : 8, -8);
                row.Power.interactable = HomeAssistantDevices.CanControl(item) && !pending && (connection.IsConnected || connection.IsDemo);
                row.Power.graphic.color = row.Power.interactable ? DashboardUI.Accent : DashboardUI.Muted;
            }
            empty.gameObject.SetActive(list.Count == 0);
            empty.text = connection == null || (!connection.IsConnected && !connection.IsDemo)
                ? "No connection\nOpen Connection to connect or start a demo."
                : all.Count == 0 ? "No entities returned by Home Assistant."
                : "No matching devices.";
            previous.interactable = page > 0;
            next.interactable = page + 1 < pageCount;
            pageText.text = list.Count == 0 ? "0 results" : "Page " + (page + 1) + " of " + pageCount + " / " + list.Count + " results";
        }

        private void RefreshFooter()
        {
            if (feedback == null || Time.unscaledTime < noticeUntil) return;
            feedback.color = DashboardUI.Muted;
            feedback.text = connection != null && connection.IsDemo ? "Demo / Changes affect sample devices only." :
                connection != null && !string.IsNullOrEmpty(connection.LastError) ? connection.LastError :
                connection != null && !string.IsNullOrEmpty(connection.MetadataStatus) ? connection.MetadataStatus :
                connection != null && !string.IsNullOrEmpty(connection.LastActionStatus) ? connection.LastActionStatus + " / " + connection.LastActionMilliseconds + " ms" :
                connection?.LastStateUpdate != null ? "Last update " + connection.LastStateUpdate.Value.ToString("HH:mm:ss") :
                "Home Assistant";
        }

        private void Update()
        {
            if (root == null) return;
            if (refreshQueued) { refreshQueued = false; Refresh(); }
            ApplyDisplayMode();
            if (xrMode == true && !placedWithTracking &&
                UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head)
                    .TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked)
            {
                Recenter();
                placedWithTracking = true;
            }
            RefreshFooter();
            if (Keyboard.current?.homeKey.wasPressedThisFrame == true ||
                (xrMode == true && OVRInput.GetDown(OVRInput.Button.Start))) Recenter();
        }

        private static bool UseXR() => XRSettings.isDeviceActive || UnityEngine.Application.platform == RuntimePlatform.Android;

        private void ApplyDisplayMode()
        {
            bool xr = UseXR();
            if (xrMode == xr) return;
            xrMode = xr;
            desktopBackground.gameObject.SetActive(!xr);
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (xr)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)canvas.transform;
                rt.sizeDelta = new Vector2(1000, 720);
                rt.localScale = Vector3.one * 0.0008f;
                if (desktopInput != null) desktopInput.enabled = false;
                if (pointableModule != null) pointableModule.enabled = true;
                Recenter();
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1048, 768);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                var eventSystem = EventSystem.current;
                if (eventSystem == null) eventSystem = new GameObject("UI EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
                foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                    if (module.GetType().Name == "PointableCanvasModule") { pointableModule = module; module.enabled = false; }
                desktopInput = eventSystem.GetComponent<InputSystemUIInputModule>();
                if (desktopInput == null) desktopInput = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                desktopInput.enabled = true;
                desktopInput.AssignDefaultActions();
            }
        }

        public void Recenter()
        {
            if (shell == null || canvas == null || canvas.renderMode != RenderMode.WorldSpace) return;
            Camera head = Camera.allCameras.FirstOrDefault(camera => camera.name == "CenterEyeAnchor") ?? Camera.main;
            if (head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            shell.position = head.transform.position + forward * panelDistance - Vector3.up * 0.12f;
            shell.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
