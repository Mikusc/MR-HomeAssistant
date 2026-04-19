using System;

namespace MRHomeAssistant.Runtime.Application.Settings
{
    [Serializable]
    public sealed class AppSettingsData
    {
        public string homeAssistantUrl = "";
        public string homeAssistantToken = "";
        public bool onboardingCompleted = false;
    }
}