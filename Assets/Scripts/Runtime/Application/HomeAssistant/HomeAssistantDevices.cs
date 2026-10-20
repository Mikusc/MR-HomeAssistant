using System;
using System.Collections.Generic;
using System.Linq;

namespace MRHomeAssistant.Runtime.Application.HomeAssistant
{
    public static class HomeAssistantDevices
    {
        public static string Domain(HomeAssistantStateSummary item) => (item?.entity_id ?? "").Split('.')[0];
        public static string Name(HomeAssistantStateSummary item) => string.IsNullOrWhiteSpace(item.attributes?.friendly_name)
            ? item.entity_id : item.attributes.friendly_name;
        public static bool Available(HomeAssistantStateSummary item) => item != null && item.state != "unavailable" && item.state != "unknown" && !string.IsNullOrWhiteSpace(item.state);
        public static bool CanControl(HomeAssistantStateSummary item) => Available(item) &&
            (Domain(item) == "light" || Domain(item) == "switch" || Domain(item) == "input_boolean") && (item.state == "on" || item.state == "off");

        public static bool InCategory(HomeAssistantStateSummary item, string category) => category == "All" ||
            (category == "Lights" && Domain(item) == "light") ||
            (category == "Switches" && (Domain(item) == "switch" || Domain(item) == "input_boolean")) ||
            (category == "Sensors" && (Domain(item) == "sensor" || Domain(item) == "binary_sensor"));

        public static bool SupportsBrightness(HomeAssistantStateSummary item) => Domain(item) == "light" &&
            (item.attributes?.supported_color_modes ?? Array.Empty<string>()).Any(mode =>
                new[] { "brightness", "color_temp", "hs", "xy", "rgb", "rgbw", "rgbww", "white" }.Contains(mode));

        public static bool SupportsTemperature(HomeAssistantStateSummary item) => Domain(item) == "light" &&
            (item.attributes?.supported_color_modes ?? Array.Empty<string>()).Contains("color_temp") &&
            item.attributes.min_color_temp_kelvin > 0 && item.attributes.max_color_temp_kelvin > item.attributes.min_color_temp_kelvin;

        public static List<HomeAssistantStateSummary> Filter(IEnumerable<HomeAssistantStateSummary> items, string category, string query)
        {
            return items.Where(item => item != null && !string.IsNullOrEmpty(item.entity_id))
                .Where(item => InCategory(item, category))
                .Where(item => string.IsNullOrWhiteSpace(query) || Name(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.entity_id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.entity_id, StringComparer.Ordinal).ToList();
        }

        public static string StateLabel(HomeAssistantStateSummary item)
        {
            if (!Available(item)) return item?.state == "unavailable" ? "Unavailable" : "Unknown";
            string unit = item.attributes?.unit_of_measurement;
            if (!string.IsNullOrEmpty(unit)) return item.state + " " + unit;
            if (Domain(item) == "binary_sensor")
            {
                bool active = item.state == "on";
                switch (item.attributes?.device_class)
                {
                    case "door": case "window": case "opening": return active ? "Open" : "Closed";
                    case "motion": case "occupancy": case "presence": return active ? "Detected" : "Clear";
                    case "connectivity": return active ? "Connected" : "Disconnected";
                }
            }
            return char.ToUpperInvariant(item.state[0]) + item.state.Substring(1);
        }

        public static HomeAssistantStateSummary[] DemoStates() => new[]
        {
            Demo("light.living_room", "Living room", "on"), Demo("light.desk", "Desk lamp / \u684c\u706f", "off"),
            Demo("light.bedroom", "Bedroom", "on"), Demo("switch.floor_fan", "Floor fan", "off"),
            Demo("switch.coffee", "Coffee machine", "unavailable"), Demo("sensor.temperature", "Room temperature", "23.4", "C"),
            Demo("sensor.humidity", "Humidity", "48", "%"), Demo("binary_sensor.window", "Window", "off", "", "window")
        };

        private static HomeAssistantStateSummary Demo(string id, string name, string state, string unit = "", string deviceClass = "") =>
            new HomeAssistantStateSummary { entity_id = id, state = state, last_updated = DateTime.UtcNow.ToString("O"),
                attributes = new HomeAssistantStateAttributes { friendly_name = name, unit_of_measurement = unit, device_class = deviceClass,
                    supported_color_modes = id == "light.desk" || id == "light.living_room" ? new[] { "color_temp" } : Array.Empty<string>(),
                    brightness = id.StartsWith("light.") ? 180 : 0, color_temp_kelvin = 3500,
                    min_color_temp_kelvin = 2200, max_color_temp_kelvin = 6500, color_mode = "color_temp" } };
    }
}
