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
        [Header("Scene Loading")]
        [Tooltip("The name of the scene to load. E.g., 'HotDynamite'")]
        [SerializeField] private string gameSceneName = "HotDynamite";

        [Header("UI & Game References")]
        [Tooltip("The Lobby UI canvas to turn off when the game starts.")]
        [SerializeField] private GameObject lobbyCanvas;

        [Tooltip("The parent GameObject containing the game elements (GameManager, Level, etc) to turn on.")]
        [SerializeField] private GameObject gameContainer;

        [Header("Game Manager Reference")]
        [Tooltip("Reference to the GameManager to call StartGame() on.")]
        [SerializeField] private HotDynamiteGameManager gameManager;

        [Header("Networking")]
        [Tooltip("When enabled, Play connects to the backend and joins a room instead of starting local immediately.")]
        [SerializeField] private bool useNetworkLobby = true;

        [Tooltip("Network client used for backend lobby communication.")]
        [SerializeField] private HotDynamiteLobbyNetworkClient networkClient;

        [Tooltip("Coordinates the host-authoritative online match flow once a room is joined.")]
        [SerializeField] private HotDynamiteOnlineMatchController onlineMatchController;

        [Tooltip("Starts the local game automatically once the room is full. Leave disabled until gameplay sync is implemented.")]
        [SerializeField] private bool startLocalGameWhenRoomReady = false;

        [Header("Offline With Bots")]
        [Tooltip("Total participants for offline play. One human and the rest bots is the default setup.")]
        [SerializeField, Min(2)] private int offlineTotalPlayers = 4;

        [Tooltip("Number of local human players for offline play.")]
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;

        private bool joinRequestInFlight;

        private void Start()
        {
            EnsureNetworkClient();
            EnsureOnlineMatchController();

            if (useNetworkLobby && (networkClient == null || onlineMatchController == null))
            {
                Debug.LogError("HotDynamiteLobbyManager: Network components are missing. Add them in edit mode before entering play mode.");
                return;
            }

            SubscribeToNetworkEvents();
            InitializeOnlineMatchController();

            // Ensure proper initial state
            if (lobbyCanvas != null)
                lobbyCanvas.SetActive(true);

            if (gameContainer != null)
                gameContainer.SetActive(false);
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkEvents();
        }

        /// <summary>
        /// Call this function from your "Play" UI Button.
        /// It hides the lobby, turns on the game container, and starts the game.
        /// </summary>
        public async void OnPlayButtonClicked()
        {
            if (!useNetworkLobby)
            {
                StartGameLocalFlow();
                return;
            }

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

                if (!networkClient.IsInRoom)
                {
                    Debug.Log($"HotDynamiteLobbyManager: Joining room for game `{networkClient.GameId}` (capacity {networkClient.RoomCapacity})...");
                    await networkClient.JoinAsync();
                }
            }
            catch (System.Exception exception)
            {
                joinRequestInFlight = false;
                Debug.LogError($"HotDynamiteLobbyManager: Failed to connect or join backend room. {exception.Message}");
            }
        }

        /// <summary>
        /// Call this function from the "Play With Bots" UI Button.
        /// It always starts an offline match, regardless of online lobby settings.
        /// </summary>
        public void OnPlayWithBotsButtonClicked()
        {
            int totalPlayers = Mathf.Max(2, offlineTotalPlayers);
            int humanPlayers = Mathf.Clamp(offlineHumanPlayers, 1, totalPlayers);
            StartGameLocalFlow(totalPlayers, humanPlayers);
        }

        /// <summary>
        /// Call this function from your "Play" UI Button if you are using separate scenes.
        /// It will load the specified Game Scene.
        /// </summary>
        public void LoadGameScene()
        {
            Debug.Log($"Loading game scene: {gameSceneName}");
            SceneManager.LoadScene(gameSceneName);
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

#if UNITY_EDITOR

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

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
            Debug.Log($"HotDynamiteLobbyManager: Joined room `{joined.roomId}` as chair {joined.chairId}.");
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            Debug.Log($"HotDynamiteLobbyManager: Room occupancy {occupiedSeats}/{capacity}.");
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
            Debug.Log($"HotDynamiteLobbyManager: Disconnected from backend. Status: {closeReason.Status}, Description: {closeReason.Description}");
        }

        private void StartGameLocalFlow()
        {
            StartGameLocalFlow(
                offlineTotalPlayers > 0 ? offlineTotalPlayers : 4,
                offlineHumanPlayers > 0 ? offlineHumanPlayers : 1);
        }

        private void StartGameLocalFlow(int totalPlayers, int humanPlayers)
        {
            // 1. Hide Lobby UI
            if (lobbyCanvas != null)
                lobbyCanvas.SetActive(false);

            // 2. Show Game Objects
            if (gameContainer != null)
                gameContainer.SetActive(true);

            // 3. Tell the Game Manager to start the game flow
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
