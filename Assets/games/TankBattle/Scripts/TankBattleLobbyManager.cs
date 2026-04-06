using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TankBattle.Networking;

namespace TankBattle.Lobby
{
    public class TankBattleLobbyManager : MonoBehaviour
    {
        private enum RoomPanelMode
        {
            Hidden = 0,
            QuickPlayWaiting = 1,
            JoinEntry = 2,
            PrivateWaitingHost = 3,
            PrivateWaitingGuest = 4
        }

        [Header("References")]
        [SerializeField] private TankBattleGameManager gameManager;
        [SerializeField] private GameObject lobbyCanvas;

        [Header("Network")]
        [SerializeField] private bool useNetworkLobby = true;
        [SerializeField] private TankBattleLobbyNetworkClient networkClient;
        [SerializeField] private TankBattleOnlineMatchController onlineMatchController;

        [Header("Offline")]
        [SerializeField, Min(2)] private int offlineTotalPlayers = 2;
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;

        [Header("Labels")]
        [SerializeField] private string joinRoomConfirmLabel = "Join";
        [SerializeField] private string waitingLabel = "Waiting...";
        [SerializeField] private string hostStartLabel = "Start";
        [SerializeField] private string quickPlayWaitingMessage = "Finding player...";
        [SerializeField] private string quickPlayFoundMessage = "Player matched. Starting soon...";
        [SerializeField] private string networkIssueTitle = "Network Error";
        [SerializeField] private string networkIssueConnectMessage = "Unable to connect to server. Please try again.";
        [SerializeField] private string networkIssueDisconnectMessage = "Connection lost. Please try again.";

        private GameObject joinRoomPopupPanel;
        private TMP_InputField roomIdInputField;
        private Text joinActionLabel;
        private Text joinStatusLabel;
        private Button joinConfirmButton;

        private GameObject createRoomPopupPanel;
        private Text createRoomTitleLabel;
        private Text createRoomIdLabel;
        private Text createRoomOccupancyLabel;
        private Button createRoomStartButton;

        private GameObject networkIssueRoot;
        private Text networkIssueTitleLabel;
        private Text networkIssueMessageLabel;
        private Button networkIssueCloseButton;

        private Button playWithBotButton;
        private Button playOnlineButton;
        private Button createButton;
        private Button joinButton;

        private RoomPanelMode roomPanelMode;
        private bool joinRequestInFlight;
        private bool dismissPrivateRoomRequested;
        private bool privateRoomDismissInFlight;
        private int currentOccupiedSeats;
        private int currentCapacity;

        private void Awake()
        {
            if (gameManager == null)
            {
                gameManager = GetComponent<TankBattleGameManager>();
            }

            EnsureUiReferences();
            EnsureNetworkComponents();
        }

        private void Start()
        {
            EnsureUiReferences();
            RegisterButtons();

            if (useNetworkLobby && (networkClient == null || onlineMatchController == null))
            {
                Debug.LogError("TankBattleLobbyManager: Missing network components.");
                useNetworkLobby = false;
            }

            SubscribeNetworkEvents();
            InitializeOnlineMatchController();
            ShowLobbyHome();
        }

        private void OnDestroy()
        {
            UnregisterButtons();
            UnsubscribeNetworkEvents();
        }

