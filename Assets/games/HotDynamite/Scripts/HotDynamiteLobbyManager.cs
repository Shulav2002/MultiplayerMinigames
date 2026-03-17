using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using HotDynamite.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HotDynamite.Lobby
{
    public class HotDynamiteLobbyManager : MonoBehaviour
    {
        private enum RoomPanelMode
        {
            Hidden = 0,
            JoinEntry = 1,
            PrivateWaitingHost = 2,
            PrivateWaitingGuest = 3
        }

        [Header("Scene Loading")]
        [Tooltip("The name of the scene to load. E.g., 'HotDynamite'")]
        [SerializeField] private string gameSceneName = "HotDynamite";

        [Header("UI & Game References")]
        [Tooltip("The Lobby UI canvas to turn off when the game starts.")]
        [SerializeField] private GameObject lobbyCanvas;

        [Tooltip("The parent GameObject containing the game elements (GameManager, Level, etc) to turn on.")]
        [SerializeField] private GameObject gameContainer;

        [Tooltip("Panel reused for join entry and private-room waiting.")]
        [SerializeField] private GameObject joinRoomPopupPanel;

        [Tooltip("Input or text component used to enter and display room ids.")]
        [SerializeField] private Component roomIdInputField;

        [Tooltip("Primary popup action label component.")]
        [SerializeField] private Component roomPopupActionLabel;

        [Tooltip("Primary popup action button component.")]
        [SerializeField] private Component roomPopupPrimaryButton;

        [Header("Room Popup Labels")]
        [SerializeField] private string joinRoomConfirmLabel = "Join";
        [SerializeField] private string hostStartLabel = "Start";
        [SerializeField] private string guestWaitingLabel = "Waiting...";

        [Header("Game Manager Reference")]
        [SerializeField] private HotDynamiteGameManager gameManager;

        [Header("Networking")]
        [SerializeField] private bool useNetworkLobby = true;
        [SerializeField] private HotDynamiteLobbyNetworkClient networkClient;
        [SerializeField] private HotDynamiteOnlineMatchController onlineMatchController;
        [SerializeField] private bool startLocalGameWhenRoomReady = false;

        [Header("Offline With Bots")]
        [SerializeField, Min(2)] private int offlineTotalPlayers = 4;
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;

        private bool joinRequestInFlight;
        private RoomPanelMode roomPanelMode;
        private int currentOccupiedSeats;
        private int currentCapacity;

        private void Start()
        {
            EnsureNetworkClient();
            EnsureOnlineMatchController();
            EnsureRoomUiReferences();

            if (useNetworkLobby && (networkClient == null || onlineMatchController == null))
            {
                Debug.LogError("HotDynamiteLobbyManager: Network components are missing. Add them in edit mode before entering play mode.");
                return;
            }

            SubscribeToNetworkEvents();
            InitializeOnlineMatchController();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(false);
            }

            HideRoomPanel();
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkEvents();
        }

        public async void OnPlayButtonClicked()
        {
            if (!useNetworkLobby)
            {
                StartGameLocalFlow();
                return;
            }

            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        public void OnPlayWithBotsButtonClicked()
        {
            int totalPlayers = Mathf.Max(2, offlineTotalPlayers);
            int humanPlayers = Mathf.Clamp(offlineHumanPlayers, 1, totalPlayers);
            StartGameLocalFlow(totalPlayers, humanPlayers);
        }

        public void OnOpenJoinRoomPopupClicked()
        {
            EnsureRoomUiReferences();
            ShowJoinEntryPanel();
        }

        public async void OnOpenCreateRoomPopupClicked()
        {
            if (!useNetworkLobby)
            {
                return;
            }

            string roomId = GenerateRoomId();
            await BeginOnlineJoinAsync(roomId, createRoom: true, usePrivateRoom: true);
        }

        public void OnRoomPopupConfirmButtonClicked()
        {
            switch (roomPanelMode)
            {
                case RoomPanelMode.JoinEntry:
                    SubmitJoinRoomRequest();
                    break;
                case RoomPanelMode.PrivateWaitingHost:
                    StartHostedPrivateMatch();
                    break;
            }
        }

        public void OnCloseJoinRoomPopupClicked()
        {
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                HideRoomPanel();
            }
        }

        public void LoadGameScene()
        {
            Debug.Log($"Loading game scene: {gameSceneName}");
            SceneManager.LoadScene(gameSceneName);
        }

        private async void SubmitJoinRoomRequest()
        {
            string roomId = GetTrimmedRoomId();
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("HotDynamiteLobbyManager: Room id is required to join a private room.");
                return;
            }

            await BeginOnlineJoinAsync(roomId, createRoom: false, usePrivateRoom: true);
        }

        private async System.Threading.Tasks.Task BeginOnlineJoinAsync(string roomId, bool createRoom, bool usePrivateRoom)
        {
            EnsureNetworkClient();
            if (networkClient == null)
            {
                Debug.LogError("HotDynamiteLobbyManager: Network client is missing. Add it in edit mode before entering play mode.");
                return;
            }

            if (joinRequestInFlight)
            {
                return;
            }

            joinRequestInFlight = true;

            try
            {
                if (!networkClient.IsConnected)
                {
                    Debug.Log($"HotDynamiteLobbyManager: Connecting to {networkClient.ServerUrl}...");
                    await networkClient.ConnectAsync();
                }

                if (networkClient.IsInRoom)
                {
                    Debug.Log($"HotDynamiteLobbyManager: Already in room `{networkClient.CurrentRoomId}`.");
                    joinRequestInFlight = false;
                    return;
                }

                if (usePrivateRoom)
                {
                    string action = createRoom ? "Creating" : "Joining";
                    Debug.Log($"HotDynamiteLobbyManager: {action} private room `{roomId}` for game `{networkClient.GameId}`.");
                    await networkClient.JoinPrivateAsync(roomId, createRoom);
                }
                else
                {
                    Debug.Log($"HotDynamiteLobbyManager: Joining random room for game `{networkClient.GameId}` (capacity {networkClient.RoomCapacity})...");
                    await networkClient.JoinAsync();
                }
            }
            catch (Exception exception)
            {
                joinRequestInFlight = false;
                Debug.LogError($"HotDynamiteLobbyManager: Failed to connect or join backend room. {exception.Message}");
            }
        }

        private void StartHostedPrivateMatch()
        {
            if (onlineMatchController == null)
            {
                return;
            }

            if (!onlineMatchController.CanHostStartMatch)
            {
                Debug.Log("HotDynamiteLobbyManager: Waiting for another player before starting the room.");
                return;
            }

            HideRoomPanel();
            onlineMatchController.StartHostedMatchNow();
        }

        private void EnsureNetworkClient()
        {
            if (!useNetworkLobby || networkClient != null)
            {
                return;
            }

            networkClient = GetComponent<HotDynamiteLobbyNetworkClient>();
        }

        private void EnsureOnlineMatchController()
        {
            if (!useNetworkLobby || onlineMatchController != null)
            {
                return;
            }

            onlineMatchController = GetComponent<HotDynamiteOnlineMatchController>();
        }

        private void EnsureRoomUiReferences()
        {
            if (joinRoomPopupPanel == null)
            {
                Transform popupPanel = FindInActiveScene("JoinPopUpPanel");
                if (popupPanel != null)
                {
                    joinRoomPopupPanel = popupPanel.gameObject;
                }
            }

            if (roomIdInputField == null)
            {
                Transform inputTransform = FindInActiveScene("RoomIdInputField");
                if (inputTransform != null)
                {
                    roomIdInputField = FindComponentByTypeName(inputTransform.gameObject, "TMP_InputField");
                }
            }

            if (roomPopupPrimaryButton == null && joinRoomPopupPanel != null)
            {
                Transform popupPrimaryButtonTransform = FindInChildren(joinRoomPopupPanel.transform, "Join");
                if (popupPrimaryButtonTransform != null)
                {
                    roomPopupPrimaryButton = FindComponentByTypeName(popupPrimaryButtonTransform.gameObject, "Button");
                }
            }

            if (roomPopupActionLabel == null && joinRoomPopupPanel != null)
            {
                Transform popupPrimaryButtonTransform = FindInChildren(joinRoomPopupPanel.transform, "Join");
                if (popupPrimaryButtonTransform != null)
                {
                    roomPopupActionLabel = FindTextComponent(popupPrimaryButtonTransform.gameObject);
                }
            }
        }

        private void ShowJoinEntryPanel()
        {
            roomPanelMode = RoomPanelMode.JoinEntry;
            SetJoinRoomPopupVisible(true);
            SetRoomIdFieldReadOnly(false);
            SetRoomIdFieldText(string.Empty);
            SetComponentText(roomPopupActionLabel, joinRoomConfirmLabel);
            SetComponentInteractable(roomPopupPrimaryButton, true);
        }

        private void ShowPrivateWaitingPanel(bool isHost, string roomId)
        {
            roomPanelMode = isHost ? RoomPanelMode.PrivateWaitingHost : RoomPanelMode.PrivateWaitingGuest;
            SetJoinRoomPopupVisible(true);
            SetRoomIdFieldReadOnly(true);
            SetRoomIdFieldText(roomId);
            RefreshPrivateWaitingUi();
        }

        private void RefreshPrivateWaitingUi()
        {
            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost)
            {
                bool canStart = onlineMatchController != null
                    && onlineMatchController.CanHostStartMatch
                    && currentOccupiedSeats >= 2;
                string label = canStart
                    ? hostStartLabel
                    : $"Waiting ({currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)})";
                SetComponentText(roomPopupActionLabel, label);
                SetComponentInteractable(roomPopupPrimaryButton, canStart);
            }
            else if (roomPanelMode == RoomPanelMode.PrivateWaitingGuest)
            {
                string label = $"Waiting ({currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)})";
                SetComponentText(roomPopupActionLabel, label);
                SetComponentInteractable(roomPopupPrimaryButton, false);
            }
        }

        private void HideRoomPanel()
        {
            roomPanelMode = RoomPanelMode.Hidden;
            SetJoinRoomPopupVisible(false);
        }

        private void SetJoinRoomPopupVisible(bool visible)
        {
            EnsureRoomUiReferences();
            if (joinRoomPopupPanel != null)
            {
                joinRoomPopupPanel.SetActive(visible);
            }
        }

        private string GetTrimmedRoomId()
        {
            string value = GetComponentText(roomIdInputField);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private void SetRoomIdFieldText(string value)
        {
            SetComponentText(roomIdInputField, value ?? string.Empty);
        }

        private void SetRoomIdFieldReadOnly(bool isReadOnly)
        {
            SetComponentBoolProperty(roomIdInputField, "readOnly", isReadOnly);
        }

        private static string GenerateRoomId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        private static Transform FindInActiveScene(string name)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform found = FindInChildren(roots[i].transform, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindInChildren(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindInChildren(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Component FindComponentByTypeName(GameObject target, string typeName)
        {
            if (target == null || string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name == typeName)
                {
                    return component;
                }
            }

            return null;
        }

        private static Component FindTextComponent(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (GetTextProperty(component.GetType()) != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static string GetComponentText(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            PropertyInfo property = GetTextProperty(component.GetType());
            if (property == null)
            {
                return string.Empty;
            }

            object value = property.GetValue(component, null);
            return value as string ?? string.Empty;
        }

        private static void SetComponentText(Component component, string value)
        {
            if (component == null)
            {
                return;
            }

            PropertyInfo property = GetTextProperty(component.GetType());
            if (property == null || !property.CanWrite)
            {
                return;
            }

            property.SetValue(component, value, null);
        }

        private static PropertyInfo GetTextProperty(Type type)
        {
            if (type == null)
            {
                return null;
            }

            PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(string) || !property.CanRead)
            {
                return null;
            }

            return property;
        }

        private static void SetComponentInteractable(Component component, bool value)
        {
            SetComponentBoolProperty(component, "interactable", value);
        }

        private static void SetComponentBoolProperty(Component component, string propertyName, bool value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
            {
                return;
            }

            property.SetValue(component, value, null);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            EnsureRoomUiReferences();

            if (!useNetworkLobby)
            {
                return;
            }

            EnsureNetworkClientInEditor();
            EnsureOnlineMatchControllerInEditor();
        }

        private void EnsureNetworkClientInEditor()
        {
            if (networkClient == null)
            {
                networkClient = GetComponent<HotDynamiteLobbyNetworkClient>();
            }

            if (networkClient == null)
            {
                networkClient = Undo.AddComponent<HotDynamiteLobbyNetworkClient>(gameObject);
                EditorUtility.SetDirty(gameObject);
            }
        }

        private void EnsureOnlineMatchControllerInEditor()
        {
            if (onlineMatchController == null)
            {
                onlineMatchController = GetComponent<HotDynamiteOnlineMatchController>();
            }

            if (onlineMatchController == null)
            {
                onlineMatchController = Undo.AddComponent<HotDynamiteOnlineMatchController>(gameObject);
                EditorUtility.SetDirty(gameObject);
            }
        }
#endif

        private void InitializeOnlineMatchController()
        {
            if (!useNetworkLobby || onlineMatchController == null || networkClient == null || gameManager == null)
            {
                return;
            }

            onlineMatchController.Initialize(gameManager, lobbyCanvas, gameContainer, networkClient);
        }

        private void SubscribeToNetworkEvents()
        {
            if (!useNetworkLobby || networkClient == null)
            {
                return;
            }

            networkClient.Connected += HandleNetworkConnected;
            networkClient.WelcomeReceived += HandleWelcomeReceived;
            networkClient.JoinedRoom += HandleJoinedRoom;
            networkClient.RoomOccupancyChanged += HandleRoomOccupancyChanged;
            networkClient.RoomReady += HandleRoomReady;
            networkClient.BackendErrorReceived += HandleBackendError;
            networkClient.ConnectionError += HandleConnectionError;
            networkClient.Disconnected += HandleDisconnected;
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (networkClient == null)
            {
                return;
            }

            networkClient.Connected -= HandleNetworkConnected;
            networkClient.WelcomeReceived -= HandleWelcomeReceived;
            networkClient.JoinedRoom -= HandleJoinedRoom;
            networkClient.RoomOccupancyChanged -= HandleRoomOccupancyChanged;
            networkClient.RoomReady -= HandleRoomReady;
            networkClient.BackendErrorReceived -= HandleBackendError;
            networkClient.ConnectionError -= HandleConnectionError;
            networkClient.Disconnected -= HandleDisconnected;
        }

        private void HandleNetworkConnected()
        {
            Debug.Log("HotDynamiteLobbyManager: Connected to backend.");
        }

        private void HandleWelcomeReceived(HotDynamiteWelcomeMessage welcome)
        {
            Debug.Log($"HotDynamiteLobbyManager: Received welcome for user `{welcome.userId}`.");
        }

        private void HandleJoinedRoom(HotDynamiteJoinedMessage joined)
        {
            joinRequestInFlight = false;
            currentCapacity = joined.capacity;
            currentOccupiedSeats = CountOccupiedSeats(joined.chairs);
            Debug.Log($"HotDynamiteLobbyManager: Joined room `{joined.roomId}` as chair {joined.chairId}.");

            if (networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Private)
            {
                ShowPrivateWaitingPanel(networkClient.IsHost, joined.roomId);
                return;
            }

            HideRoomPanel();
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            Debug.Log($"HotDynamiteLobbyManager: Room occupancy {occupiedSeats}/{capacity}.");
            RefreshPrivateWaitingUi();
        }

        private void HandleRoomReady(string[] chairs)
        {
            Debug.Log("HotDynamiteLobbyManager: Room is full and ready.");

            if (startLocalGameWhenRoomReady)
            {
                StartGameLocalFlow();
            }
        }

        private void HandleBackendError(HotDynamiteErrorMessage error)
        {
            joinRequestInFlight = false;
            Debug.LogError($"HotDynamiteLobbyManager: Backend error: {error.message}");
        }

        private void HandleConnectionError(string errorMessage)
        {
            joinRequestInFlight = false;
            Debug.LogError($"HotDynamiteLobbyManager: Connection error: {errorMessage}");
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            joinRequestInFlight = false;
            HideRoomPanel();
            Debug.Log($"HotDynamiteLobbyManager: Disconnected from backend. Status: {closeReason.Status}, Description: {closeReason.Description}");
        }

        private static int CountOccupiedSeats(string[] chairs)
        {
            if (chairs == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < chairs.Length; i++)
            {
                if (!string.IsNullOrEmpty(chairs[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private void StartGameLocalFlow()
        {
            StartGameLocalFlow(
                offlineTotalPlayers > 0 ? offlineTotalPlayers : 4,
                offlineHumanPlayers > 0 ? offlineHumanPlayers : 1);
        }

        private void StartGameLocalFlow(int totalPlayers, int humanPlayers)
        {
            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(true);
            }

            if (gameManager != null)
            {
                gameManager.StartGameLocal(totalPlayers, humanPlayers);
            }
            else
            {
                Debug.LogError("HotDynamiteLobbyManager: HotDynamiteGameManager reference is missing!");
            }
        }
    }
}
