using System;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    [DisallowMultipleComponent]
    public sealed class HomeAssistantHudPresenter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private HomeAssistantConnection connection;

        [SerializeField]
        private TMP_Text titleText;

        [SerializeField]
        private TMP_Text connectionPhaseText;

        [SerializeField]
        private TMP_Text stateCountText;

        [SerializeField]
        private TMP_Text lastUpdateText;

        [SerializeField]
        private Button reconnectButton;

        [Header("Options")]
        [SerializeField]
        private bool autoFindConnection = true;

        private string lastUpdateDisplay = "--";

        private void Reset()
        {
            if (autoFindConnection)
            {
                connection = FindAnyObjectByType<HomeAssistantConnection>();
            }
        }

        private void Awake()
        {
            if (connection == null && autoFindConnection)
            {
                connection = FindAnyObjectByType<HomeAssistantConnection>();
            }

            if (reconnectButton != null)
            {
                reconnectButton.onClick.RemoveListener(OnReconnectButtonPressed);
                reconnectButton.onClick.AddListener(OnReconnectButtonPressed);
            }
        }

        private void OnEnable()
        {
            if (connection != null)
            {
                connection.ConnectionStateChanged += HandleConnectionStateChanged;
                connection.StatesChanged += HandleStatesChanged;
            }

            RefreshAll();
        }

        private void OnDisable()
        {
            if (connection != null)
            {
                connection.ConnectionStateChanged -= HandleConnectionStateChanged;
                connection.StatesChanged -= HandleStatesChanged;
            }

            if (reconnectButton != null)
            {
                reconnectButton.onClick.RemoveListener(OnReconnectButtonPressed);
            }
        }

        private void HandleConnectionStateChanged()
        {
            StampLastUpdate();
            RefreshAll();
        }

        private void HandleStatesChanged(System.Collections.Generic.IReadOnlyList<HomeAssistantStateSummary> _)
        {
            StampLastUpdate();
            RefreshAll();
        }

        private async void OnReconnectButtonPressed()
        {
            if (connection == null)
            {
                Debug.LogWarning("[HomeAssistantHudPresenter] Reconnect pressed but HomeAssistantConnection is missing.", this);
                return;
            }

            connection.EvaluateConfiguration();

            if (!connection.HasValidConfiguration)
            {
                StampLastUpdate();
                RefreshAll();
                return;
            }

            await connection.ConnectAndInitializeAsync();

            StampLastUpdate();
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (titleText != null)
            {
                titleText.text = "MR Home Assistant";
            }

            if (connection == null)
            {
                if (connectionPhaseText != null)
                {
                    connectionPhaseText.text = "Connection phase: <missing connection>";
                }

                if (stateCountText != null)
                {
                    stateCountText.text = "States count: 0";
                }

                if (lastUpdateText != null)
                {
                    lastUpdateText.text = "Last update: --";
                }

                return;
            }

            if (connectionPhaseText != null)
            {
                string phaseLine = $"Connection phase: {connection.ConnectionPhase}";
                if (!string.IsNullOrWhiteSpace(connection.LastError))
                {
                    phaseLine += $" | Error: {connection.LastError}";
                }

                connectionPhaseText.text = phaseLine;
            }

            if (stateCountText != null)
            {
                int count = connection.States != null ? connection.States.Count : 0;
                stateCountText.text = $"States count: {count}";
            }

            if (lastUpdateText != null)
            {
                lastUpdateText.text = $"Last update: {lastUpdateDisplay}";
            }
        }

        private void StampLastUpdate()
        {
            lastUpdateDisplay = DateTime.Now.ToString("HH:mm:ss");
        }
    }
}