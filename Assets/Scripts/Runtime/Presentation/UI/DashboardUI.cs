using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    // Fixed reference dimensions keep ray targets stable as labels and connection states change.
    internal static class DashboardUI
    {
        public const float Width = 1000;
        public const float Height = 720;
        public static readonly Color Background = new Color32(22, 25, 29, 255);
        public static readonly Color Surface = new Color32(37, 41, 46, 255);
        public static readonly Color Border = new Color32(67, 73, 80, 255);
        public static readonly Color Text = new Color32(244, 246, 247, 255);
        public static readonly Color Muted = new Color32(177, 186, 194, 255);
        public static readonly Color Accent = new Color32(112, 231, 187, 255);
        public static readonly Color Warning = new Color32(255, 202, 119, 255);
        public static readonly Color Error = new Color32(255, 157, 157, 255);

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.gameObject.layer = 5;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static Image Fill(RectTransform rt, Color color, bool raycast = false)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h,
            float size = 22, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            DashboardFonts.EnsureFallback();
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            // UISet may update the font at Start; preserve this dashboard's semantic colors.
            text.gameObject.tag = "QDSUISharedThemeColor";
            text.font = TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = size;
            text.color = color ?? Text;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            text.characterSpacing = 0;
            return text;
        }

        public static Button Button(Transform parent, string name, string label, float x, float y, float w, float h,
            UnityAction onClick, bool primary = false)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var image = Fill(rt, primary ? Accent : Surface, true);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 0.92f, 0.91f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.62f, 0.76f, 0.74f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            Label(rt, "Label", label, 12, 0, w - 24, h, 21, primary ? Background : Text, TextAlignmentOptions.Center);
            return button;
        }

        public static TMP_InputField Input(Transform parent, string name, string placeholder, float x, float y, float w, bool password = false)
        {
            var rt = Rect(parent, name, x, y, w, 54);
            var image = Fill(rt, Surface, true);
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = image;
            var viewport = Rect(rt, "Text viewport", 16, 4, w - 32, 46);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, "Value", "", 0, 0, w - 32, 46, 21);
            var hint = Label(viewport, "Placeholder", placeholder, 0, 0, w - 32, 46, 21, Muted);
            field.textViewport = viewport;
            field.textComponent = (TextMeshProUGUI)text;
            field.placeholder = hint;
            field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            field.characterLimit = password ? 4096 : 256;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.resetOnDeActivation = false;
            field.shouldHideSoftKeyboard = true;
            field.customCaretColor = true;
            field.caretColor = Accent;
            field.selectionColor = new Color(0.3f, 0.7f, 0.6f, 0.4f);
            return field;
        }

        public static Slider Slider(Transform parent, string name, float x, float y, float width, UnityAction<float> changed)
        {
            var rt = Rect(parent, name, x, y, width, 48);
            Fill(rt, Surface, true);
            var slider = rt.gameObject.AddComponent<Slider>();
            Fill(Rect(rt, "Track", 22, 20, width - 44, 8), Border);
            var area = Rect(rt, "Handle area", 22, 0, width - 44, 48);
            var handle = Rect(area, "Handle", 0, 2, 44, 44);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.up;
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(44, -4);
            handle.anchoredPosition = Vector2.zero;
            var image = Fill(handle, Accent, true);
            slider.handleRect = handle;
            slider.targetGraphic = image;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(changed);
            return slider;
        }

        public static Toggle Power(Transform parent, float x, float y, UnityAction<bool> changed)
        {
            var root = Rect(parent, "Power switch", x, y, 96, 52);
            var track = Fill(root, Border, true);
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.transition = Selectable.Transition.ColorTint;
            var onGraphic = Fill(Rect(root, "On fill", 0, 0, 96, 52), Accent);
            toggle.graphic = onGraphic;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            var thumb = Fill(Rect(root, "Thumb", 8, 8, 36, 36), Text);
            toggle.onValueChanged.AddListener(value => thumb.rectTransform.anchoredPosition = new Vector2(value ? 52 : 8, -8));
            toggle.onValueChanged.AddListener(changed);
            return toggle;
        }
    }
}
