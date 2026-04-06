using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using StayAlive.Networking;

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
            PrivateWaitingHost = 2,
            PrivateWaitingGuest = 3
        }

        [Header("UI & Game References")]
        [SerializeField] private GameObject lobbyCanvas;
        [SerializeField] private GameObject gameContainer;
        [SerializeField] private StayAlivePopupManager popupManager;

        [Header("Join Popup")]
        [SerializeField] private Component roomIdInputField;

        [Header("Game End")]
        [SerializeField] private GameObject lobbyRoot;
        [SerializeField] private GameObject gameEndPanel;
        [SerializeField] private GameObject winnerAnimationRoot;
        [SerializeField] private Animator winnerAnimator;

        [Header("Labels")]
        [SerializeField] private string networkIssueConnectMessage = "Unable to connect to the server. Please check your internet connection and try again.";
        [SerializeField] private string networkIssueDisconnectMessage = "Connection to the server was lost. Please check your internet connection and try again.";

        [Header("Game")]
        [SerializeField] private StayAliveGameManager gameManager;

        [Header("Networking")]
        [SerializeField] private bool useNetworkLobby = true;
        [SerializeField] private StayAliveLobbyNetworkClient networkClient;
        [SerializeField] private StayAliveOnlineMatchController onlineMatchController;
        [SerializeField, Min(1f)] private float onlineConnectTimeoutSeconds = 5f;
        [SerializeField, Min(1f)] private float onlineJoinTimeoutSeconds = 6f;

        [Header("Offline With Bots")]
        [SerializeField, Min(2)] private int offlineTotalPlayers = 4;
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;
        [SerializeField, Min(0f)] private float gameEndDelaySeconds = 0.5f;

        [Header("Pause Overlay")]
        [SerializeField] private Button pauseRestartButton;
        [SerializeField] private Slider pauseVolumeSlider;

        private ReplayMode lastReplayMode;
        private int lastOfflineTotalPlayers = 4;
        private int lastOfflineHumanPlayers = 1;
        private RoomPanelMode roomPanelMode;
        private int currentOccupiedSeats;
        private int currentCapacity;
        private string currentRoomId;
        private bool joinRequestInFlight;
        private bool dismissPrivateRoomRequested;
        private bool privateRoomDismissInFlight;
        private bool subscribedToGameEvents;
        private bool subscribedToPopupEvents;
        private Coroutine showGameEndCoroutine;
        private bool randomWaitingProgressActive;
        private int displayedRandomWaitingSeats;
        private float nextRandomWaitingAdvanceAt;
        private bool missingReferencesLogged;

        private const string PauseVolumePreferenceKey = "StayAlive.MasterVolume";
        private const string WinnerAnimationLayerName = "Base Layer";
        private const string WinnerStateBlue = "BlueCharacter";
        private const string WinnerStateGreen = "GreenCharacter";
        private const string WinnerStateRed = "RedCharacter";
        private const string WinnerStateYellow = "YellowCharacter";
        private const float RandomWaitingFakeAdvanceMinSeconds = 0.9f;
        private const float RandomWaitingFakeAdvanceMaxSeconds = 1.75f;

        private bool IsPrimaryLobbyManager => string.Equals(gameObject.name, "Manager", StringComparison.Ordinal);

        private void Update()
        {
            UpdateRandomWaitingProgress();

            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                RefreshJoinEntryUi();
            }
        }

        private void Start()
        {
            EnsureReferences();
            ApplySavedPauseVolume();

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
            SubscribeToGameEvents();
            ShowLobbyHome();
        }

        private void OnDestroy()
        {
            if (IsPrimaryLobbyManager)
            {
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

            PrepareForFreshOnlineAttempt();
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
            lastReplayMode = ReplayMode.None;
            ShowJoinEntryPanel();
        }

        public async void OnOpenCreateRoomPopupClicked()
        {
            if (!useNetworkLobby)
            {
                return;
            }

            EnsureReferences();
            lastReplayMode = ReplayMode.None;
            dismissPrivateRoomRequested = false;
            PrepareForFreshOnlineAttempt();
            ShowLobbyVisualState();

            if (!await EnsureReadyForPrivateRoomCreateAsync())
            {
                return;
            }

            ShowPrivateWaitingPanel(isHost: true, "Creating...", 1, Mathf.Max(2, networkClient != null ? networkClient.RoomCapacity : 4));
            await BeginOnlineJoinAsync(null, createRoom: true, usePrivateRoom: true);
        }

        public void OnRoomPopupConfirmButtonClicked()
        {
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                SubmitJoinRoomRequest();
                return;
            }

            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost)
            {
                StartHostedPrivateMatch();
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
            RoomPanelMode panelMode = roomPanelMode;
            HideRoomPanel();
            try
            {
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
            finally
            {
                privateRoomDismissInFlight = false;
            }
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
                    return;

                case ReplayMode.QuickPlayOnline:
                    RestartQuickPlayOnline();
                    return;
            }

            ShowLobbyHome();
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
            ShowLobbyHome();
        }

        public void OnPauseButtonClicked()
        {
            if (!IsPrimaryLobbyManager || popupManager == null || gameContainer == null || !gameContainer.activeInHierarchy || popupManager.IsOverlayPopupVisible)
            {
                return;
            }

            if (pauseRestartButton != null)
            {
                pauseRestartButton.interactable = CanPauseRestart();
            }

            popupManager.ShowPauseOverlayPopup();
            SyncPauseVolumeSlider();
        }

        public async void OnPauseHomeButtonClicked()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            ClosePauseMenu();
            await ReturnToHomeAsync();
        }

        public void OnPauseResumeButtonClicked()
        {
            if (!IsPrimaryLobbyManager)
            {
                return;
            }

            ClosePauseMenu();
        }

        public void OnPauseRestartButtonClicked()
        {
            if (!IsPrimaryLobbyManager || !CanPauseRestart())
            {
                return;
            }

            ClosePauseMenu();

            switch (ResolveReplayModeForPauseRestart())
            {
                case ReplayMode.OfflineBots:
                    StartGameLocalFlow(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
                    return;

                case ReplayMode.QuickPlayOnline:
                    RestartQuickPlayOnline();
                    return;
            }

            ShowLobbyHome();
        }

        public void OnPauseVolumeSliderValueChanged(float value)
        {
            float clampedValue = Mathf.Clamp01(value);
            AudioListener.volume = clampedValue;
            PlayerPrefs.SetFloat(PauseVolumePreferenceKey, clampedValue);
            PlayerPrefs.Save();
        }

        public void OnJoinRoomInputValueChanged(string value)
        {
            popupManager?.ClearLobbyJoinError();
            RefreshJoinEntryUi();
        }

        private async void SubmitJoinRoomRequest()
        {
            string roomId = GetRoomIdText();
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("StayAliveLobbyManager: Room id is required.");
                return;
            }

            dismissPrivateRoomRequested = false;
            popupManager?.ClearLobbyJoinError();
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

            if (!networkClient.IsConnected)
            {
                PrepareForFreshOnlineAttempt();
            }

            joinRequestInFlight = true;
            try
            {
                if (!networkClient.IsConnected)
                {
                    await networkClient.ConnectAsync();
                    if (!await WaitForConnectionAsync())
                    {
                        throw new TimeoutException(networkIssueConnectMessage);
                    }
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

                if (!await WaitForRoomJoinAsync())
                {
                    throw new TimeoutException(networkIssueConnectMessage);
                }
            }
            catch (Exception exception)
            {
                joinRequestInFlight = false;
                dismissPrivateRoomRequested = false;
                Debug.LogError($"StayAliveLobbyManager: Failed to connect or join backend room.\n{exception}");
                if (createRoom && roomPanelMode == RoomPanelMode.PrivateWaitingHost && currentOccupiedSeats <= 1)
                {
                    HideRoomPanel();
                }

                if (usePrivateRoom && !createRoom && roomPanelMode == RoomPanelMode.JoinEntry)
                {
                    ShowJoinEntryError(exception.Message);
                    return;
                }

                if (!usePrivateRoom || createRoom)
                {
                    ShowNetworkIssue(networkIssueConnectMessage);
                }
            }
        }

        private async Task<bool> WaitForConnectionAsync()
        {
            float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1f, onlineConnectTimeoutSeconds);
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                if (networkClient != null && networkClient.IsConnected)
                {
                    return true;
                }

                await Task.Yield();
            }

            return networkClient != null && networkClient.IsConnected;
        }

        private async Task<bool> WaitForRoomJoinAsync()
        {
            float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1f, onlineJoinTimeoutSeconds);
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                if (networkClient != null && networkClient.IsInRoom)
                {
                    return true;
                }

                if (networkClient == null || !networkClient.IsConnected)
                {
                    return false;
                }

                await Task.Yield();
            }

            return networkClient != null && networkClient.IsInRoom;
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
            lastReplayMode = ReplayMode.QuickPlayOnline;
            ShowRandomWaitingPanel();
            await LeaveCurrentRoomIfNeededAsync();
            await BeginOnlineJoinAsync(null, createRoom: false, usePrivateRoom: false);
        }

        private ReplayMode ResolveReplayModeForReplay()
        {
            if (lastReplayMode == ReplayMode.OfflineBots)
            {
                return ReplayMode.OfflineBots;
            }

            if (useNetworkLobby && networkClient != null && networkClient.RoomMode == StayAliveRoomModes.Random)
            {
                return ReplayMode.QuickPlayOnline;
            }

            return ReplayMode.None;
        }

        private ReplayMode ResolveReplayModeForPauseRestart()
        {
            if (lastReplayMode == ReplayMode.OfflineBots)
            {
                return ReplayMode.OfflineBots;
            }

            if (useNetworkLobby
                && networkClient != null
                && networkClient.IsConnected
                && networkClient.RoomMode == StayAliveRoomModes.Random)
            {
                return ReplayMode.QuickPlayOnline;
            }

            return ReplayMode.None;
        }

        private bool CanPauseRestart()
        {
            return ResolveReplayModeForPauseRestart() != ReplayMode.None;
        }

        private async Task ReturnToHomeAsync()
        {
            dismissPrivateRoomRequested = false;
            await LeaveCurrentRoomIfNeededAsync();
            ShowLobbyHome();
        }

        private void ShowLobbyHome()
        {
            ClosePauseMenu();
            HideRoomPanel();
            HideGameEnd();
            HideWaiting();
            HideNetworkIssue();

            if (popupManager != null)
            {
                popupManager.HideAllPopups();
            }

            onlineMatchController?.ResetToLobbyState();
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
            StartRandomWaitingProgress();
            popupManager?.ShowWaitingForPlayersPopup(displayedRandomWaitingSeats, GetRandomWaitingCapacity());
        }

        private void ShowJoinEntryPanel()
        {
            roomPanelMode = RoomPanelMode.JoinEntry;
            ShowLobbyVisualState();
            SetRoomIdReadOnly(false);
            SetRoomIdText(string.Empty);
            popupManager?.ClearLobbyJoinError();
            popupManager?.ShowJoinPopup();
            RefreshJoinEntryUi();
        }

        private void RefreshJoinEntryUi()
        {
            bool canJoin = !string.IsNullOrWhiteSpace(GetRoomIdText()) && !joinRequestInFlight;
            popupManager?.SetLobbyPrimaryButtonInteractable(canJoin);
        }

        private void ShowPrivateWaitingPanel(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            StopRandomWaitingProgress();
            roomPanelMode = isHost ? RoomPanelMode.PrivateWaitingHost : RoomPanelMode.PrivateWaitingGuest;
            currentOccupiedSeats = occupiedSeats;
            currentCapacity = capacity;
            currentRoomId = roomId ?? string.Empty;
            ShowLobbyVisualState();
            popupManager?.ShowPrivateRoomWaitingPopup(
                isHost,
                currentRoomId,
                currentOccupiedSeats,
                currentCapacity,
                CanHostStartPrivateMatch());
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
                    currentCapacity,
                    CanHostStartPrivateMatch());
            }
        }

        private bool CanHostStartPrivateMatch()
        {
            return roomPanelMode == RoomPanelMode.PrivateWaitingHost
                && onlineMatchController != null
                && onlineMatchController.CanHostStartMatch
                && currentOccupiedSeats >= 2
                && !string.IsNullOrWhiteSpace(currentRoomId)
                && !string.Equals(currentRoomId, "Creating...", StringComparison.OrdinalIgnoreCase);
        }

        private void StartHostedPrivateMatch()
        {
            if (onlineMatchController == null)
            {
                return;
            }

            if (!CanHostStartPrivateMatch())
            {
                Debug.Log("StayAliveLobbyManager: Waiting for another player before starting the room.");
                return;
            }

            HideRoomPanel();
            onlineMatchController.StartHostedMatchNow();
        }

        private void HideRoomPanel()
        {
            roomPanelMode = RoomPanelMode.Hidden;
            currentRoomId = string.Empty;

            if (popupManager != null)
            {
                popupManager.HideLobbyPopup();
            }
        }

        private void ShowGameEnd(int[] winningChairIds)
        {
            if (!IsPrimaryLobbyManager || showGameEndCoroutine != null)
            {
                return;
            }

            showGameEndCoroutine = StartCoroutine(ShowGameEndAfterDelay(winningChairIds));
        }

        private System.Collections.IEnumerator ShowGameEndAfterDelay(int[] winningChairIds)
        {
            float delay = Mathf.Max(0f, gameEndDelaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            showGameEndCoroutine = null;
            ClosePauseMenu();
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

            SetWinnerPanelsForWinningChairs(winningChairIds);
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

            ShowWinnerAnimation(false, null);
        }

        private void HideWaiting()
        {
            StopRandomWaitingProgress();

            if (popupManager != null)
            {
                popupManager.HidePopupByIntent(StayAlivePopupIntent.WaitingForPlayers);
                popupManager.HidePopupByIntent(StayAlivePopupIntent.PrivateRoomWaitingHost);
                popupManager.HidePopupByIntent(StayAlivePopupIntent.PrivateRoomWaitingGuest);
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

        private void PrepareForFreshOnlineAttempt()
        {
            if (onlineMatchController != null)
            {
                onlineMatchController.ResetToLobbyState();
            }

            if (networkClient != null && !networkClient.IsConnected)
            {
                networkClient.ResetRuntimeState();
            }
        }

        private void HideNetworkIssue()
        {
            popupManager?.HidePopupByIntent(StayAlivePopupIntent.NetworkError);
        }

        private void SetWinnerPanelsForWinningChairs(int[] winningChairIds)
        {
            int[] appearanceIndices = ResolveWinningAppearanceIndices(winningChairIds);
            if (appearanceIndices.Length == 0)
            {
                ShowWinnerAnimation(false, null);
                return;
            }

            if (appearanceIndices.Length > 1)
            {
                ShowWinnerAnimation(false, null);
                return;
            }

            ShowWinnerAnimation(true, ResolveWinnerAnimationStateName(appearanceIndices[0]));
        }

        private int[] ResolveWinningAppearanceIndices(int[] winningChairIds)
        {
            if (winningChairIds == null || winningChairIds.Length == 0)
            {
                return Array.Empty<int>();
            }

            List<int> appearanceIndices = new List<int>(winningChairIds.Length);
            for (int i = 0; i < winningChairIds.Length; i++)
            {
                int winningChairId = winningChairIds[i];
                if (gameManager != null)
                {
                    StayAlivePlayer winningPlayer = gameManager.GetPlayerByChair(winningChairId);
                    if (winningPlayer != null)
                    {
                        appearanceIndices.Add(Mathf.Max(0, winningPlayer.AppearanceIndex % 4));
                        continue;
                    }
                }

                appearanceIndices.Add(Mathf.Clamp(winningChairId, 0, 3));
            }

            return appearanceIndices.ToArray();
        }

        private void ShowWinnerAnimation(bool visible, string stateName)
        {
            if (winnerAnimationRoot != null)
            {
                winnerAnimationRoot.SetActive(visible);
            }

            if (!visible || winnerAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return;
            }

            string fullStateName = $"{WinnerAnimationLayerName}.{stateName}";
            int fullStateHash = Animator.StringToHash(fullStateName);
            int shortStateHash = Animator.StringToHash(stateName);
            winnerAnimator.Rebind();
            winnerAnimator.Update(0f);

            if (winnerAnimator.HasState(0, fullStateHash))
            {
                winnerAnimator.Play(fullStateHash, 0, 0f);
            }
            else if (winnerAnimator.HasState(0, shortStateHash))
            {
                winnerAnimator.Play(shortStateHash, 0, 0f);
            }
            else
            {
                Debug.LogWarning($"StayAliveLobbyManager: Winner animation state '{stateName}' was not found on controller '{winnerAnimator.runtimeAnimatorController}'.");
            }

            winnerAnimator.Update(0f);
        }

        private static string ResolveWinnerAnimationStateName(int appearanceIndex)
        {
            switch (appearanceIndex)
            {
                case 0:
                    return WinnerStateRed;

                case 1:
                    return WinnerStateBlue;

                case 2:
                    return WinnerStateGreen;

                case 3:
                    return WinnerStateYellow;

                default:
                    return WinnerStateBlue;
            }
        }

        private void HandleGameEnded(int[] winningChairIds)
        {
            ShowGameEnd(winningChairIds);
        }

        private void HandleMatchEnded(int[] winningChairIds)
        {
            ShowGameEnd(winningChairIds);
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
            popupManager?.HideLobbyPopup();

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
            if (missingReferencesLogged)
            {
                return;
            }

            missingReferencesLogged = true;
            LogMissingReference(lobbyCanvas == null, "lobbyCanvas");
            LogMissingReference(gameContainer == null, "gameContainer");
            LogMissingReference(popupManager == null, "popupManager");
            LogMissingReference(roomIdInputField == null, "roomIdInputField");
            LogMissingReference(lobbyRoot == null, "lobbyRoot");
            LogMissingReference(gameEndPanel == null, "gameEndPanel");
            LogMissingReference(gameManager == null, "gameManager");
            LogMissingReference(pauseRestartButton == null, "pauseRestartButton");
            LogMissingReference(pauseVolumeSlider == null, "pauseVolumeSlider");

            if (useNetworkLobby)
            {
                LogMissingReference(networkClient == null, "networkClient");
                LogMissingReference(onlineMatchController == null, "onlineMatchController");
            }
        }

        private void InitializeOnlineMatchController()
        {
            if (!useNetworkLobby || onlineMatchController == null || networkClient == null || gameManager == null)
            {
                return;
            }

            onlineMatchController.Initialize(gameManager, lobbyCanvas, gameContainer, networkClient, popupManager);
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
                lastReplayMode = ReplayMode.None;
                ShowPrivateWaitingPanel(networkClient.IsHost, joined.roomId, currentOccupiedSeats, currentCapacity);
                return;
            }

            if (networkClient != null && networkClient.RoomMode == StayAliveRoomModes.Random)
            {
                lastReplayMode = ReplayMode.QuickPlayOnline;
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
            RefreshPrivateWaitingUi();
            RefreshRandomWaitingProgressUi();
        }

        private void HandleRoomReady(string[] chairs)
        {
        }

        private void HandleBackendError(StayAliveErrorMessage error)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                ShowJoinEntryError(error?.message);
                Debug.LogError($"StayAliveLobbyManager: Backend error: {error?.message}");
                return;
            }

            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost && currentOccupiedSeats <= 1)
            {
                HideRoomPanel();
            }

            if (networkClient != null && networkClient.RoomMode == StayAliveRoomModes.Random)
            {
                ShowLobbyHome();
            }

            Debug.LogError($"StayAliveLobbyManager: Backend error: {error?.message}");
        }

        private void HandleConnectionError(string errorMessage)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                ShowJoinEntryError(string.IsNullOrWhiteSpace(errorMessage) ? networkIssueConnectMessage : errorMessage);
                Debug.LogError($"StayAliveLobbyManager: Connection error: {errorMessage}");
                return;
            }

            if (roomPanelMode == RoomPanelMode.PrivateWaitingHost && currentOccupiedSeats <= 1)
            {
                HideRoomPanel();
            }

            if (networkClient != null && (networkClient.RoomMode == StayAliveRoomModes.Random || roomPanelMode == RoomPanelMode.PrivateWaitingHost))
            {
                ShowNetworkIssue(networkIssueConnectMessage);
            }

            Debug.LogError($"StayAliveLobbyManager: Connection error: {errorMessage}");
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            joinRequestInFlight = false;
            dismissPrivateRoomRequested = false;
            if (roomPanelMode == RoomPanelMode.JoinEntry)
            {
                ShowJoinEntryError(networkIssueDisconnectMessage);
                Debug.Log($"StayAliveLobbyManager: Disconnected from backend. Status: {closeReason.Status}, Description: {closeReason.Description}");
                return;
            }

            HideRoomPanel();
            if (networkClient != null && (networkClient.RoomMode == StayAliveRoomModes.Random || currentOccupiedSeats > 0 || roomPanelMode != RoomPanelMode.Hidden))
            {
                ShowNetworkIssue(networkIssueDisconnectMessage);
            }

            Debug.Log($"StayAliveLobbyManager: Disconnected from backend. Status: {closeReason.Status}, Description: {closeReason.Description}");
        }

        private async void DismissJoinedPrivateRoomAfterClose()
        {
            await LeaveCurrentRoomIfNeededAsync();
            dismissPrivateRoomRequested = false;
            ShowLobbyHome();
        }

        private void ShowJoinEntryError(string message)
        {
            string resolvedMessage = string.IsNullOrWhiteSpace(message) ? "Unable to join room." : message;
            ShowLobbyVisualState();
            if (popupManager != null)
            {
                popupManager.ShowLobbyJoinError(resolvedMessage);
            }

            RefreshJoinEntryUi();
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

                case StayAlivePopupIntent.PauseMenu:
                    OnPauseResumeButtonClicked();
                    break;
            }
        }

        private void ClosePauseMenu()
        {
            popupManager?.HideOverlayPopup();
        }

        private void ApplySavedPauseVolume()
        {
            float savedVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PauseVolumePreferenceKey, 1f));
            AudioListener.volume = savedVolume;
            SyncPauseVolumeSlider();
        }

        private void SyncPauseVolumeSlider()
        {
            float currentVolume = Mathf.Clamp01(AudioListener.volume);
            if (pauseVolumeSlider != null)
            {
                pauseVolumeSlider.SetValueWithoutNotify(currentVolume);
            }

            popupManager?.SetOverlayVolumeSliderValueWithoutNotify(currentVolume);
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

        private void UpdateRandomWaitingProgress()
        {
            if (!randomWaitingProgressActive || Time.unscaledTime < nextRandomWaitingAdvanceAt)
            {
                return;
            }

            int capacity = GetRandomWaitingCapacity();
            int realOccupiedSeats = Mathf.Clamp(currentOccupiedSeats, 0, capacity);
            int fakeAdvanceCap = Mathf.Max(1, capacity - 1);
            int nextDisplayedSeats = Mathf.Max(displayedRandomWaitingSeats, realOccupiedSeats);

            if (nextDisplayedSeats < fakeAdvanceCap)
            {
                displayedRandomWaitingSeats = nextDisplayedSeats + 1;
            }
            else
            {
                displayedRandomWaitingSeats = nextDisplayedSeats;
            }

            ScheduleNextRandomWaitingAdvance();
            RefreshRandomWaitingProgressUi();
        }

        private void StartRandomWaitingProgress()
        {
            int capacity = GetRandomWaitingCapacity();
            int baselineSeats = Mathf.Max(1, Mathf.Clamp(currentOccupiedSeats, 0, capacity));

            if (!randomWaitingProgressActive)
            {
                displayedRandomWaitingSeats = baselineSeats;
            }
            else
            {
                displayedRandomWaitingSeats = Mathf.Clamp(Mathf.Max(displayedRandomWaitingSeats, baselineSeats), 1, capacity);
            }

            randomWaitingProgressActive = true;
            ScheduleNextRandomWaitingAdvance();
            RefreshRandomWaitingProgressUi();
        }

        private void StopRandomWaitingProgress()
        {
            randomWaitingProgressActive = false;
            displayedRandomWaitingSeats = 0;
            nextRandomWaitingAdvanceAt = -1f;
        }

        private void ScheduleNextRandomWaitingAdvance()
        {
            nextRandomWaitingAdvanceAt = Time.unscaledTime + UnityEngine.Random.Range(RandomWaitingFakeAdvanceMinSeconds, RandomWaitingFakeAdvanceMaxSeconds);
        }

        private void RefreshRandomWaitingProgressUi()
        {
            if (!randomWaitingProgressActive || popupManager == null || popupManager.LobbyIntent != StayAlivePopupIntent.WaitingForPlayers)
            {
                return;
            }

            int capacity = GetRandomWaitingCapacity();
            int realOccupiedSeats = Mathf.Clamp(currentOccupiedSeats, 0, capacity);
            int visibleSeats = Mathf.Clamp(Mathf.Max(displayedRandomWaitingSeats, realOccupiedSeats), 1, capacity);
            displayedRandomWaitingSeats = visibleSeats;
            popupManager.ShowWaitingForPlayersPopup(visibleSeats, capacity);
        }

        private int GetRandomWaitingCapacity()
        {
            if (currentCapacity > 0)
            {
                return Mathf.Max(2, currentCapacity);
            }

            if (networkClient != null && networkClient.RoomCapacity > 0)
            {
                return Mathf.Max(2, networkClient.RoomCapacity);
            }

            return 4;
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

        private void LogMissingReference(bool isMissing, string fieldName)
        {
            if (isMissing)
            {
                Debug.LogWarning($"StayAliveLobbyManager: Assign '{fieldName}' in the inspector.", this);
            }
        }
    }
}
