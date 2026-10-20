using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using MRHomeAssistant.Runtime.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MRHomeAssistant.Editor
{
    public static class HomeAssistantUIValidation
    {
        private const string ShellPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs/Backplate/EmptyUIBackplateWithCanvas.prefab";
        private static int assertions;

        [MenuItem("Tools/MR Home Assistant/Validate UI and Capture")]
        public static async void RunInteractive() => await Validate(false);

        // Automation entry: keep graphics enabled and omit -quit so async checks can finish.
        public static async void RunBatch() => await Validate(true);

        private static async Task Validate(bool batch)
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            HomeAssistantConnection connection = null;
            int exitCode = 1;
            assertions = 0;
            try
            {
                var host = new GameObject("UI validation");
                SceneManager.MoveGameObjectToScene(host, preview);
                connection = host.AddComponent<HomeAssistantConnection>();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShellPath);
                Require(prefab != null, "Official UISet shell resolves");
                var shell = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                shell.transform.SetParent(host.transform, false);
                var presenter = host.AddComponent<HomeAssistantHudPresenter>();
                var serialized = new SerializedObject(presenter);
                serialized.FindProperty("connection").objectReferenceValue = connection;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                presenter.BuildView();
                presenter.ShowSettings(true);
                foreach (var item in host.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
                var canvas = host.GetComponentInChildren<Canvas>();
                Require(canvas != null, "Canvas exists");
                string[] names = host.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).Select(c => c.GetType().Name).ToArray();
                Require(names.Contains("PointableCanvas") && names.Contains("PokeInteractable") && names.Contains("RayInteractable"), "Official ray and poke paths remain present");

                string directory = Path.GetFullPath("Docs/Validation");
                Directory.CreateDirectory(directory);
                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(250);
                await Capture(canvas, preview, Path.Combine(directory, "connection.png"), 1440, 1080);
                var token = host.GetComponentsInChildren<TMP_InputField>(true).Single(input => input.name == "Access token");
                Require(token.contentType == TMP_InputField.ContentType.Password, "Token field masks input");
                Require(host.GetComponentsInChildren<Button>(true).Single(button => button.name == "Disconnect").interactable == false, "Disconnected action is disabled");
                host.GetComponentsInChildren<Button>(true).Single(button => button.name == "Demo").onClick.Invoke();
                Require(connection.IsDemo && connection.States.Count == 8, "Demo button works");
                await Capture(canvas, preview, Path.Combine(directory, "devices.png"), 1440, 1080);
                await Capture(canvas, preview, Path.Combine(directory, "devices-portrait.png"), 900, 1200);
                CheckBounds(host);
                Require(connection.Catalog.Group(connection.States).Count == 7, "Device functions aggregate into seven rows");
                var deskLabel = host.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Device name" && t.text.Contains("Desk"));
                deskLabel.ForceMeshUpdate();
                Require(deskLabel.textInfo.characterInfo.Any(c => c.character == '\u684c' && c.fontAsset.name.Contains("CJK")), "Chinese device name resolves to bundled font");
                host.GetComponentsInChildren<Button>().First(b => b.name == "Favorite device").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name == "View Favorites").onClick.Invoke();
                Require(host.GetComponentsInChildren<RectTransform>().Count(r => r.name.StartsWith("Device row ")) == 1, "Favorites view filters devices");
                host.GetComponentsInChildren<Button>().Single(b => b.name == "View All").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Rooms").onClick.Invoke();
                await Capture(canvas, preview, Path.Combine(directory, "rooms.png"), 1440, 1080);
                host.GetComponentsInChildren<Button>().Single(b => b.name.StartsWith("Room option") && b.GetComponentInChildren<TMP_Text>().text == "Study").onClick.Invoke();
                Require(host.GetComponentsInChildren<Toggle>().Length == 1, "Room selection filters devices");
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Open device details").onClick.Invoke();
                Require(!host.GetComponentsInChildren<Button>().Single(b => b.name == "Apply light settings").interactable, "Loading reported values does not create unsaved changes");
                var brightness = host.GetComponentsInChildren<Slider>().Single(s => s.name == "Brightness");
                brightness.value = 110;
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Apply light settings").onClick.Invoke();
                await Task.Delay(500);
                Require(connection.TryGetCachedState("light.desk", out var desk) && desk.attributes.brightness == 110, "Detail slider commits confirmed brightness");
                Require(desk.attributes.color_temp_kelvin == 3500, "Brightness-only edit preserves temperature");
                await Capture(canvas, preview, Path.Combine(directory, "light-details.png"), 1440, 1080);
                CheckBounds(host);
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Back to devices").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name == "View Recent").onClick.Invoke();
                Require(host.GetComponentsInChildren<Toggle>().Length == 1, "Confirmed operation appears in recent view");
                host.GetComponentsInChildren<Button>().Single(b => b.name == "View All").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Rooms").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name.StartsWith("Room option") && b.GetComponentInChildren<TMP_Text>().text == "All rooms").onClick.Invoke();
                var next = host.GetComponentsInChildren<Button>(true).Single(button => button.name == "Next page");
                next.onClick.Invoke();
                Require(host.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Page").text.Contains("2 of 2"), "Paging changes visible rows");
                host.GetComponentsInChildren<Button>().First(b => b.name == "Open device details").onClick.Invoke();
                Require(host.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Selected function").text.StartsWith("1 / 2"), "Grouped sensor opens two functions");
                Require(host.GetComponentsInChildren<Slider>().Length == 0, "Sensors expose no light controls");
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Next function").onClick.Invoke();
                Require(host.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Selected function").text.StartsWith("2 / 2"), "Function navigation selects related entity");
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Back to devices").onClick.Invoke();
                host.GetComponentsInChildren<Button>(true).Single(button => button.name == "Filter Lights").onClick.Invoke();
                Require(host.GetComponentsInChildren<Toggle>().Length == 3, "Light category shows three switches");
                var toggle = host.GetComponentsInChildren<Toggle>().First();
                toggle.isOn = !toggle.isOn;
                Require(host.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "Updating..."), "Switch shows pending state");
                await Task.Delay(500);
                Require(!host.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "Updating..."), "Pending state settles");

                var search = host.GetComponentsInChildren<TMP_InputField>(true).Single(input => input.name == "Search devices");
                search.text = "no-match";
                Require(host.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "No matching devices."), "Empty search state");
                presenter.ShowSettings(true);
                host.GetComponentsInChildren<Button>(true).Single(button => button.name == "Edit token").onClick.Invoke();
                Require(host.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "Text entry").gameObject.activeSelf, "In-panel keyboard opens");
                var keyboardInput = host.GetComponentsInChildren<TMP_InputField>().Single(input => input.name == "Keyboard value");
                Require(keyboardInput.contentType == TMP_InputField.ContentType.Password, "Keyboard also masks credentials");
                await Capture(canvas, preview, Path.Combine(directory, "keyboard.png"), 1440, 1080);
                host.GetComponentsInChildren<Button>().Single(button => button.name == "Key 0").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(button => button.name == "Key 1").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(button => button.name == "Done").onClick.Invoke();
                Require(token.text == "12", "Keyboard appends and commits typed text");
                token.text = "";
                connection.Disconnect();
                presenter.ShowSettings(false);
                Require(!host.GetComponentsInChildren<Toggle>().Any(), "Disconnect hides stale device controls");
                await Capture(canvas, preview, Path.Combine(directory, "disconnected.png"), 1440, 1080);
                File.WriteAllText(Path.Combine(directory, "unity-ui-checks.txt"), assertions + " UI assertions passed at " + DateTime.UtcNow.ToString("O"));
                Debug.Log("[MR Home Assistant] " + assertions + " UI checks passed. Captures: " + directory);
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Debug.LogError("[MR Home Assistant] UI validation failed: " + exception);
            }
            finally
            {
                connection?.Disconnect();
                if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(preview, true);
            }
            // Exit outside the PlayerLoop continuation, after scene teardown has settled.
            if (batch) EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        private static void CheckBounds(GameObject host)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var slider in host.GetComponentsInChildren<Slider>())
            {
                var points = new Vector3[4];
                slider.handleRect.GetWorldCorners(points);
                var bounds = ((RectTransform)slider.transform).rect;
                Require(points.All(point => bounds.Contains(slider.transform.InverseTransformPoint(point))), "Slider thumb stays within target: " + slider.name);
            }
            foreach (var control in host.GetComponentsInChildren<Selectable>())
            {
                var rect = (RectTransform)control.transform;
                Require(rect.rect.width >= 44 && rect.rect.height >= 44, "Minimum target size: " + control.name);
            }
            foreach (var label in host.GetComponentsInChildren<TMP_Text>())
            {
                Require(label.rectTransform.rect.width > 0 && label.rectTransform.rect.height > 0, "Text bounds: " + label.name);
            }
        }

        private static async Task Capture(Canvas canvas, Scene preview, string path, int width, int height)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            await Task.Delay(150);
            var cameraObject = new GameObject("Preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.cameraType = CameraType.Preview;
            camera.enabled = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.384f, 0.524f / ((float)width / height));
            camera.transform.position = new Vector3(0, 0, -2);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(11, 14, 17, 255);
            camera.cullingMask = 1 << 31;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.transform.position = Vector3.zero;
            canvas.transform.rotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one * 0.001f;
            Canvas.ForceUpdateCanvases();
            var target = new RenderTexture(width, height, 24);
            var previous = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            Texture2D image = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                camera.targetTexture = target;
                Color32[] pixels = Array.Empty<Color32>();
                // Editor asset imports can invalidate Canvas meshes during initial warm-up.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    await Task.Delay(150);
                    foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
                    Canvas.ForceUpdateCanvases();
                    camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
                    camera.Render();
                    // No native readback texture survives an await / editor asset GC.
                    image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    pixels = image.GetPixels32();
                    if (pixels.Count(pixel => pixel.r > 120 && pixel.g > 120 && pixel.b > 120) > 500) break;
                    Object.DestroyImmediate(image);
                    image = null;
                }
                Require(pixels.Count(pixel => pixel.r > 120 && pixel.g > 120 && pixel.b > 120) > 500, "Nonblank rendered UI: " + Path.GetFileName(path));
                Require(pixels.Count(pixel => pixel.r > 220 && pixel.b > 220 && pixel.g < 40) < 100, "No missing-shader pixels: " + Path.GetFileName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