        public async void OnPlayOnlineButtonClicked()
        {
            if (!useNetworkLobby)
            {
                StartGameLocalFlow();
                return;
            }

            dismissPrivateRoomRequested = false;
            ShowQuickPlayWaitingPanel(quickPlayWaitingMessage);
            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        public void OnPlayWithBotsButtonClicked()
        {
            if (useNetworkLobby && networkClient != null && networkClient.IsConnected && networkClient.IsInRoom)
            {
                _ = LeaveCurrentRoomIfNeededAsync();
            }

            StartGameLocalFlow(
                Mathf.Max(2, offlineTotalPlayers),
                Mathf.Clamp(offlineHumanPlayers, 1, Mathf.Max(2, offlineTotalPlayers)));
        }

        public void OnOpenJoinRoomPopupClicked()
        {
            ShowJoinEntryPanel();
        }

        public async void OnOpenCreateRoomPopupClicked()
        {
            if (!useNetworkLobby)
            {
                return;
            }

            if (!await EnsureReadyForPrivateRoomCreateAsync())
            {
                return;
            }

            dismissPrivateRoomRequested = false;
            ShowPrivateWaitingPanel(isHost: true, roomId: "Creating...", occupiedSeats: 1, capacity: Mathf.Max(2, networkClient.RoomCapacity));
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
                    if (onlineMatchController != null)
                    {
                        onlineMatchController.StartHostedMatchNow();
                    }

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

            if (roomPanelMode == RoomPanelMode.QuickPlayWaiting)
            {
                CancelQuickPlayAsync();
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

        public void OnCloseNetworkIssueButtonClicked()
        {
            ShowLobbyHome();
        }

        private async void SubmitJoinRoomRequest()
        {
            string roomId = roomIdInputField != null ? roomIdInputField.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(roomId))
            {
                return;
            }

            dismissPrivateRoomRequested = false;
            await BeginOnlineJoinAsync(roomId, createRoom: false, usePrivateRoom: true);
        }

        private async Task BeginOnlineJoinAsync(string roomId, bool createRoom, bool usePrivateRoom)
        {
            if (!useNetworkLobby || networkClient == null)
            {
                return;
            }

            if (joinRequestInFlight)
            {
                return;
            }

            joinRequestInFlight = true;
            SetHomeButtonsInteractable(false);

            try
            {
                if (!networkClient.IsConnected)
                {
                    await networkClient.ConnectAsync();
                }

                if (networkClient.IsInRoom && (createRoom || usePrivateRoom))
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
                SetHomeButtonsInteractable(true);
                Debug.LogError($"TankBattleLobbyManager: Failed to join room. {exception.Message}");
                ShowNetworkIssue(networkIssueConnectMessage);
            }
        }

        private void ShowJoinEntryPanel()
        {
            roomPanelMode = RoomPanelMode.JoinEntry;
            HideCreateRoomPopup();
            SetJoinRoomPopupVisible(true);
            SetJoinInputInteractable(true);
            SetJoinFieldText(string.Empty);
            SetJoinStatusText("Enter Room ID");
            SetJoinActionLabel(joinRoomConfirmLabel);
            SetJoinConfirmButtonInteractable(true);
            SetHomeButtonsInteractable(true);
        }

        private void ShowQuickPlayWaitingPanel(string status)
        {
            roomPanelMode = RoomPanelMode.QuickPlayWaiting;
            HideCreateRoomPopup();
            SetJoinRoomPopupVisible(true);
            SetJoinInputInteractable(false);
            SetJoinFieldText(string.Empty);
            SetJoinStatusText(string.IsNullOrWhiteSpace(status) ? quickPlayWaitingMessage : status);
            SetJoinActionLabel(waitingLabel);
            SetJoinConfirmButtonInteractable(false);
        }

        private void ShowPrivateWaitingPanel(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            roomPanelMode = isHost ? RoomPanelMode.PrivateWaitingHost : RoomPanelMode.PrivateWaitingGuest;
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            HideJoinRoomPopup();
            SetCreateRoomPopupVisible(true);
            if (createRoomTitleLabel != null)
            {
                createRoomTitleLabel.text = "Room Created";
            }

            if (createRoomIdLabel != null)
            {
                createRoomIdLabel.text = roomId ?? string.Empty;
            }

            RefreshPrivateWaitingUi();
        }

        private void RefreshPrivateWaitingUi()
        {
            if (createRoomOccupancyLabel != null)
            {
                createRoomOccupancyLabel.text = $"{currentOccupiedSeats}/{Mathf.Max(1, currentCapacity)} joined";
            }

            if (createRoomStartButton == null)
            {
                return;
            }

            bool isHostWaiting = roomPanelMode == RoomPanelMode.PrivateWaitingHost;
            bool canStart = isHostWaiting && onlineMatchController != null && onlineMatchController.CanHostStartMatch;
            createRoomStartButton.interactable = canStart;
            SetButtonLabel(createRoomStartButton, isHostWaiting ? hostStartLabel : waitingLabel);
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
            if (joinRoomPopupPanel != null)
            {
                joinRoomPopupPanel.SetActive(visible);
            }
        }

        private void SetCreateRoomPopupVisible(bool visible)
        {
            if (createRoomPopupPanel != null)
            {
                createRoomPopupPanel.SetActive(visible);
            }
        }

        private void SetJoinInputInteractable(bool interactable)
        {
            if (roomIdInputField == null)
            {
                return;
            }

            roomIdInputField.interactable = interactable;
            roomIdInputField.readOnly = !interactable;
        }

        private void SetJoinFieldText(string value)
        {
            if (roomIdInputField != null)
            {
                roomIdInputField.text = value ?? string.Empty;
            }
        }

        private void SetJoinStatusText(string text)
        {
            if (joinStatusLabel != null)
            {
                joinStatusLabel.text = text ?? string.Empty;
            }
        }

        private void SetJoinActionLabel(string text)
        {
            if (joinActionLabel != null)
            {
                joinActionLabel.text = text ?? string.Empty;
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            Text textComponent = button.GetComponentInChildren<Text>(true);
            if (textComponent != null)
            {
                textComponent.text = label ?? string.Empty;
            }
        }

        private void SetJoinConfirmButtonInteractable(bool interactable)
        {
            if (joinConfirmButton != null)
            {
                joinConfirmButton.interactable = interactable;
            }
        }

        private void SetHomeButtonsInteractable(bool interactable)
        {
            if (playWithBotButton != null)
            {
                playWithBotButton.interactable = interactable;
            }

            if (playOnlineButton != null)
            {
                playOnlineButton.interactable = interactable;
            }

            if (createButton != null)
            {
                createButton.interactable = interactable;
            }

            if (joinButton != null)
            {
                joinButton.interactable = interactable;
            }
        }

        private void ShowLobbyHome()
        {
            dismissPrivateRoomRequested = false;
            HideRoomPanel();
            HideNetworkIssue();
            SetHomeButtonsInteractable(true);

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (gameManager != null)
            {
                gameManager.ShowLobbyState();
            }
        }

        private void StartGameLocalFlow()
        {
            StartGameLocalFlow(2, 1);
        }

        private void StartGameLocalFlow(int totalPlayers, int humanPlayers)
        {
            HideRoomPanel();
            HideNetworkIssue();
            SetHomeButtonsInteractable(true);

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
            }

            if (gameManager != null)
            {
                gameManager.StartGameLocal(totalPlayers, humanPlayers);
            }
        }

        private void EnsureNetworkComponents()
        {
            if (!useNetworkLobby)
            {
                return;
            }

            if (networkClient == null)
            {
                networkClient = GetComponent<TankBattleLobbyNetworkClient>();
            }

            if (networkClient == null)
            {
                networkClient = gameObject.AddComponent<TankBattleLobbyNetworkClient>();
            }

            if (onlineMatchController == null)
            {
                onlineMatchController = GetComponent<TankBattleOnlineMatchController>();
            }

            if (onlineMatchController == null)
            {
                onlineMatchController = gameObject.AddComponent<TankBattleOnlineMatchController>();
            }
        }

        private void InitializeOnlineMatchController()
        {
            if (!useNetworkLobby || onlineMatchController == null || networkClient == null || gameManager == null)
            {
                return;
            }

            onlineMatchController.Initialize(gameManager, lobbyCanvas, null, networkClient);
        }

        private void SubscribeNetworkEvents()
        {
            if (!useNetworkLobby || networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom += HandleJoinedRoom;
            networkClient.RoomOccupancyChanged += HandleRoomOccupancyChanged;
            networkClient.BackendErrorReceived += HandleBackendError;
            networkClient.ConnectionError += HandleConnectionError;
            networkClient.Disconnected += HandleDisconnected;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom -= HandleJoinedRoom;
            networkClient.RoomOccupancyChanged -= HandleRoomOccupancyChanged;
            networkClient.BackendErrorReceived -= HandleBackendError;
            networkClient.ConnectionError -= HandleConnectionError;
            networkClient.Disconnected -= HandleDisconnected;
        }

        private void HandleJoinedRoom(TankBattleJoinedMessage joined)
        {
            joinRequestInFlight = false;
            currentCapacity = joined.capacity;
            currentOccupiedSeats = CountOccupiedSeats(joined.chairs);

            if (dismissPrivateRoomRequested && networkClient.RoomMode == TankBattleRoomModes.Private)
            {
                DismissJoinedPrivateRoomAfterClose();
                return;
            }

            if (networkClient.RoomMode == TankBattleRoomModes.Private)
            {
                ShowPrivateWaitingPanel(networkClient.IsHost, joined.roomId, currentOccupiedSeats, currentCapacity);
                return;
            }

            ShowQuickPlayWaitingPanel(quickPlayFoundMessage);
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            RefreshPrivateWaitingUi();
        }

        private void HandleBackendError(TankBattleErrorMessage error)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            SetHomeButtonsInteractable(true);
            HideRoomPanel();
            ShowNetworkIssue(error != null && !string.IsNullOrWhiteSpace(error.message) ? error.message : networkIssueConnectMessage);
        }

        private void HandleConnectionError(string errorMessage)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            SetHomeButtonsInteractable(true);
            ShowNetworkIssue(string.IsNullOrWhiteSpace(errorMessage) ? networkIssueConnectMessage : errorMessage);
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            HideRoomPanel();
            SetHomeButtonsInteractable(true);
            ShowNetworkIssue(networkIssueDisconnectMessage);
        }

        private async void CancelQuickPlayAsync()
        {
            await LeaveCurrentRoomIfNeededAsync();
            ShowLobbyHome();
        }

        private async void DismissJoinedPrivateRoomAfterClose()
        {
            await LeaveCurrentRoomIfNeededAsync();
            dismissPrivateRoomRequested = false;
            ShowLobbyHome();
        }

        private async Task<bool> EnsureReadyForPrivateRoomCreateAsync()
        {
            if (!useNetworkLobby || networkClient == null)
            {
                return false;
            }

            if (privateRoomDismissInFlight || joinRequestInFlight)
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
                Debug.LogWarning($"TankBattleLobbyManager: Leave room request failed. {exception.Message}");
                return;
            }

            float timeoutAt = Time.realtimeSinceStartup + 2f;
            while (networkClient.IsInRoom && Time.realtimeSinceStartup < timeoutAt)
            {
                await Task.Yield();
            }
        }

