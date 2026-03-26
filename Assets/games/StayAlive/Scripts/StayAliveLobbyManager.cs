using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StayAlive.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StayAlive.Lobby
{
    public class StayAliveLobbyManager : MonoBehaviour
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
            CreateEntry = 2,
            PrivateWaitingHost = 3,
            PrivateWaitingGuest = 4
        }

        [Header("UI & Game References")]
        [SerializeField] private GameObject lobbyCanvas;
        [SerializeField] private GameObject gameContainer;
        [SerializeField] private StayAlivePopupManager popupManager;

        [Header("Join Popup")]
        [SerializeField] private GameObject joinRoomPopupPanel;
        [SerializeField] private Component roomIdInputField;
        [SerializeField] private Component roomPopupActionLabel;
        [SerializeField] private Button roomPopupPrimaryButton;

        [Header("Create Popup")]
        [SerializeField] private GameObject createRoomPopupPanel;
        [SerializeField] private Component createRoomIdLabel;
        [SerializeField] private Component createRoomOccupancyLabel;
        [SerializeField] private Button createRoomStartButton;

        [Header("Game End")]
        [SerializeField] private GameObject lobbyRoot;
        [SerializeField] private GameObject gameEndPanel;
        [SerializeField] private GameObject waitingForPlayersPanel;
        [SerializeField] private GameObject networkIssueRoot;
        [SerializeField] private Component networkIssueTitleLabel;
        [SerializeField] private Component networkIssueMessageLabel;
        [SerializeField] private Button networkIssueCloseButton;
        [SerializeField] private GameObject blueWinnerPanel;
        [SerializeField] private GameObject greenWinnerPanel;
        [SerializeField] private GameObject redWinnerPanel;
        [SerializeField] private GameObject yellowWinnerPanel;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button backToHomeButton;

        [Header("Labels")]
        [SerializeField] private string joinRoomConfirmLabel = "Join";
        [SerializeField] private string networkIssueTitle = "Network Error";
        [SerializeField] private string networkIssueConnectMessage = "Unable to connect to the server. Please check your internet connection and try again.";
        [SerializeField] private string networkIssueDisconnectMessage = "Connection to the server was lost. Please check your internet connection and try again.";

        [Header("Game")]
        [SerializeField] private StayAliveGameManager gameManager;

        [Header("Networking")]
        [SerializeField] private bool useNetworkLobby = true;
        [SerializeField] private StayAliveLobbyNetworkClient networkClient;
        [SerializeField] private StayAliveOnlineMatchController onlineMatchController;

        [Header("Offline With Bots")]
        [SerializeField, Min(2)] private int offlineTotalPlayers = 4;
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;
        [SerializeField, Min(0f)] private float gameEndDelaySeconds = 0.5f;

        private ReplayMode lastReplayMode;
        private int lastOfflineTotalPlayers = 4;
        private int lastOfflineHumanPlayers = 1;
        private RoomPanelMode roomPanelMode;
        private int currentOccupiedSeats;
        private int currentCapacity;
        private string currentRoomId;
        private bool joinRequestInFlight;
        private bool dismissPrivateRoomRequested;
        private bool subscribedToGameEvents;
        private bool subscribedToPopupEvents;
        private Coroutine showGameEndCoroutine;

        private bool IsPrimaryLobbyManager => string.Equals(gameObject.name, "Manager", StringComparison.Ordinal);

        private void Start()
        {
            EnsureReferences();

            if (useNetworkLobby && (networkClient == null || onlineMatchController == null))
            {
                Debug.LogError("StayAliveLobbyManager: Network components are missing.");
                return;
            }

            SubscribeToNetworkEvents();
            InitializeOnlineMatchController();

            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            SubscribeToPopupEvents();
            RegisterButtons();
            SubscribeToGameEvents();
            ShowLobbyHome();
        }

        private void OnDestroy()
        {
            if (IsPrimaryLobbyManager)
            {
                UnregisterButtons();
                UnsubscribeFromGameEvents();
                UnsubscribeFromPopupEvents();
            }

            UnsubscribeFromNetworkEvents();
        }

        public async void OnPlayButtonClicked()
        {
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
            lastReplayMode = ReplayMode.OfflineBots;
            StartGameLocalFlow(offlineTotalPlayers, offlineHumanPlayers);
        }

        public void OnOpenJoinRoomPopupClicked()
        {
            EnsureReferences();
            roomPanelMode = RoomPanelMode.JoinEntry;
            ShowLobbyVisualState();
            SetRoomIdReadOnly(false);
            SetRoomIdText(string.Empty);

            if (popupManager != null)
            {
                popupManager.ShowJoinPopup();
                return;
            }

            SetPopup(joinRoomPopupPanel, true);
            SetPopup(createRoomPopupPanel, false);
            SetComponentText(roomPopupActionLabel, joinRoomConfirmLabel);
        }

        public void OnOpenCreateRoomPopupClicked()
        {
            if (!useNetworkLobby)
            {
                return;
            }

            EnsureReferences();
            roomPanelMode = RoomPanelMode.CreateEntry;
            dismissPrivateRoomRequested = false;
            ShowLobbyVisualState();

            if (popupManager != null)
            {
                popupManager.ShowCreatePopup();
                return;
            }

            SubmitCreateRoomRequest();
        }

        public void OnRoomPopupConfirmButtonClicked()
        {
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                SubmitJoinRoomRequest();
                return;
            }

            if (roomPanelMode == RoomPanelMode.CreateEntry)
            {
                SubmitCreateRoomRequest();
                return;
            }

            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost)
            {
                onlineMatchController?.StartHostedMatchNow();
            }
        }

        public void OnCloseJoinRoomPopupClicked()
        {
            HideRoomPanel();
        }

        public async void OnCloseCreateRoomPopupClicked()
        {
            RoomPanelMode panelMode = roomPanelMode;
            HideRoomPanel();

            bool shouldDismissPrivateRoom = panelMode == RoomPanelMode.PrivateWaitingHost
                || panelMode == RoomPanelMode.PrivateWaitingGuest;
            if (!shouldDismissPrivateRoom)
            {
                dismissPrivateRoomRequested = false;
                return;
            }

            dismissPrivateRoomRequested = true;
            await DismissPrivateRoomAsync();
        }

        public void OnPlayAgainButtonClicked()
        {
            HideGameEnd();

            if (lastReplayMode == ReplayMode.OfflineBots)
            {
                StartGameLocalFlow(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
                return;
            }

            if (lastReplayMode == ReplayMode.QuickPlayOnline)
            {
                RestartQuickPlayOnline();
                return;
            }

            ShowLobbyHome();
        }

        public async void OnBackToHomeButtonClicked()
        {
            HideGameEnd();
            await LeaveCurrentRoomIfNeededAsync();
            ShowLobbyHome();
        }

        public void OnCloseNetworkIssueButtonClicked()
        {
            ShowLobbyHome();
        }

        private async void SubmitJoinRoomRequest()
        {
            string roomId = GetRoomIdText();
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("StayAliveLobbyManager: Room id is required.");
                return;
            }

            await BeginOnlineJoinAsync(roomId, createRoom: false, usePrivateRoom: true);
        }

        private async void SubmitCreateRoomRequest()
        {
            dismissPrivateRoomRequested = false;
            if (!await EnsureReadyForPrivateRoomCreateAsync())
            {
                return;
            }

            ShowPrivateWaitingPanel(isHost: true, "Creating...", 1, Mathf.Max(2, networkClient != null ? networkClient.RoomCapacity : 4));
            await BeginOnlineJoinAsync(null, createRoom: true, usePrivateRoom: true);
        }

        private async Task BeginOnlineJoinAsync(string roomId, bool createRoom, bool usePrivateRoom)
        {
            EnsureReferences();
            if (networkClient == null || joinRequestInFlight)
            {
                return;
            }

            joinRequestInFlight = true;
            try
            {
                if (!networkClient.IsConnected)
                {
                    await networkClient.ConnectAsync();
                }

                if (networkClient.IsInRoom && usePrivateRoom && createRoom)
                {
                    await LeaveCurrentRoomIfNeededAsync();
                }

                if (networkClient.IsInRoom)
                {
                    joinRequestInFlight = false;
                    return;
                }

                if (usePrivateRoom)
                {
                    await networkClient.JoinPrivateAsync(roomId, createRoom);
                }
                else
                {
                    await networkClient.JoinAsync();
                }
            }
            catch (Exception exception)
            {
                joinRequestInFlight = false;
                ShowNetworkIssue(string.IsNullOrWhiteSpace(exception.Message) ? networkIssueConnectMessage : exception.Message);
            }
        }

        private async Task<bool> EnsureReadyForPrivateRoomCreateAsync()
        {
            EnsureReferences();
            if (networkClient == null || joinRequestInFlight)
            {
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
                ShowLobbyHome();
                return;
            }

            if (networkClient.IsConnected && networkClient.IsInRoom)
            {
                await LeaveCurrentRoomIfNeededAsync();
            }

            dismissPrivateRoomRequested = false;
            ShowLobbyHome();
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
                Debug.LogWarning($"StayAliveLobbyManager: Leave room request failed. {exception.Message}");
                return;
            }

            float timeoutAt = Time.realtimeSinceStartup + 2f;
            while (networkClient.IsInRoom && Time.realtimeSinceStartup < timeoutAt)
            {
                await Task.Yield();
            }
        }

        private async void RestartQuickPlayOnline()
        {
            ShowRandomWaitingPanel();
            await LeaveCurrentRoomIfNeededAsync();
            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        private void ShowLobbyHome()
        {
            HideRoomPanel();
            HideGameEnd();
            HideWaiting();
            HideNetworkIssue();

            if (popupManager != null)
            {
                popupManager.HideAllPopups();
            }

            ShowLobbyVisualState();
        }

        private void ShowRandomWaitingPanel()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            HideRoomPanel();
            HideGameEnd();
            HideNetworkIssue();
            ShowLobbyVisualState();

            if (waitingForPlayersPanel != null)
            {
                waitingForPlayersPanel.SetActive(true);
                return;
            }

            if (popupManager != null)
            {
                popupManager.ShowWaitingForPlayersPopup();
            }
        }

        private void ShowPrivateWaitingPanel(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            roomPanelMode = isHost ? RoomPanelMode.PrivateWaitingHost : RoomPanelMode.PrivateWaitingGuest;
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            currentRoomId = roomId ?? string.Empty;
            ShowLobbyVisualState();

            if (popupManager != null)
            {
                popupManager.ShowPrivateRoomWaitingPopup(isHost, currentRoomId, currentOccupiedSeats, currentCapacity);
                return;
            }

            SetPopup(joinRoomPopupPanel, false);
            SetPopup(createRoomPopupPanel, true);
            SetComponentText(createRoomIdLabel, roomId ?? string.Empty);
            RefreshPrivateWaitingUi();
        }

        private void RefreshPrivateWaitingUi()
        {
            if (popupManager != null
                && (roomPanelMode == RoomPanelMode.PrivateWaitingHost || roomPanelMode == RoomPanelMode.PrivateWaitingGuest))
            {
                popupManager.ShowPrivateRoomWaitingPopup(
                    roomPanelMode == RoomPanelMode.PrivateWaitingHost,
                    currentRoomId,
                    currentOccupiedSeats,
                    currentCapacity);
            }

            SetComponentText(createRoomOccupancyLabel, $"{currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)} joined");

            if (createRoomStartButton == null)
            {
                return;
            }

            bool canStart = roomPanelMode == RoomPanelMode.PrivateWaitingHost
                && onlineMatchController != null
                && onlineMatchController.CanHostStartMatch
                && currentOccupiedSeats >= 2;
            createRoomStartButton.interactable = canStart;
        }

        private void HideRoomPanel()
        {
            roomPanelMode = RoomPanelMode.Hidden;
            currentRoomId = string.Empty;

            if (popupManager != null)
            {
                popupManager.HideLobbyPopup();
            }

            SetPopup(joinRoomPopupPanel, false);
            SetPopup(createRoomPopupPanel, false);
        }

        private void ShowGameEnd(int winningChairId)
        {
            if (!IsPrimaryLobbyManager || showGameEndCoroutine != null)
            {
                return;
            }

            showGameEndCoroutine = StartCoroutine(ShowGameEndAfterDelay(winningChairId));
        }

        private System.Collections.IEnumerator ShowGameEndAfterDelay(int winningChairId)
        {
            float delay = Mathf.Max(0f, gameEndDelaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            showGameEndCoroutine = null;
            if (gameEndPanel != null)
            {
                gameEndPanel.SetActive(true);
            }

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(false);
            }

            SetWinnerPanel(winningChairId);
            HideWaiting();
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

            if (gameEndPanel != null)
            {
                gameEndPanel.SetActive(false);
            }

            SetWinnerPanels(false, false, false, false);
        }

        private void HideWaiting()
        {
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

            string resolvedMessage = string.IsNullOrWhiteSpace(message) ? networkIssueConnectMessage : message;
            bool showOnLobbyCanvas = ShouldShowNetworkIssueOnLobbyCanvas();

            if (showOnLobbyCanvas)
            {
                ShowLobbyVisualState();
            }

            if (popupManager != null)
            {
                popupManager.ShowNetworkErrorPopup(resolvedMessage, preferOverlay: !showOnLobbyCanvas);
            }

            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(true);
            }

            SetComponentText(networkIssueTitleLabel, networkIssueTitle);
            SetComponentText(networkIssueMessageLabel, resolvedMessage);
        }

        private bool ShouldShowNetworkIssueOnLobbyCanvas()
        {
            if (roomPanelMode != RoomPanelMode.Hidden)
            {
                return true;
            }

            if (popupManager == null)
            {
                return gameContainer == null || !gameContainer.activeInHierarchy;
            }

            switch (popupManager.LobbyIntent)
            {
                case StayAlivePopupIntent.JoinRoom:
                case StayAlivePopupIntent.CreateRoom:
                case StayAlivePopupIntent.WaitingForPlayers:
                case StayAlivePopupIntent.PrivateRoomWaitingHost:
                case StayAlivePopupIntent.PrivateRoomWaitingGuest:
                    return true;

                default:
                    return gameContainer == null || !gameContainer.activeInHierarchy;
            }
        }

        private void ShowLobbyVisualState()
        {
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

        private void HideNetworkIssue()
        {
            if (popupManager != null)
            {
                popupManager.HidePopupByIntent(StayAlivePopupIntent.NetworkError);
            }

            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(false);
            }
        }

        private void SetWinnerPanel(int winningChairId)
        {
            int appearanceIndex = ResolveWinningAppearanceIndex(winningChairId);
            SetWinnerPanels(
                blue: appearanceIndex == 1,
                green: appearanceIndex == 2,
                red: appearanceIndex == 0,
                yellow: appearanceIndex == 3);
        }

        private int ResolveWinningAppearanceIndex(int winningChairId)
        {
            if (gameManager != null)
            {
                StayAlivePlayer winningPlayer = gameManager.GetPlayerByChair(winningChairId);
                if (winningPlayer != null)
                {
                    return Mathf.Max(0, winningPlayer.AppearanceIndex % 4);
                }
            }

            return Mathf.Clamp(winningChairId, 0, 3);
        }

        private void SetWinnerPanels(bool blue, bool green, bool red, bool yellow)
        {
            if (blueWinnerPanel != null) blueWinnerPanel.SetActive(blue);
            if (greenWinnerPanel != null) greenWinnerPanel.SetActive(green);
            if (redWinnerPanel != null) redWinnerPanel.SetActive(red);
            if (yellowWinnerPanel != null) yellowWinnerPanel.SetActive(yellow);
        }

        private void HandleGameEnded(int winningChairId)
        {
            ShowGameEnd(winningChairId);
        }

        private void HandleMatchEnded(int winningChairId)
        {
            ShowGameEnd(winningChairId);
        }

        private void StartGameLocalFlow()
        {
            StartGameLocalFlow(offlineTotalPlayers, offlineHumanPlayers);
        }

        private void StartGameLocalFlow(int totalPlayers, int humanPlayers)
        {
            lastReplayMode = ReplayMode.OfflineBots;
            lastOfflineTotalPlayers = Mathf.Max(2, totalPlayers);
            lastOfflineHumanPlayers = Mathf.Clamp(humanPlayers, 1, lastOfflineTotalPlayers);

            HideRoomPanel();
            HideGameEnd();
            HideWaiting();
            HideNetworkIssue();

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

            gameManager?.StartGameLocal(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
        }

        private void EnsureReferences()
        {
            if (lobbyCanvas == null)
            {
                Transform found = FindInActiveScene("LobbyCanvas");
                if (found != null) lobbyCanvas = found.gameObject;
            }

            if (lobbyRoot == null)
            {
                Transform found = FindUnderLobbyCanvas("Lobby");
                if (found != null) lobbyRoot = found.gameObject;
            }

            if (gameEndPanel == null)
            {
                Transform found = FindUnderLobbyCanvas("GameEnd");
                if (found != null) gameEndPanel = found.gameObject;
            }

            if (waitingForPlayersPanel == null)
            {
                Transform found = FindUnderLobbyCanvas("WaitingForPlayers");
                if (found != null) waitingForPlayersPanel = found.gameObject;
            }

            if (networkIssueRoot == null)
            {
                Transform found = FindUnderLobbyCanvas("NetworkIssue");
                if (found != null) networkIssueRoot = found.gameObject;
            }

            if (joinRoomPopupPanel == null)
            {
                Transform found = FindInActiveScene("JoinPopUpPanel");
                if (found != null) joinRoomPopupPanel = found.gameObject;
            }

            if (createRoomPopupPanel == null)
            {
                Transform found = FindInActiveScene("CreatePopUpPanel");
                if (found != null) createRoomPopupPanel = found.gameObject;
            }

            if (roomIdInputField == null)
            {
                Transform found = FindInActiveScene("RoomIdInputField");
                if (found != null) roomIdInputField = FindComponentByTypeName(found.gameObject, "TMP_InputField");
            }

            if (roomPopupPrimaryButton == null && joinRoomPopupPanel != null)
            {
                Transform found = FindInChildren(joinRoomPopupPanel.transform, "Join");
                if (found != null) roomPopupPrimaryButton = found.GetComponent<Button>();
            }

            if (roomPopupActionLabel == null && joinRoomPopupPanel != null)
            {
                Transform found = FindInChildren(joinRoomPopupPanel.transform, "Join");
                if (found != null) roomPopupActionLabel = FindTextComponent(found.gameObject);
            }

            if (createRoomIdLabel == null && createRoomPopupPanel != null)
            {
                Transform found = FindInChildren(createRoomPopupPanel.transform, "RoomID");
                if (found != null) createRoomIdLabel = FindTextComponent(found.gameObject);
            }

            if (createRoomOccupancyLabel == null && createRoomPopupPanel != null)
            {
                Transform found = FindInChildren(createRoomPopupPanel.transform, "JoinedPlayersCount");
                if (found != null) createRoomOccupancyLabel = FindTextComponent(found.gameObject);
            }

            if (createRoomStartButton == null && createRoomPopupPanel != null)
            {
                Transform found = FindInChildren(createRoomPopupPanel.transform, "Start");
                if (found != null) createRoomStartButton = found.GetComponent<Button>();
            }

            if (networkIssueTitleLabel == null && networkIssueRoot != null)
            {
                Transform found = FindInChildren(networkIssueRoot.transform, "NetworkError");
                if (found != null) networkIssueTitleLabel = FindTextComponent(found.gameObject);
            }

            if (networkIssueMessageLabel == null && networkIssueRoot != null)
            {
                Transform found = FindInChildren(networkIssueRoot.transform, "Isssue");
                if (found != null) networkIssueMessageLabel = FindTextComponent(found.gameObject);
            }

            if (networkIssueCloseButton == null && networkIssueRoot != null)
            {
                Transform found = FindInChildren(networkIssueRoot.transform, "Close");
                if (found != null) networkIssueCloseButton = found.GetComponent<Button>();
            }

            if (blueWinnerPanel == null) blueWinnerPanel = FindNamedGameObjectUnderGameEnd("BlueWinner");
            if (greenWinnerPanel == null) greenWinnerPanel = FindNamedGameObjectUnderGameEnd("GreenWinner");
            if (redWinnerPanel == null) redWinnerPanel = FindNamedGameObjectUnderGameEnd("RedWinner");
            if (yellowWinnerPanel == null) yellowWinnerPanel = FindNamedGameObjectUnderGameEnd("YellowWinner");

            if (playAgainButton == null)
            {
                GameObject found = FindNamedGameObjectUnderGameEnd("PlayAgain");
                if (found != null) playAgainButton = found.GetComponent<Button>();
            }

            if (backToHomeButton == null)
            {
                GameObject found = FindNamedGameObjectUnderGameEnd("BackToHome");
                if (found != null) backToHomeButton = found.GetComponent<Button>();
            }

            if (gameManager == null)
            {
                gameManager = FindFirstObjectByType<StayAliveGameManager>(FindObjectsInactive.Include);
            }

            if (popupManager == null)
            {
                popupManager = GetComponent<StayAlivePopupManager>();
            }

            if (useNetworkLobby && networkClient == null)
            {
                networkClient = GetComponent<StayAliveLobbyNetworkClient>();
            }

            if (useNetworkLobby && onlineMatchController == null)
            {
                onlineMatchController = GetComponent<StayAliveOnlineMatchController>();
            }
        }

        private void InitializeOnlineMatchController()
        {
            if (!useNetworkLobby || onlineMatchController == null || networkClient == null || gameManager == null)
            {
                return;
            }

            onlineMatchController.Initialize(gameManager, lobbyCanvas, gameContainer, networkClient);
        }

        private void SubscribeToGameEvents()
        {
            if (subscribedToGameEvents)
            {
                return;
            }

            if (gameManager != null) gameManager.GameEnded += HandleGameEnded;
            if (onlineMatchController != null) onlineMatchController.MatchEnded += HandleMatchEnded;
            subscribedToGameEvents = true;
        }

        private void UnsubscribeFromGameEvents()
        {
            if (!subscribedToGameEvents)
            {
                return;
            }

            if (gameManager != null) gameManager.GameEnded -= HandleGameEnded;
            if (onlineMatchController != null) onlineMatchController.MatchEnded -= HandleMatchEnded;
            subscribedToGameEvents = false;
        }

        private void SubscribeToPopupEvents()
        {
            if (subscribedToPopupEvents || popupManager == null)
            {
                return;
            }

            popupManager.ButtonClicked += HandlePopupButtonClicked;
            subscribedToPopupEvents = true;
        }

        private void UnsubscribeFromPopupEvents()
        {
            if (!subscribedToPopupEvents || popupManager == null)
            {
                return;
            }

            popupManager.ButtonClicked -= HandlePopupButtonClicked;
            subscribedToPopupEvents = false;
        }

        private void SubscribeToNetworkEvents()
        {
            if (!useNetworkLobby || networkClient == null)
            {
                return;
            }

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

            networkClient.JoinedRoom -= HandleJoinedRoom;
            networkClient.RoomOccupancyChanged -= HandleRoomOccupancyChanged;
            networkClient.RoomReady -= HandleRoomReady;
            networkClient.BackendErrorReceived -= HandleBackendError;
            networkClient.ConnectionError -= HandleConnectionError;
            networkClient.Disconnected -= HandleDisconnected;
        }

        private void HandleJoinedRoom(StayAliveJoinedMessage joined)
        {
            joinRequestInFlight = false;
            currentCapacity = joined.capacity;
            currentOccupiedSeats = CountOccupiedSeats(joined.chairs);

            if (dismissPrivateRoomRequested && networkClient != null && networkClient.RoomMode == StayAliveRoomModes.Private)
            {
                DismissJoinedPrivateRoomAfterClose();
                return;
            }

            if (networkClient != null && networkClient.RoomMode == StayAliveRoomModes.Private)
            {
                ShowPrivateWaitingPanel(networkClient.IsHost, joined.roomId, currentOccupiedSeats, currentCapacity);
                return;
            }

            ShowRandomWaitingPanel();
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            RefreshPrivateWaitingUi();
        }

        private void HandleRoomReady(string[] chairs)
        {
        }

        private void HandleBackendError(StayAliveErrorMessage error)
        {
            joinRequestInFlight = false;
            ShowNetworkIssue(error != null ? error.message : networkIssueConnectMessage);
        }

        private void HandleConnectionError(string errorMessage)
        {
            joinRequestInFlight = false;
            ShowNetworkIssue(string.IsNullOrWhiteSpace(errorMessage) ? networkIssueConnectMessage : errorMessage);
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            joinRequestInFlight = false;
            HideRoomPanel();
            ShowNetworkIssue(networkIssueDisconnectMessage);
        }

        private async void DismissJoinedPrivateRoomAfterClose()
        {
            await LeaveCurrentRoomIfNeededAsync();
            dismissPrivateRoomRequested = false;
            ShowLobbyHome();
        }

        private void HandlePopupButtonClicked(StayAlivePopupCanvasType canvasType, StayAlivePopupIntent intent, StayAlivePopupButtonType buttonType)
        {
            if (buttonType == StayAlivePopupButtonType.Primary)
            {
                HandlePopupPrimaryAction(intent);
                return;
            }

            HandlePopupCloseAction(intent, canvasType);
        }

        private void HandlePopupPrimaryAction(StayAlivePopupIntent intent)
        {
            switch (intent)
            {
                case StayAlivePopupIntent.JoinRoom:
                case StayAlivePopupIntent.CreateRoom:
                case StayAlivePopupIntent.PrivateRoomWaitingHost:
                    OnRoomPopupConfirmButtonClicked();
                    break;

                case StayAlivePopupIntent.NetworkError:
                    OnCloseNetworkIssueButtonClicked();
                    break;
            }
        }

        private void HandlePopupCloseAction(StayAlivePopupIntent intent, StayAlivePopupCanvasType canvasType)
        {
            switch (intent)
            {
                case StayAlivePopupIntent.JoinRoom:
                    OnCloseJoinRoomPopupClicked();
                    break;

                case StayAlivePopupIntent.CreateRoom:
                case StayAlivePopupIntent.PrivateRoomWaitingHost:
                case StayAlivePopupIntent.PrivateRoomWaitingGuest:
                    OnCloseCreateRoomPopupClicked();
                    break;

                case StayAlivePopupIntent.WaitingForPlayers:
                    ShowLobbyHome();
                    break;

                case StayAlivePopupIntent.NetworkError:
                    OnCloseNetworkIssueButtonClicked();
                    break;

                case StayAlivePopupIntent.Custom:
                    if (canvasType == StayAlivePopupCanvasType.Lobby)
                    {
                        popupManager?.HideLobbyPopup();
                    }
                    else
                    {
                        popupManager?.HideOverlayPopup();
                    }

                    break;
            }
        }

        private void RegisterButtons()
        {
            if (playAgainButton != null) playAgainButton.onClick.AddListener(OnPlayAgainButtonClicked);
            if (backToHomeButton != null) backToHomeButton.onClick.AddListener(OnBackToHomeButtonClicked);
            if (networkIssueCloseButton != null) networkIssueCloseButton.onClick.AddListener(OnCloseNetworkIssueButtonClicked);
        }

        private void UnregisterButtons()
        {
            if (playAgainButton != null) playAgainButton.onClick.RemoveListener(OnPlayAgainButtonClicked);
            if (backToHomeButton != null) backToHomeButton.onClick.RemoveListener(OnBackToHomeButtonClicked);
            if (networkIssueCloseButton != null) networkIssueCloseButton.onClick.RemoveListener(OnCloseNetworkIssueButtonClicked);
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

        private GameObject FindNamedGameObjectUnderGameEnd(string name)
        {
            if (gameEndPanel == null)
            {
                return null;
            }

            Transform found = FindInChildren(gameEndPanel.transform, name);
            return found != null ? found.gameObject : null;
        }

        private Transform FindUnderLobbyCanvas(string name)
        {
            if (lobbyCanvas != null)
            {
                return FindInChildren(lobbyCanvas.transform, name);
            }

            return FindInActiveScene(name);
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
                if (component == null)
                {
                    continue;
                }

                Type componentType = component.GetType();
                if (componentType.Name == typeName || componentType.FullName == typeName)
                {
                    return component;
                }
            }

            return target.GetComponent(typeName);
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

                if (component is Text)
                {
                    return component;
                }

                PropertyInfo property = GetProperty(component.GetType(), "text");
                if (property != null && property.PropertyType == typeof(string) && property.CanRead)
                {
                    return component;
                }
            }

            return null;
        }

        private static void SetPopup(GameObject target, bool isVisible)
        {
            if (target != null)
            {
                target.SetActive(isVisible);
            }
        }

        private string GetRoomIdText()
        {
            string value = GetComponentText(roomIdInputField);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private void SetRoomIdText(string value)
        {
            SetComponentText(roomIdInputField, value ?? string.Empty);
        }

        private void SetRoomIdReadOnly(bool isReadOnly)
        {
            SetComponentBoolProperty(roomIdInputField, "readOnly", isReadOnly);
        }

        private static string GetComponentText(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            if (component is Text text)
            {
                return text.text;
            }

            PropertyInfo property = GetProperty(component.GetType(), "text");
            if (property != null && property.PropertyType == typeof(string) && property.CanRead)
            {
                object value = property.GetValue(component, null);
                return value as string ?? string.Empty;
            }

            return string.Empty;
        }

        private static void SetComponentText(Component component, string value)
        {
            if (component == null)
            {
                return;
            }

            if (component is Text text)
            {
                text.text = value ?? string.Empty;
                return;
            }

            PropertyInfo property = GetProperty(component.GetType(), "text");
            if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
            {
                property.SetValue(component, value ?? string.Empty, null);
            }
        }

        private static void SetComponentBoolProperty(Component component, string propertyName, bool value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            PropertyInfo property = GetProperty(component.GetType(), propertyName);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                property.SetValue(component, value, null);
            }
        }

        private static PropertyInfo GetProperty(Type type, string propertyName)
        {
            return type != null
                ? type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            EnsureReferences();

            if (!useNetworkLobby)
            {
                return;
            }

            if (networkClient == null)
            {
                networkClient = GetComponent<StayAliveLobbyNetworkClient>();
            }

            if (onlineMatchController == null)
            {
                onlineMatchController = GetComponent<StayAliveOnlineMatchController>();
            }

            if (popupManager == null)
            {
                popupManager = GetComponent<StayAlivePopupManager>();
            }

            if (networkClient == null)
            {
                networkClient = gameObject.AddComponent<StayAliveLobbyNetworkClient>();
                EditorUtility.SetDirty(gameObject);
            }

            if (onlineMatchController == null)
            {
                onlineMatchController = gameObject.AddComponent<StayAliveOnlineMatchController>();
                EditorUtility.SetDirty(gameObject);
            }

            if (popupManager == null)
            {
                popupManager = gameObject.AddComponent<StayAlivePopupManager>();
                EditorUtility.SetDirty(gameObject);
            }
        }
#endif
    }
}
