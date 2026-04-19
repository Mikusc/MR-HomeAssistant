using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRHomeAssistant.Runtime.Platform.Scene
{
    [DisallowMultipleComponent]
    public sealed class MrukRoomDebugReporter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private MRUK mruk;

        [Header("Options")]
        [SerializeField]
        private bool autoFindMruk = true;

        [SerializeField]
        private bool logEachRoomWhenSceneLoads = true;

        [SerializeField]
        private bool logAnchorLabels = false;

        private void Reset()
        {
            if (autoFindMruk)
            {
                mruk = FindAnyObjectByType<MRUK>();
            }
        }

        private void Awake()
        {
            if (mruk == null && autoFindMruk)
            {
                mruk = FindAnyObjectByType<MRUK>();
            }
        }

        private void OnEnable()
        {
            if (mruk == null)
            {
                Debug.LogWarning("[MrukRoomDebugReporter] MRUK reference is missing.", this);
                return;
            }

            mruk.RegisterSceneLoadedCallback(HandleSceneLoaded);
            mruk.RoomCreatedEvent.AddListener(HandleRoomCreated);
            mruk.RoomUpdatedEvent.AddListener(HandleRoomUpdated);
            mruk.RoomRemovedEvent.AddListener(HandleRoomRemoved);

            LogSceneSummary("OnEnable");
        }

        private void OnDisable()
        {
            if (mruk == null)
            {
                return;
            }

            mruk.SceneLoadedEvent.RemoveListener(HandleSceneLoaded);
            mruk.RoomCreatedEvent.RemoveListener(HandleRoomCreated);
            mruk.RoomUpdatedEvent.RemoveListener(HandleRoomUpdated);
            mruk.RoomRemovedEvent.RemoveListener(HandleRoomRemoved);
        }

        [ContextMenu("Log Current MRUK State")]
        private void LogCurrentState()
        {
            if (mruk == null)
            {
                Debug.LogWarning("[MrukRoomDebugReporter] Cannot log MRUK state because the reference is missing.", this);
                return;
            }

            LogSceneSummary("Manual");
        }

        private void HandleSceneLoaded()
        {
            LogSceneSummary("SceneLoaded");

            if (!logEachRoomWhenSceneLoads || mruk.Rooms == null)
            {
                return;
            }

            foreach (MRUKRoom room in mruk.Rooms)
            {
                LogRoomSummary("SceneLoaded", room);
            }
        }

        private void HandleRoomCreated(MRUKRoom room)
        {
            LogRoomSummary("RoomCreated", room);
        }

        private void HandleRoomUpdated(MRUKRoom room)
        {
            LogRoomSummary("RoomUpdated", room);
        }

        private void HandleRoomRemoved(MRUKRoom room)
        {
            LogRoomSummary("RoomRemoved", room);
        }

        private void LogSceneSummary(string source)
        {
            int roomCount = mruk.Rooms != null ? mruk.Rooms.Count : 0;
            MRUKRoom currentRoom = null;

            if (mruk.IsInitialized)
            {
                currentRoom = mruk.GetCurrentRoom();
            }

            Debug.Log(
                $"[MrukRoomDebugReporter] {source} | DataSource={mruk.SceneSettings.DataSource} | Initialized={mruk.IsInitialized} | Rooms={roomCount} | CurrentRoom={DescribeRoom(currentRoom)}",
                this);
        }

        private void LogRoomSummary(string source, MRUKRoom room)
        {
            if (room == null)
            {
                Debug.LogWarning($"[MrukRoomDebugReporter] {source} | Room reference is null.", this);
                return;
            }

            int anchorCount = room.Anchors != null ? room.Anchors.Count : 0;
            int wallCount = room.WallAnchors != null ? room.WallAnchors.Count : 0;
            int floorCount = room.FloorAnchors != null ? room.FloorAnchors.Count : 0;
            int ceilingCount = room.CeilingAnchors != null ? room.CeilingAnchors.Count : 0;

            Debug.Log(
                $"[MrukRoomDebugReporter] {source} | Room={DescribeRoom(room)} | Local={room.IsLocal} | Anchors={anchorCount} | Walls={wallCount} | Floors={floorCount} | Ceilings={ceilingCount}",
                room);

            if (!logAnchorLabels || room.Anchors == null || room.Anchors.Count == 0)
            {
                return;
            }

            for (int i = 0; i < room.Anchors.Count; i++)
            {
                MRUKAnchor anchor = room.Anchors[i];
                Debug.Log(
                    $"[MrukRoomDebugReporter] Anchor[{i}] | Room={DescribeRoom(room)} | Name={anchor.name} | Label={anchor.Label}",
                    anchor);
            }
        }

        private static string DescribeRoom(MRUKRoom room)
        {
            return room == null ? "<none>" : room.name;
        }
    }
}