        private void ShowNetworkIssue(string message)
        {
            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(true);
            }

            if (networkIssueTitleLabel != null)
            {
                networkIssueTitleLabel.text = networkIssueTitle;
            }

            if (networkIssueMessageLabel != null)
            {
                networkIssueMessageLabel.text = string.IsNullOrWhiteSpace(message) ? networkIssueConnectMessage : message;
            }
        }

        private void HideNetworkIssue()
        {
            if (networkIssueRoot != null)
            {
                networkIssueRoot.SetActive(false);
            }
        }

        private void RegisterButtons()
        {
            playWithBotButton = FindButton("PlayWBot");
            playOnlineButton = FindButton("PlayOnline");
            createButton = FindButton("Create");
            joinButton = FindButton("Join", parentName: "Background");

            AddButtonListener(playWithBotButton, OnPlayWithBotsButtonClicked);
            AddButtonListener(playOnlineButton, OnPlayOnlineButtonClicked);
            AddButtonListener(createButton, OnOpenCreateRoomPopupClicked);
            AddButtonListener(joinButton, OnOpenJoinRoomPopupClicked);

            AddButtonListener(joinConfirmButton, OnRoomPopupConfirmButtonClicked);
            AddButtonListener(createRoomStartButton, OnRoomPopupConfirmButtonClicked);

            AddButtonListener(FindButton("Close", parentName: "JoinPopUp"), OnCloseJoinRoomPopupClicked);
            AddButtonListener(FindButton("Close", parentName: "CreatePopUp"), OnCloseCreateRoomPopupClicked);

            if (networkIssueCloseButton != null)
            {
                AddButtonListener(networkIssueCloseButton, OnCloseNetworkIssueButtonClicked);
            }
        }

