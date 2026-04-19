using System;

namespace MRHomeAssistant.Runtime.Application.HomeAssistant
{
    public enum HomeAssistantConnectionPhase
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3
    }

    [Serializable]
    internal sealed class HomeAssistantMessageEnvelope
    {
        public string type = string.Empty;
        public int id;
        public bool success;
        public string message = string.Empty;
        public string ha_version = string.Empty;
    }

    [Serializable]
    internal sealed class HomeAssistantAuthRequest
    {
        public string type = "auth";
        public string access_token = string.Empty;
    }

    [Serializable]
    internal sealed class HomeAssistantGetStatesRequest
    {
        public int id;
        public string type = "get_states";
    }

    [Serializable]
    internal sealed class HomeAssistantGetStatesResponse
    {
        public int id;
        public string type = string.Empty;
        public bool success;
        public HomeAssistantStateSummary[] result = Array.Empty<HomeAssistantStateSummary>();
    }

    [Serializable]
    public sealed class HomeAssistantStateSummary
    {
        public string entity_id = string.Empty;
        public string state = string.Empty;
        public string last_changed = string.Empty;
        public string last_updated = string.Empty;
        public HomeAssistantStateAttributes attributes = new HomeAssistantStateAttributes();
    }

    [Serializable]
    public sealed class HomeAssistantStateAttributes
    {
        public string friendly_name = string.Empty;
        public string unit_of_measurement = string.Empty;
        public string device_class = string.Empty;
        public string state_class = string.Empty;
        public string icon = string.Empty;
        public float min;
        public float max;
        public float step;
        public string duration = string.Empty;
        public string finishes_at = string.Empty;
    }

    [Serializable]
    internal sealed class HomeAssistantSubscribeEventsRequest
    {
        public int id;
        public string type = "subscribe_events";
        public string event_type = "state_changed";
    }

    [Serializable]
    internal sealed class HomeAssistantImmersiveHomeRegisterRequest
    {
        public int id;
        public string type = "immersive_home/register";
        public string device_id = string.Empty;
        public string name = string.Empty;
        public string version = string.Empty;
        public string platform = string.Empty;
    }

    [Serializable]
    internal sealed class HomeAssistantImmersiveHomeUpdateRequest
    {
        public int id;
        public string type = "immersive_home/update";
        public string device_id = string.Empty;
        public string room = string.Empty;
    }

    [Serializable]
    internal sealed class HomeAssistantEventEnvelope
    {
        public int id;
        public string type = string.Empty;
        public HomeAssistantEventPayload @event = new HomeAssistantEventPayload();
    }

    [Serializable]
    internal sealed class HomeAssistantEventPayload
    {
        public string event_type = string.Empty;
        public HomeAssistantStateChangedData data = new HomeAssistantStateChangedData();
    }

    [Serializable]
    internal sealed class HomeAssistantStateChangedData
    {
        public string entity_id = string.Empty;
        public HomeAssistantStateSummary old_state;
        public HomeAssistantStateSummary new_state;
    }
}