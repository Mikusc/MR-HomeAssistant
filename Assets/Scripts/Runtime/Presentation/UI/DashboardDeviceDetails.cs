using System;
using System.Linq;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    internal sealed class DashboardDeviceDetails
    {
        private readonly HomeAssistantConnection connection;
        private readonly Action<string, bool> notice;
        private readonly Action<string> used;
        private readonly RectTransform root;
        private readonly TMP_Text title, location, entityLabel, state, brightnessLabel, temperatureLabel, updated;
        private readonly Button previous, next, power, apply;
        private readonly Slider brightness, temperature;
        private HomeAssistantDeviceGroup group;
        private int entityIndex;
        private bool brightnessDirty, temperatureDirty;
        private bool loadingValues;
        private bool Dirty => brightnessDirty || temperatureDirty;
        public bool IsOpen => group != null;

        public DashboardDeviceDetails(Transform parent, HomeAssistantConnection source, Action close,
            Action<string, bool> showNotice, Action<string> recordUse)
        {
            connection = source; notice = showNotice; used = recordUse;
            root = DashboardUI.Rect(parent, "Device details", 32, 150, 936, 504);
            DashboardUI.Button(root, "Back to devices", "<", 0, 0, 64, 48, () => close());
            title = DashboardUI.Label(root, "Detail title", "", 80, 0, 856, 48, 28);
            location = DashboardUI.Label(root, "Detail room", "", 0, 56, 936, 32, 20, DashboardUI.Muted);
            previous = DashboardUI.Button(root, "Previous function", "<", 0, 100, 64, 48, () => Select(-1));
            entityLabel = DashboardUI.Label(root, "Selected function", "", 80, 100, 776, 48, 21);
            next = DashboardUI.Button(root, "Next function", ">", 872, 100, 64, 48, () => Select(1));
            state = DashboardUI.Label(root, "Confirmed state", "", 0, 162, 620, 48, 24);
            power = DashboardUI.Button(root, "Detail power", "", 640, 162, 296, 48, TogglePower);
            brightnessLabel = DashboardUI.Label(root, "Brightness label", "", 0, 228, 296, 48, 21);
            brightness = DashboardUI.Slider(root, "Brightness", 312, 228, 624, _ => { if (!loadingValues) { brightnessDirty = true; Refresh(); } });
            temperatureLabel = DashboardUI.Label(root, "Temperature label", "", 0, 294, 296, 48, 21);
            temperature = DashboardUI.Slider(root, "Color temperature", 312, 294, 624, _ => { if (!loadingValues) { temperatureDirty = true; Refresh(); } });
            updated = DashboardUI.Label(root, "Detail update", "", 0, 364, 936, 62, 18, DashboardUI.Muted);
            updated.textWrappingMode = TextWrappingModes.Normal;
            apply = DashboardUI.Button(root, "Apply light settings", "Apply", 640, 448, 296, 54, Apply, true);
            root.gameObject.SetActive(false);
        }

        public void Open(HomeAssistantDeviceGroup value) { group = value; entityIndex = 0; brightnessDirty = temperatureDirty = false; Refresh(); }
        public void Close() { group = null; root.gameObject.SetActive(false); }
        public void SetVisible(bool visible) => root.gameObject.SetActive(visible && IsOpen);
        private HomeAssistantStateSummary Current => group == null ? null :
            connection.States.FirstOrDefault(s => s.entity_id == group.Entities[entityIndex].entity_id);
        private void Select(int direction) { entityIndex += direction; brightnessDirty = temperatureDirty = false; Refresh(); }

        public void Refresh()
        {
            if (group == null) return;
            var item = Current;
            title.text = group.Name;
            location.text = group.AreaName;
            entityLabel.text = (entityIndex + 1) + " / " + group.Entities.Count + "   " +
                (item == null ? "Entity removed" : HomeAssistantDevices.Name(item) + "  /  " + item.entity_id);
            previous.interactable = entityIndex > 0;
            next.interactable = entityIndex + 1 < group.Entities.Count;
            bool live = connection.IsConnected || connection.IsDemo;
            bool editable = live && HomeAssistantDevices.CanControl(item) && !connection.IsPending(item?.entity_id);
            bool light = HomeAssistantDevices.SupportsBrightness(item);
            bool color = HomeAssistantDevices.SupportsTemperature(item);
            if (!Dirty && item != null)
            {
                loadingValues = true;
                brightness.minValue = 1; brightness.maxValue = 255;
                brightness.SetValueWithoutNotify(Mathf.Clamp(item.attributes.brightness, 1, 255));
                temperature.minValue = color ? item.attributes.min_color_temp_kelvin : 1000;
                temperature.maxValue = color ? item.attributes.max_color_temp_kelvin : 10000;
                temperature.SetValueWithoutNotify(item.attributes.color_temp_kelvin);
                loadingValues = false;
            }
            state.text = !live ? "Cached / Read only" : item == null ? "Unavailable" :
                connection.IsPending(item.entity_id) ? "Updating..." : HomeAssistantDevices.StateLabel(item);
            state.color = !live ? DashboardUI.Warning : DashboardUI.Text;
            power.gameObject.SetActive(HomeAssistantDevices.Domain(item) is "light" or "switch" or "input_boolean");
            power.interactable = editable;
            power.GetComponentInChildren<TMP_Text>().text = item?.state == "on" ? "Turn off" : "Turn on";
            brightness.gameObject.SetActive(light); brightnessLabel.gameObject.SetActive(light);
            temperature.gameObject.SetActive(color); temperatureLabel.gameObject.SetActive(color);
            brightness.interactable = temperature.interactable = editable;
            brightnessLabel.text = "Brightness  " + Mathf.RoundToInt(brightness.value / 255f * 100) + "%";
            temperatureLabel.text = "Temperature  " + Mathf.RoundToInt(temperature.value) + " K";
            apply.gameObject.SetActive(light || color);
            apply.interactable = Dirty && editable;
            updated.text = item == null ? "This entity is no longer in the current snapshot." :
                Dirty ? "Unsaved changes" : "Reported: " + HomeAssistantDevices.StateLabel(item) +
                (light ? " / " + Mathf.RoundToInt(item.attributes.brightness / 255f * 100) + "%" : "") +
                (color ? " / " + item.attributes.color_temp_kelvin + " K" : "");
        }

        private async void TogglePower()
        {
            var item = Current;
            if (item == null) return;
            string groupId = group.Id;
            string profile = connection.IsDemo ? "demo" : connection.CurrentWebSocketUrl;
            string error = await connection.SetPowerAsync(item.entity_id, item.state != "on");
            Complete(groupId, item.entity_id, profile, error, false);
        }

        private async void Apply()
        {
            var item = Current;
            if (item == null) return;
            string groupId = group.Id;
            string profile = connection.IsDemo ? "demo" : connection.CurrentWebSocketUrl;
            string error = await connection.SetLightAsync(item.entity_id,
                brightnessDirty && HomeAssistantDevices.SupportsBrightness(item) ? Mathf.RoundToInt(brightness.value) : (int?)null,
                temperatureDirty && HomeAssistantDevices.SupportsTemperature(item) ? Mathf.RoundToInt(temperature.value) : (int?)null);
            Complete(groupId, item.entity_id, profile, error, true);
        }

        private void Complete(string groupId, string entityId, string profile, string error, bool clearDraft)
        {
            if (root == null) return;
            if (profile != (connection.IsDemo ? "demo" : connection.CurrentWebSocketUrl)) return;
            if (string.IsNullOrEmpty(error))
            {
                used(groupId);
                if (clearDraft && group?.Id == groupId && Current?.entity_id == entityId) brightnessDirty = temperatureDirty = false;
            }
            notice(string.IsNullOrEmpty(error) ? "Device state confirmed." : error, !string.IsNullOrEmpty(error));
            Refresh();
        }
    }
}