        private void UnregisterButtons()
        {
            RemoveButtonListener(playWithBotButton, OnPlayWithBotsButtonClicked);
            RemoveButtonListener(playOnlineButton, OnPlayOnlineButtonClicked);
            RemoveButtonListener(createButton, OnOpenCreateRoomPopupClicked);
            RemoveButtonListener(joinButton, OnOpenJoinRoomPopupClicked);

            RemoveButtonListener(joinConfirmButton, OnRoomPopupConfirmButtonClicked);
            RemoveButtonListener(createRoomStartButton, OnRoomPopupConfirmButtonClicked);

            RemoveButtonListener(FindButton("Close", parentName: "JoinPopUp"), OnCloseJoinRoomPopupClicked);
            RemoveButtonListener(FindButton("Close", parentName: "CreatePopUp"), OnCloseCreateRoomPopupClicked);

            if (networkIssueCloseButton != null)
            {
                RemoveButtonListener(networkIssueCloseButton, OnCloseNetworkIssueButtonClicked);
            }
        }

        private static void AddButtonListener(Button button, UnityAction handler)
        {
            if (button == null || handler == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        private static void RemoveButtonListener(Button button, UnityAction handler)
        {
            if (button == null || handler == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
        }

        private void EnsureUiReferences()
        {
            if (lobbyCanvas == null)
            {
                GameObject lobby = FindGameObjectByName("LobbyCanvas");
                if (lobby != null)
                {
                    lobbyCanvas = lobby;
                }
            }

            if (joinRoomPopupPanel == null)
            {
                joinRoomPopupPanel = FindGameObjectByName("JoinPopUpPanel");
            }

            if (createRoomPopupPanel == null)
            {
                createRoomPopupPanel = FindGameObjectByName("CreatePopUpPanel");
            }

            if (networkIssueRoot == null)
            {
                networkIssueRoot = FindGameObjectByName("NetworkIssue");
            }

            if (roomIdInputField == null)
            {
                GameObject input = FindGameObjectByName("RoomIdInputField");
                if (input != null)
                {
                    roomIdInputField = input.GetComponent<TMP_InputField>();
                }
            }

            if (joinConfirmButton == null)
            {
                joinConfirmButton = FindButton("Join", "JoinPopUp");
            }

            if (joinStatusLabel == null)
            {
                GameObject status = FindGameObjectByName("JoinGameText");
                if (status != null)
                {
                    joinStatusLabel = status.GetComponent<Text>();
                }
            }

            if (joinActionLabel == null && joinConfirmButton != null)
            {
                joinActionLabel = joinConfirmButton.GetComponentInChildren<Text>(true);
            }

            if (createRoomTitleLabel == null)
            {
                GameObject title = FindGameObjectByName("RoomCreated");
                if (title != null)
                {
                    createRoomTitleLabel = title.GetComponent<Text>();
                }
            }

            if (createRoomIdLabel == null)
            {
                GameObject id = FindGameObjectByName("RoomID");
                if (id != null)
                {
                    createRoomIdLabel = id.GetComponent<Text>();
                }
            }

            if (createRoomOccupancyLabel == null)
            {
                GameObject occupancy = FindGameObjectByName("JoinedPlayersCount");
                if (occupancy != null)
                {
                    createRoomOccupancyLabel = occupancy.GetComponent<Text>();
                }
            }

            if (createRoomStartButton == null)
            {
                createRoomStartButton = FindButton("Start", "CreatePopUp");
            }

            if (networkIssueTitleLabel == null)
            {
                GameObject title = FindGameObjectByName("Isssue");
                if (title != null)
                {
                    networkIssueTitleLabel = title.GetComponent<Text>();
                }
            }

            if (networkIssueMessageLabel == null)
            {
                GameObject message = FindGameObjectByName("NetworkError");
                if (message != null)
                {
                    networkIssueMessageLabel = message.GetComponent<Text>();
                }
            }

            if (networkIssueCloseButton == null)
            {
                networkIssueCloseButton = FindButton("Close", "NetworkIssuePanel");
            }
        }

        private static int CountOccupiedSeats(string[] chairs)
        {
            if (chairs == null)
            {
                return 0;
            }

            int occupied = 0;
            for (int i = 0; i < chairs.Length; i++)
            {
                if (!string.IsNullOrEmpty(chairs[i]))
                {
                    occupied++;
                }
            }

            return occupied;
        }

        private static Button FindButton(string name, string parentName = null)
        {
            GameObject target = FindGameObjectByName(name, parentName);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private static GameObject FindGameObjectByName(string name, string parentName = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject current = all[i];
                if (current == null || current.hideFlags != HideFlags.None)
                {
                    continue;
                }

                if (!string.Equals(current.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(parentName))
                {
                    return current;
                }

                Transform parent = current.transform.parent;
                while (parent != null)
                {
                    if (string.Equals(parent.name, parentName, StringComparison.Ordinal))
                    {
                        return current;
                    }

                    parent = parent.parent;
                }
            }

            return null;
        }
    }
}
