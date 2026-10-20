using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MRHomeAssistant.Runtime.App;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using MRHomeAssistant.Runtime.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

namespace MRHomeAssistant.Editor
{
    [InitializeOnLoad]
    public static class HomeAssistantPlayModeValidation
    {
        private const string RunKey = "MRHomeAssistant.PlayModeValidation";
        private const string ResultKey = RunKey + ".ExitCode";
        private static int assertions;
        private static Mouse mouse;
        private static InputSettings originalInputSettings;
        private static InputSettings testInputSettings;
        private static XRGeneralSettings DesktopXR => XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);

        static HomeAssistantPlayModeValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // Automation entry: modifies the scene in memory, never saves it or contacts a real server.
        [MenuItem("Tools/MR Home Assistant/Run Play Mode Validation and Exit")]
        public static void RunBatch()
        {
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            var connection = Object.FindAnyObjectByType<HomeAssistantConnection>();
            var settings = new SerializedObject(connection);
            settings.FindProperty("autoConnectOnStart").boolValue = false;
            settings.FindProperty("autoEvaluateConfigurationOnStart").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var bootstrap = new SerializedObject(Object.FindAnyObjectByType<AppBootstrap>());
            bootstrap.FindProperty("initializeAppSettingsOnStart").boolValue = false;
            bootstrap.ApplyModifiedPropertiesWithoutUndo();
            SessionState.SetBool(RunKey + ".XRStartup", DesktopXR.InitManagerOnStart);
            DesktopXR.InitManagerOnStart = false;
            SessionState.SetBool(RunKey, true);
            SessionState.SetInt(ResultKey, 1);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) RunChecks();
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(RunKey, false);
                DesktopXR.InitManagerOnStart = SessionState.GetBool(RunKey + ".XRStartup", true);
                EditorApplication.Exit(SessionState.GetInt(ResultKey, 1));
            }
        }

        private static async void RunChecks()
        {
            try
            {
                await Frames(6);
                assertions = 0;
                originalInputSettings = InputSystem.settings;
                testInputSettings = Object.Instantiate(originalInputSettings);
                testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = testInputSettings;
                var connection = Object.FindAnyObjectByType<HomeAssistantConnection>();
                var presenter = Object.FindAnyObjectByType<HomeAssistantHudPresenter>();
                var canvas = Object.FindObjectsByType<Canvas>()
                    .Single(c => c.transform.Find("Home dashboard") != null);
                var root = canvas.transform.Find("Home dashboard");
                Require(!connection.IsConnected && !connection.IsBusy, "No automatic real-server connection");
                Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Desktop screen-space layout");
                Require(EventSystem.current.currentInputModule is InputSystemUIInputModule, "Desktop Input System module active");
                Require(!EventSystem.current.GetComponents<BaseInputModule>()
                    .Any(module => module.enabled && module.GetType().Name == "PointableCanvasModule"), "XR input module not competing with mouse");
                Require(Find<Button>(root, "Connect").GetComponentInChildren<TMP_Text>().color.grayscale < 0.2f,
                    "UISet theme preserves dark text on the primary button");
                mouse = InputSystem.AddDevice<Mouse>("UI validation mouse");
                UnityEngine.Application.runInBackground = true;
                await Frames(2);
                var token = Find<TMP_InputField>(root, "Access token");
                var address = Find<TMP_InputField>(root, "Server address");
                token.text = "";
                address.text = "";
                await Capture("playmode-initial.png");
                await Click(Find<Button>(root, "Connect"));
                Require(Find<TMP_Text>(root, "Form status").text.Contains("valid"), "Invalid address produces feedback");

                await Click(Find<Button>(root, "Edit token"));
                await Click(Find<Button>(root, "Key 0"));
                await Click(Find<Button>(root, "Key 1"));
                await Click(Find<Button>(root, "Done"));
                Require(token.text == "12", "Mouse keyboard appends characters");
                await Click(Find<Button>(root, "Edit token"));
                await Click(Find<Button>(root, "Key 2"));
                await Click(Find<Button>(root, "Cancel"));
                Require(token.text == "12", "Keyboard cancel preserves original value");
                token.text = "";

                await Click(Find<Button>(root, "Demo"));
                Require(connection.IsDemo && connection.States.Count == 8, "Mouse opens isolated demo");
                await Capture("playmode-devices.png");
                await Click(Find<Button>(root, "Next page"));
                Require(Find<TMP_Text>(root, "Page").text.Contains("2 of 2"), "Mouse paging");
                await Click(Find<Button>(root, "Filter Lights"));
                Require(root.GetComponentsInChildren<Toggle>().Length == 3, "Mouse category filter");
                var toggle = root.GetComponentsInChildren<Toggle>().First();
                bool wasOn = toggle.isOn;
                await Click(toggle);
                await Task.Delay(400);
                await Frames(2);
                Require(toggle.isOn != wasOn, "Mouse switch updates confirmed demo state");
                var search = Find<TMP_InputField>(root, "Search devices");
                await Click(search);
                Require(search.isFocused, "Mouse focuses search input");
                // TMP consumes IMGUI text events, not InputSystem.QueueTextEvent.
                foreach (char letter in "desk") search.ProcessEvent(Event.KeyboardEvent(letter.ToString()));
                await Frames(3);
                Require(search.text == "desk" && root.GetComponentsInChildren<Toggle>().Length == 1, "TMP text-event path filters devices");
                await Click(root.GetComponentsInChildren<Button>().Single(b => b.name == "Favorite device"));
                await Click(Find<Button>(root, "View Favorites"));
                Require(root.GetComponentsInChildren<Toggle>().Length == 1, "Mouse favorite filter");
                await Click(root.GetComponentsInChildren<Button>().Single(b => b.name == "Open device details"));
                Require(Find<TMP_Text>(root, "Detail title").text.Contains("Desk"), "Mouse opens device details");
                Require(!Find<Button>(root, "Apply light settings").interactable, "Reported slider values are not an unsaved edit");
                await Click(Find<Slider>(root, "Brightness"));
                await Click(Find<Button>(root, "Apply light settings"));
                await Task.Delay(400); await Frames(2);
                Require(connection.TryGetCachedState("light.desk", out var desk) && desk.attributes.brightness >= 120 && desk.attributes.brightness <= 135,
                    "Pointer slider commits and confirms brightness");
                await Capture("playmode-details.png");
                await Click(Find<Button>(root, "Back to devices"));
                await Click(Find<Button>(root, "View Recent"));
                Require(root.GetComponentsInChildren<Toggle>().Length == 1, "Recent view contains confirmed device");
                await Click(Find<Button>(root, "View All"));
                await Click(Find<Button>(root, "Clear search"));
                Require(search.text == "", "Mouse clears search");
                await Click(Find<Button>(root, "Rooms"));
                await Click(root.GetComponentsInChildren<Button>().Single(b => b.name.StartsWith("Room option") && b.GetComponentInChildren<TMP_Text>().text == "Study"));
                Require(root.GetComponentsInChildren<Toggle>().Length == 1, "Mouse room selection");
                await Click(Find<Button>(root, "Connection tab"));
                await Click(Find<Button>(root, "Disconnect"));
                Require(!connection.IsDemo && connection.States.Count == 0, "Mouse exits demo without stale state");
                await Capture("playmode-connection.png");
                string[] args = Environment.GetCommandLineArgs();
                int endpointArg = Array.IndexOf(args, "-mrValidationUrl");
                bool nativeProtocol = endpointArg >= 0 && endpointArg + 1 < args.Length;
                if (nativeProtocol)
                {
                    string endpoint = args[endpointArg + 1];
                    Require(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host == "127.0.0.1", "Native protocol test is loopback only");
                    Require(connection.Configure(endpoint, "test-session", out _), "Native JSON test configures mock endpoint");
                    await connection.ConnectAndInitializeAsync();
                    await Task.Delay(500); await Frames(2);
                    Require(connection.IsConnected && connection.Catalog.IsLoaded && connection.Catalog.Areas.Count == 1,
                        "Native JsonUtility decodes state and registry DTOs");
                    Require(await connection.SetLightAsync("light.desk", 140, 4300) == "" &&
                        connection.TryGetCachedState("light.desk", out var liveDesk) && liveDesk.attributes.color_temp_kelvin == 4300,
                        "Native JsonUtility encodes light settings and confirms returned values");
                    connection.SetApplicationPaused(true);
                    Require(connection.IsStale && !connection.IsConnected, "Native pause retains read-only snapshot");
                    presenter.ShowSettings(false);
                    await Frames(2);
                    Require(root.GetComponentsInChildren<Toggle>().Any() && root.GetComponentsInChildren<Toggle>().All(t => !t.interactable) &&
                        root.GetComponentsInChildren<TMP_Text>().Any(t => t.text.StartsWith("Cached /")), "Stale device controls are visibly marked and disabled");
                    await Capture("playmode-stale.png");
                    connection.SetApplicationPaused(false);
                    await Task.Delay(600); await Frames(2);
                    Require(connection.IsConnected && !connection.IsStale, "Native resume resynchronizes snapshot");
                    connection.ForgetCredentials();
                    Require(!connection.HasValidConfiguration, "Native forget clears credentials");
                }
                Directory.CreateDirectory("Docs/Validation");
                File.WriteAllText("Docs/Validation/unity-playmode-checks.txt", assertions +
                    " Play Mode assertions passed at " + DateTime.UtcNow.ToString("O") +
                    "\nBootstrap scene; synthetic Input System mouse and TMP text events; demo devices.\n" +
                    (nativeProtocol ? "Native JsonUtility and WebSocket checks against loopback mock. No physical devices.\n" : "No live WebSocket test.\n"));
                Debug.Log("[MR Home Assistant] " + assertions + " Play Mode checks passed.");
                SessionState.SetInt(ResultKey, 0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[MR Home Assistant] Play Mode validation failed: " + exception);
            }
            finally
            {
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (originalInputSettings != null) InputSystem.settings = originalInputSettings;
                if (testInputSettings != null) Object.DestroyImmediate(testInputSettings);
                Object.FindAnyObjectByType<HomeAssistantConnection>()?.Disconnect();
                EditorApplication.ExitPlaymode();
            }
        }

        private static T Find<T>(Transform root, string name) where T : Component =>
            root.GetComponentsInChildren<T>(true).Single(item => item.name == name);

        private static async Task Click(Selectable control)
        {
            Require(control.IsActive() && control.IsInteractable(), "Active pointer target: " + control.name);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)control.transform;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Selectable>() == control,
                "Unobstructed pointer hit: " + control.name);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            await Frames(2);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            await Frames(2);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            await Frames(2);
        }

        private static async Task Frames(int count)
        {
            int start = Time.frameCount;
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (Time.frameCount - start < count)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play Mode stopped advancing frames.");
                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(30);
            }
        }

        private static async Task Capture(string name)
        {
            Directory.CreateDirectory("Docs/Validation");
            string path = Path.GetFullPath(Path.Combine("Docs/Validation", name));
            DateTime started = DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(path);
            await Frames(4);
            Require(File.Exists(path) && new FileInfo(path).Length > 4000 && File.GetLastWriteTimeUtc(path) >= started,
                "Fresh Game-view capture: " + name);
            var image = new Texture2D(2, 2);
            try
            {
                image.LoadImage(File.ReadAllBytes(path));
                var pixels = image.GetPixels32();
                Require(pixels.Count(pixel => pixel.r > 120 && pixel.g > 120 && pixel.b > 120) > 500 &&
                    pixels.Count(pixel => pixel.r > 220 && pixel.b > 220 && pixel.g < 40) < 100,
                    "Nonblank Game view without missing-shader pixels: " + name);
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }
    }
}
