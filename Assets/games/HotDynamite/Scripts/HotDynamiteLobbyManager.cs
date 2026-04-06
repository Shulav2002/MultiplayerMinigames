using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using HotDynamite.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HotDynamite.Lobby
{
    public class HotDynamiteLobbyManager : MonoBehaviour
    {
        private enum ReplayMode
        {
            None = 0,
            OfflineBots = 1,
            QuickPlayOnline = 2
        }

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

        [Header("Join Popup")]
        [SerializeField] private GameObject joinRoomPopupPanel;
        [SerializeField] private Component roomIdInputField;
        [SerializeField] private Component roomPopupActionLabel;
        [SerializeField] private Component roomPopupPrimaryButton;

        [Header("Create Popup")]
        [SerializeField] private GameObject createRoomPopupPanel;
        [SerializeField] private Component createRoomIdLabel;
        [SerializeField] private Component createRoomOccupancyLabel;
        [SerializeField] private Component createRoomStartButton;

        [Header("Game End")]
        [SerializeField] private GameObject lobbyRoot;
        [SerializeField] private GameObject gameEndPanel;
        [SerializeField] private GameObject waitingForPlayersPanel;
        [SerializeField] private GameObject networkIssueRoot;
        [SerializeField] private GameObject networkIssuePanel;
        [SerializeField] private Component networkIssueTitleLabel;
        [SerializeField] private Component networkIssueMessageLabel;
        [SerializeField] private Button networkIssueCloseButton;
        [SerializeField] private GameObject blueWinnerPanel;
        [SerializeField] private GameObject greenWinnerPanel;
        [SerializeField] private GameObject redWinnerPanel;
        [SerializeField] private GameObject yellowWinnerPanel;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button backToHomeButton;

        [Header("Room Popup Labels")]
        [SerializeField] private string joinRoomConfirmLabel = "Join";
        [SerializeField] private string networkIssueTitle = "Network Error";
        [SerializeField] private string networkIssueConnectMessage = "Unable to connect to the server. Please check your internet connection and try again.";
        [SerializeField] private string networkIssueDisconnectMessage = "Connection to the server was lost. Please check your internet connection and try again.";

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
        [SerializeField, Min(0f)] private float gameEndDelaySeconds = 0.5f;

        private bool joinRequestInFlight;
        private bool gameEndShown;
        private bool subscribedToGameEvents;
        private bool lastMatchWasOffline;
        private ReplayMode lastReplayMode;
        private int lastOfflineTotalPlayers = 4;
        private int lastOfflineHumanPlayers = 1;
        private RoomPanelMode roomPanelMode;
        private int currentOccupiedSeats;
        private int currentCapacity;
        private Coroutine showGameEndCoroutine;
        private bool dismissPrivateRoomRequested;
        private bool privateRoomDismissInFlight;

        private bool IsPrimaryLobbyManager => string.Equals(gameObject.name, "Manager", StringComparison.Ordinal);

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

            if (IsPrimaryLobbyManager)
            {
                EnsureGameEndUiReferences();
                RegisterGameEndButtons();
                SubscribeToGameEvents();
                ShowLobbyHome();
            }
        }

        private void OnDestroy()
        {
            if (IsPrimaryLobbyManager)
            {
                UnregisterGameEndButtons();
                UnsubscribeFromGameEvents();
            }

            UnsubscribeFromNetworkEvents();
        }

        public async void OnPlayButtonClicked()
        {
            lastMatchWasOffline = false;
            lastReplayMode = ReplayMode.QuickPlayOnline;

            if (!useNetworkLobby)
            {
                StartGameLocalFlow();
                return;
            }

            ShowRandomWaitingPanel();
            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        public void OnPlayWithBotsButtonClicked()
        {
            int totalPlayers = Mathf.Max(2, offlineTotalPlayers);
            int humanPlayers = Mathf.Clamp(offlineHumanPlayers, 1, totalPlayers);
            lastReplayMode = ReplayMode.OfflineBots;
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

            lastMatchWasOffline = false;
            dismissPrivateRoomRequested = false;

            if (!await EnsureReadyForPrivateRoomCreateAsync())
            {
                return;
            }

            EnsureRoomUiReferences();
            ShowPrivateWaitingPanel(isHost: true, roomId: "Creating...", occupiedSeats: 1, capacity: Mathf.Max(2, networkClient != null ? networkClient.RoomCapacity : 4));
            await BeginOnlineJoinAsync(null, createRoom: true, usePrivateRoom: true);
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
                return;
            }

            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost || roomPanelMode == RoomPanelMode.PrivateWaitingGuest)
            {
                OnCloseCreateRoomPopupClicked();
            }
        }

        public async void OnCloseCreateRoomPopupClicked()
        {
            if (privateRoomDismissInFlight)
            {
                return;
            }

            privateRoomDismissInFlight = true;
            dismissPrivateRoomRequested = true;
            HideRoomPanel();
            try
            {
                await DismissPrivateRoomAsync();
            }
            finally
            {
                privateRoomDismissInFlight = false;
            }
        }

        public void LoadGameScene()
        {
            Debug.Log($"Loading game scene: {gameSceneName}");
            SceneManager.LoadScene(gameSceneName);
        }

        public void OnPlayAgainButtonClicked()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            HideGameEnd();

            switch (ResolveReplayModeForReplay())
            {
                case ReplayMode.OfflineBots:
                    StartGameLocalFlow(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
                    break;
                case ReplayMode.QuickPlayOnline:
                    RestartQuickPlayOnline();
                    break;
                default:
                    ShowLobbyHome();
                    break;
            }
        }

        public async void OnBackToHomeButtonClicked()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            HideGameEnd();
            await ReturnToHomeAsync();
        }

        public void OnCloseNetworkIssueButtonClicked()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            ShowLobbyHome();
        }

        private async void SubmitJoinRoomRequest()
        {
            string roomId = GetTrimmedRoomId();
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("HotDynamiteLobbyManager: Room id is required to join a private room.");
                return;
            }

            lastMatchWasOffline = false;
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

            if (!usePrivateRoom)
            {
                ShowRandomWaitingPanel();
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
                    if (usePrivateRoom && createRoom)
                    {
                        Debug.Log("HotDynamiteLobbyManager: Leaving existing room before creating a new private room.");
                        await LeaveCurrentRoomIfNeededAsync();
                    }
                }

                if (networkClient.IsInRoom)
                {
                    Debug.Log($"HotDynamiteLobbyManager: Already in room `{networkClient.CurrentRoomId}`.");
                    if (createRoom)
                    {
                        HideCreateRoomPopup();
                    }

                    joinRequestInFlight = false;
                    return;
                }

                if (usePrivateRoom)
                {
                    string action = createRoom ? "Creating" : "Joining";
                    string roomDescription = string.IsNullOrEmpty(roomId) ? "<server-generated>" : roomId;
                    Debug.Log($"HotDynamiteLobbyManager: {action} private room `{roomDescription}` for game `{networkClient.GameId}`.");
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
                Debug.LogError($"HotDynamiteLobbyManager: Failed to connect or join backend room.\n{exception}");
                if (createRoom)
                {
                    HideCreateRoomPopup();
                }

                if (!usePrivateRoom || createRoom)
                {
                    ShowNetworkIssue(networkIssueConnectMessage);
                }
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

            if (createRoomPopupPanel == null)
            {
                Transform popupPanel = FindInActiveScene("CreatePopUpPanel");
                if (popupPanel != null)
                {
                    createRoomPopupPanel = popupPanel.gameObject;
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

            if (createRoomIdLabel == null && createRoomPopupPanel != null)
            {
                Transform roomIdTransform = FindInChildren(createRoomPopupPanel.transform, "RoomID");
                if (roomIdTransform != null)
                {
                    createRoomIdLabel = FindTextComponent(roomIdTransform.gameObject);
                }
            }

            if (createRoomOccupancyLabel == null && createRoomPopupPanel != null)
            {
                Transform occupancyTransform = FindInChildren(createRoomPopupPanel.transform, "JoinedPlayersCount");
                if (occupancyTransform != null)
                {
                    createRoomOccupancyLabel = FindTextComponent(occupancyTransform.gameObject);
                }
            }

            if (createRoomStartButton == null && createRoomPopupPanel != null)
            {
                Transform startTransform = FindInChildren(createRoomPopupPanel.transform, "Start");
                if (startTransform != null)
                {
                    createRoomStartButton = FindComponentByTypeName(startTransform.gameObject, "Button");
                }
            }
        }

        private void ShowJoinEntryPanel()
        {
            roomPanelMode = RoomPanelMode.JoinEntry;
            HideCreateRoomPopup();
            SetJoinRoomPopupVisible(true);
            SetRoomIdFieldReadOnly(false);
            SetRoomIdFieldText(string.Empty);
            SetComponentText(roomPopupActionLabel, joinRoomConfirmLabel);
            SetComponentInteractable(roomPopupPrimaryButton, true);
        }

        private void ShowPrivateWaitingPanel(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            roomPanelMode = isHost ? RoomPanelMode.PrivateWaitingHost : RoomPanelMode.PrivateWaitingGuest;
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            HideJoinRoomPopup();
            SetCreateRoomPopupVisible(true);
            SetComponentText(createRoomIdLabel, roomId ?? string.Empty);
            RefreshPrivateWaitingUi();
        }

        private void RefreshPrivateWaitingUi()
        {
            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost)
            {
                bool canStart = onlineMatchController != null
                    && onlineMatchController.CanHostStartMatch
                    && currentOccupiedSeats >= 2;
                SetComponentText(createRoomOccupancyLabel, $"{currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)} joined");
                SetComponentInteractable(createRoomStartButton, canStart);
            }
            else if (roomPanelMode == RoomPanelMode.PrivateWaitingGuest)
            {
                SetComponentText(createRoomOccupancyLabel, $"{currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)} joined");
                SetComponentInteractable(createRoomStartButton, false);
            }
        }

        private void HideRoomPanel()
        {
            roomPanelMode = RoomPanelMode.Hidden;
            HideJoinRoomPopup();
            HideCreateRoomPopup();
        }

        private void HideJoinRoomPopup()
        {
            SetJoinRoomPopupVisible(false);
        }

        private void HideCreateRoomPopup()
        {
            SetCreateRoomPopupVisible(false);
        }

        private void SetJoinRoomPopupVisible(bool visible)
        {
            EnsureRoomUiReferences();
            if (joinRoomPopupPanel != null)
            {
                joinRoomPopupPanel.SetActive(visible);
            }
        }

        private void SetCreateRoomPopupVisible(bool visible)
        {
            EnsureRoomUiReferences();
            if (createRoomPopupPanel != null)
            {
                createRoomPopupPanel.SetActive(visible);
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

        private void EnsureGameEndUiReferences()
        {
            if (lobbyRoot == null)
            {
                Transform lobbyTransform = FindUnderLobbyCanvas("Lobby");
                if (lobbyTransform != null)
                {
                    lobbyRoot = lobbyTransform.gameObject;
                }
            }

            if (gameEndPanel == null)
            {
                Transform gameEndTransform = FindUnderLobbyCanvas("GameEnd");
                if (gameEndTransform != null)
                {
                    gameEndPanel = gameEndTransform.gameObject;
                }
            }

            if (waitingForPlayersPanel == null)
            {
                Transform waitingTransform = FindUnderLobbyCanvas("WaitingForPlayers");
                if (waitingTransform != null)
                {
                    waitingForPlayersPanel = waitingTransform.gameObject;
                }
            }

            if (networkIssueRoot == null)
            {
                Transform networkIssueTransform = FindUnderLobbyCanvas("NetworkIssue");
                if (networkIssueTransform != null)
                {
                    networkIssueRoot = networkIssueTransform.gameObject;
                }
            }

            if (networkIssuePanel == null)
            {
                Transform networkIssuePanelTransform = FindUnderLobbyCanvas("NetworkIssuePanel");
                if (networkIssuePanelTransform != null)
                {
                    networkIssuePanel = networkIssuePanelTransform.gameObject;
                }
            }

            if (networkIssueTitleLabel == null)
            {
                networkIssueTitleLabel = FindNamedComponentUnderNetworkIssue("NetworkError");
            }

            if (networkIssueMessageLabel == null)
            {
                networkIssueMessageLabel = FindNamedComponentUnderNetworkIssue("Isssue");
            }

            if (networkIssueCloseButton == null)
            {
                networkIssueCloseButton = FindNamedButtonUnderNetworkIssue("Close");
            }

            if (blueWinnerPanel == null)
            {
                blueWinnerPanel = FindNamedGameObjectUnderGameEnd("BlueWinner");
            }

            if (greenWinnerPanel == null)
            {
                greenWinnerPanel = FindNamedGameObjectUnderGameEnd("GreenWinner");
            }

            if (redWinnerPanel == null)
            {
                redWinnerPanel = FindNamedGameObjectUnderGameEnd("RedWinner");
            }

            if (yellowWinnerPanel == null)
            {
                yellowWinnerPanel = FindNamedGameObjectUnderGameEnd("YellowWinner");
            }

            if (playAgainButton == null)
            {
                playAgainButton = FindNamedButtonUnderGameEnd("PlayAgain");
            }

            if (backToHomeButton == null)
            {
                backToHomeButton = FindNamedButtonUnderGameEnd("BackToHome");
            }
        }

        private void RegisterGameEndButtons()
        {
            EnsureGameEndUiReferences();

            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(OnPlayAgainButtonClicked);
                playAgainButton.onClick.AddListener(OnPlayAgainButtonClicked);
            }

            if (backToHomeButton != null)
            {
                backToHomeButton.onClick.RemoveListener(OnBackToHomeButtonClicked);
                backToHomeButton.onClick.AddListener(OnBackToHomeButtonClicked);
            }

            if (networkIssueCloseButton != null)
            {
                networkIssueCloseButton.onClick.RemoveListener(OnCloseNetworkIssueButtonClicked);
                networkIssueCloseButton.onClick.AddListener(OnCloseNetworkIssueButtonClicked);
            }
        }

        private void UnregisterGameEndButtons()
        {
            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(OnPlayAgainButtonClicked);
            }

            if (backToHomeButton != null)
            {
                backToHomeButton.onClick.RemoveListener(OnBackToHomeButtonClicked);
            }

            if (networkIssueCloseButton != null)
            {
                networkIssueCloseButton.onClick.RemoveListener(OnCloseNetworkIssueButtonClicked);
            }
        }

        private void SubscribeToGameEvents()
        {
            if (subscribedToGameEvents)
            {
                return;
            }

            if (gameManager != null)
            {
                gameManager.GameEnded += HandleGameEnded;
            }

            if (onlineMatchController != null)
            {
                onlineMatchController.MatchEnded += HandleMatchEnded;
            }

            subscribedToGameEvents = true;
        }

        private void UnsubscribeFromGameEvents()
        {
            if (!subscribedToGameEvents)
            {
                return;
            }

            if (gameManager != null)
            {
                gameManager.GameEnded -= HandleGameEnded;
            }

            if (onlineMatchController != null)
            {
                onlineMatchController.MatchEnded -= HandleMatchEnded;
            }

            subscribedToGameEvents = false;
        }

        private void ShowLobbyHome()
        {
            HideRoomPanel();
            HideGameEnd();
            HideWaitingForPlayers();
            HideNetworkIssue();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(true);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(false);
            }
        }

        private void ShowRandomWaitingPanel()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            EnsureGameEndUiReferences();
            HideRoomPanel();
            HideGameEnd();
            HideNetworkIssue();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(false);
            }

            if (waitingForPlayersPanel != null)
            {
                waitingForPlayersPanel.SetActive(true);
            }
        }

        private void ShowGameEnd(int winningChairId)
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            if (gameEndShown || showGameEndCoroutine != null)
            {
                return;
            }

            showGameEndCoroutine = StartCoroutine(ShowGameEndAfterDelay(winningChairId));
        }

        private void HideGameEnd()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            if (showGameEndCoroutine != null)
            {
                StopCoroutine(showGameEndCoroutine);
                showGameEndCoroutine = null;
            }

            gameEndShown = false;
            SetWinnerPanelsActive(false, false, false, false);

            if (gameEndPanel != null)
            {
                gameEndPanel.SetActive(false);
            }
        }

        private void HideWaitingForPlayers()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            if (waitingForPlayersPanel != null)
            {
                waitingForPlayersPanel.SetActive(false);
            }
        }

        private void ShowNetworkIssue(string message)
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            EnsureGameEndUiReferences();
            HideRoomPanel();
            HideGameEnd();
            HideWaitingForPlayers();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(true);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(false);
            }

            SetComponentText(networkIssueTitleLabel, networkIssueTitle);
            SetComponentText(networkIssueMessageLabel, string.IsNullOrWhiteSpace(message) ? networkIssueConnectMessage : message);

            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(true);
            }
            else if (networkIssuePanel != null)
            {
                networkIssuePanel.SetActive(true);
            }
        }

        private void HideNetworkIssue()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(false);
            }
            else if (networkIssuePanel != null)
            {
                networkIssuePanel.SetActive(false);
            }
        }

        private System.Collections.IEnumerator ShowGameEndAfterDelay(int winningChairId)
        {
            float delay = Mathf.Max(0f, gameEndDelaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            showGameEndCoroutine = null;
            EnsureGameEndUiReferences();
            gameEndShown = true;
            HideRoomPanel();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(false);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(true);
            }

            if (gameEndPanel != null)
            {
                gameEndPanel.SetActive(true);
            }

            HideWaitingForPlayers();
            SetWinnerPanel(winningChairId);
        }

        private void SetWinnerPanel(int winningChairId)
        {
            int appearanceIndex = ResolveWinningAppearanceIndex(winningChairId);
            SetWinnerPanelsActive(
                appearanceIndex == 1,
                appearanceIndex == 2,
                appearanceIndex == 0,
                appearanceIndex == 3);
        }

        private int ResolveWinningAppearanceIndex(int winningChairId)
        {
            if (gameManager != null)
            {
                HotDynamitePlayer winningPlayer = gameManager.GetPlayerByChair(winningChairId);
                if (winningPlayer != null)
                {
                    return Mathf.Max(0, winningPlayer.AppearanceIndex % 4);
                }

                IReadOnlyList<HotDynamitePlayer> players = gameManager.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    HotDynamitePlayer candidate = players[i];
                    if (candidate != null && candidate.IsAlive)
                    {
                        return Mathf.Max(0, candidate.AppearanceIndex % 4);
                    }
                }
            }

            return Mathf.Clamp(winningChairId, 0, 3);
        }

        private void SetWinnerPanelsActive(bool blueVisible, bool greenVisible, bool redVisible, bool yellowVisible)
        {
            if (blueWinnerPanel != null)
            {
                blueWinnerPanel.SetActive(blueVisible);
            }

            if (greenWinnerPanel != null)
            {
                greenWinnerPanel.SetActive(greenVisible);
            }

            if (redWinnerPanel != null)
            {
                redWinnerPanel.SetActive(redVisible);
            }

            if (yellowWinnerPanel != null)
            {
                yellowWinnerPanel.SetActive(yellowVisible);
            }
        }

        private void HandleGameEnded(int winningChairId)
        {
            ShowGameEnd(winningChairId);
        }

        private void HandleMatchEnded(int winningChairId)
        {
            ShowGameEnd(winningChairId);
        }

        private async void RestartQuickPlayOnline()
        {
            lastMatchWasOffline = false;
            lastReplayMode = ReplayMode.QuickPlayOnline;
            ShowRandomWaitingPanel();
            await LeaveCurrentRoomIfNeededAsync();
            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        private ReplayMode ResolveReplayModeForReplay()
        {
            if (lastReplayMode != ReplayMode.None)
            {
                return lastReplayMode;
            }

            if (lastMatchWasOffline)
            {
                return ReplayMode.OfflineBots;
            }

            if (useNetworkLobby && networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Random)
            {
                return ReplayMode.QuickPlayOnline;
            }

            return ReplayMode.None;
        }

        private async Task ReturnToHomeAsync()
        {
            dismissPrivateRoomRequested = false;
            await LeaveCurrentRoomIfNeededAsync();
            ShowLobbyHome();
        }

        private async Task<bool> EnsureReadyForPrivateRoomCreateAsync()
        {
            EnsureNetworkClient();
            if (networkClient == null)
            {
                return false;
            }

            if (privateRoomDismissInFlight || joinRequestInFlight)
            {
                Debug.Log("HotDynamiteLobbyManager: Waiting for the previous private-room request to finish.");
                return false;
            }

            if (!networkClient.IsConnected || !networkClient.IsInRoom)
            {
                return true;
            }

            await LeaveCurrentRoomIfNeededAsync();
            return !networkClient.IsInRoom;
        }

        private async Task DismissPrivateRoomAsync()
        {
            if (!useNetworkLobby || networkClient == null)
            {
                dismissPrivateRoomRequested = false;
                ShowLobbyHome();
                return;
            }

            if (joinRequestInFlight)
            {
                float timeoutAt = Time.realtimeSinceStartup + 3f;
                while (joinRequestInFlight && Time.realtimeSinceStartup < timeoutAt)
                {
                    await Task.Yield();
                }
            }

            if (networkClient.IsConnected && networkClient.IsInRoom)
            {
                await LeaveCurrentRoomIfNeededAsync();
                dismissPrivateRoomRequested = false;
                ShowLobbyHome();
                return;
            }

            if (!joinRequestInFlight)
            {
                dismissPrivateRoomRequested = false;
                ShowLobbyHome();
            }
        }

        private async Task LeaveCurrentRoomIfNeededAsync()
        {
            if (!useNetworkLobby || networkClient == null || !networkClient.IsConnected || !networkClient.IsInRoom)
            {
                return;
            }

            try
            {
                await networkClient.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"HotDynamiteLobbyManager: Leave room request failed. {exception.Message}");
                return;
            }

            float timeoutAt = Time.realtimeSinceStartup + 2f;
            while (networkClient.IsInRoom && Time.realtimeSinceStartup < timeoutAt)
            {
                await Task.Yield();
            }
        }

        private Transform FindUnderLobbyCanvas(string name)
        {
            if (lobbyCanvas != null)
            {
                return FindInChildren(lobbyCanvas.transform, name);
            }

            return FindInActiveScene(name);
        }

        private GameObject FindNamedGameObjectUnderGameEnd(string name)
        {
            if (gameEndPanel == null)
            {
                return null;
            }

            Transform found = FindInChildren(gameEndPanel.transform, name);
            return found != null ? found.gameObject : null;
        }

        private Button FindNamedButtonUnderGameEnd(string name)
        {
            GameObject target = FindNamedGameObjectUnderGameEnd(name);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private Component FindNamedComponentUnderNetworkIssue(string name)
        {
            Transform searchRoot = networkIssueRoot != null ? networkIssueRoot.transform : networkIssuePanel != null ? networkIssuePanel.transform : null;
            if (searchRoot == null)
            {
                return null;
            }

            Transform found = FindInChildren(searchRoot, name);
            if (found == null)
            {
                return null;
            }

            Component component = found.GetComponent<Text>();
            if (component != null)
            {
                return component;
            }

            return found.GetComponent("TMP_Text");
        }

        private Button FindNamedButtonUnderNetworkIssue(string name)
        {
            Transform searchRoot = networkIssueRoot != null ? networkIssueRoot.transform : networkIssuePanel != null ? networkIssuePanel.transform : null;
            if (searchRoot == null)
            {
                return null;
            }

            Transform found = FindInChildren(searchRoot, name);
            return found != null ? found.GetComponent<Button>() : null;
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

            EnsureGameEndUiReferences();
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

            if (dismissPrivateRoomRequested && networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Private)
            {
                DismissJoinedPrivateRoomAfterClose();
                return;
            }

            if (networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Private)
            {
                ShowPrivateWaitingPanel(networkClient.IsHost, joined.roomId, currentOccupiedSeats, currentCapacity);
                return;
            }

            if (networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Random)
            {
                lastReplayMode = ReplayMode.QuickPlayOnline;
                lastMatchWasOffline = false;
                ShowRandomWaitingPanel();
            }
            else
            {
                HideRoomPanel();
            }
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
            dismissPrivateRoomRequested = false;
            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost && currentOccupiedSeats <= 1)
            {
                HideCreateRoomPopup();
            }

            if (networkClient != null && networkClient.RoomMode == HotDynamiteRoomModes.Random)
            {
                ShowLobbyHome();
            }

            Debug.LogError($"HotDynamiteLobbyManager: Backend error: {error.message}");
        }

        private void HandleConnectionError(string errorMessage)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost && currentOccupiedSeats <= 1)
            {
                HideCreateRoomPopup();
            }

            if (networkClient != null && (networkClient.RoomMode == HotDynamiteRoomModes.Random || roomPanelMode == RoomPanelMode.PrivateWaitingHost))
            {
                ShowNetworkIssue(string.IsNullOrWhiteSpace(errorMessage) ? networkIssueConnectMessage : errorMessage);
            }

            Debug.LogError($"HotDynamiteLobbyManager: Connection error: {errorMessage}");
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            HideRoomPanel();
            if (networkClient != null && (networkClient.RoomMode == HotDynamiteRoomModes.Random || currentOccupiedSeats > 0 || roomPanelMode != RoomPanelMode.Hidden))
            {
                ShowNetworkIssue(networkIssueDisconnectMessage);
            }
            Debug.Log($"HotDynamiteLobbyManager: Disconnected from backend. Status: {closeReason.Status}, Description: {closeReason.Description}");
        }

        private async void DismissJoinedPrivateRoomAfterClose()
        {
            await LeaveCurrentRoomIfNeededAsync();
            dismissPrivateRoomRequested = false;
            ShowLobbyHome();
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
            lastMatchWasOffline = true;
            lastReplayMode = ReplayMode.OfflineBots;
            lastOfflineTotalPlayers = Mathf.Max(2, totalPlayers);
            lastOfflineHumanPlayers = Mathf.Clamp(humanPlayers, 1, lastOfflineTotalPlayers);

            HideRoomPanel();
            HideGameEnd();
            HideWaitingForPlayers();

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(false);
            }

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
                gameManager.StartGameLocal(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
            }
            else
            {
                Debug.LogError("HotDynamiteLobbyManager: HotDynamiteGameManager reference is missing!");
            }
        }
    }
}
