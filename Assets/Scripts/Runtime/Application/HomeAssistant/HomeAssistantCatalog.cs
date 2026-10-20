using System;
using System.Collections.Generic;
using System.Linq;

namespace MRHomeAssistant.Runtime.Application.HomeAssistant
{
    public sealed class HomeAssistantDeviceGroup
    {
        public string Id;
        public string Name;
        public string AreaId;
        public string AreaName;
        public List<HomeAssistantStateSummary> Entities = new();
        public HomeAssistantStateSummary Primary => Entities[0];
    }

    // Room inheritance is resolved from HA registries, never guessed from entity names.
    public sealed class HomeAssistantCatalog
    {
        private readonly Dictionary<string, HomeAssistantArea> areas = new();
        private readonly Dictionary<string, HomeAssistantDevice> devices = new();
        private readonly Dictionary<string, HomeAssistantEntity> entities = new();
        public IReadOnlyCollection<HomeAssistantArea> Areas => areas.Values;
        public bool IsLoaded { get; private set; }

        public void Load(HomeAssistantArea[] areaList, HomeAssistantDevice[] deviceList, HomeAssistantEntity[] entityList)
        {
            Clear();
            foreach (var area in areaList ?? Array.Empty<HomeAssistantArea>())
                if (!string.IsNullOrEmpty(area?.area_id)) areas[area.area_id] = area;
            foreach (var device in deviceList ?? Array.Empty<HomeAssistantDevice>())
                if (!string.IsNullOrEmpty(device?.id)) devices[device.id] = device;
            foreach (var entity in entityList ?? Array.Empty<HomeAssistantEntity>())
                if (!string.IsNullOrEmpty(entity?.entity_id)) entities[entity.entity_id] = entity;
            IsLoaded = true;
        }

        public void Clear() { areas.Clear(); devices.Clear(); entities.Clear(); IsLoaded = false; }

        public List<HomeAssistantDeviceGroup> Group(IEnumerable<HomeAssistantStateSummary> states, string category = "All", string query = "", string areaId = "")
        {
            var groups = new Dictionary<string, HomeAssistantDeviceGroup>();
            foreach (var state in states)
            {
                if (state == null || string.IsNullOrEmpty(state.entity_id)) continue;
                entities.TryGetValue(state.entity_id, out var entity);
                if (!string.IsNullOrEmpty(entity?.disabled_by) || !string.IsNullOrEmpty(entity?.hidden_by)) continue;
                HomeAssistantDevice device = null;
                if (!string.IsNullOrEmpty(entity?.device_id)) devices.TryGetValue(entity.device_id, out device);
                string room = !string.IsNullOrEmpty(entity?.area_id) ? entity.area_id : device?.area_id ?? "";
                // An entity override can put functions of the same device in different rooms.
                string id = device == null ? "entity:" + state.entity_id : "device:" + device.id + ":" + room;
                if (!groups.TryGetValue(id, out var group))
                {
                    areas.TryGetValue(room, out var area);
                    group = new HomeAssistantDeviceGroup { Id = id, AreaId = room,
                        AreaName = area?.name ?? "Unassigned",
                        Name = !string.IsNullOrWhiteSpace(device?.name_by_user) ? device.name_by_user :
                            !string.IsNullOrWhiteSpace(device?.name) ? device.name : HomeAssistantDevices.Name(state) };
                    groups[id] = group;
                }
                group.Entities.Add(state);
            }
            foreach (var group in groups.Values)
                group.Entities = group.Entities.OrderBy(s => HomeAssistantDevices.Domain(s) == "light" ? 0 :
                    HomeAssistantDevices.Domain(s) == "switch" || HomeAssistantDevices.Domain(s) == "input_boolean" ? 1 : 2)
                    .ThenBy(HomeAssistantDevices.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return groups.Values.Where(g => string.IsNullOrEmpty(areaId) || (areaId == "__unassigned" ? g.AreaId == "" : g.AreaId == areaId))
                .Where(g => g.Entities.Any(s => HomeAssistantDevices.InCategory(s, category)))
                .Where(g => string.IsNullOrWhiteSpace(query) || Contains(g.Name, query) || Contains(g.AreaName, query) ||
                    g.Entities.Any(s => Contains(HomeAssistantDevices.Name(s), query) || Contains(s.entity_id, query)))
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
        }

        private static bool Contains(string text, string query) => (text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        public void LoadDemo()
        {
            Load(new[] { new HomeAssistantArea { area_id = "living", name = "Living room" },
                    new HomeAssistantArea { area_id = "study", name = "Study" }, new HomeAssistantArea { area_id = "bedroom", name = "Bedroom" } },
                new[] { new HomeAssistantDevice { id = "climate", name = "Room climate", area_id = "living" } },
                new[] { new HomeAssistantEntity { entity_id = "light.desk", area_id = "study" },
                    new HomeAssistantEntity { entity_id = "light.living_room", area_id = "living" },
                    new HomeAssistantEntity { entity_id = "light.bedroom", area_id = "bedroom" },
                    new HomeAssistantEntity { entity_id = "sensor.temperature", device_id = "climate" },
                    new HomeAssistantEntity { entity_id = "sensor.humidity", device_id = "climate" } });
        }
    }
}
