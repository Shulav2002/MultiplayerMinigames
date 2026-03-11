using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HotDynamite.Networking
{
    public class HotDynamiteOnlineMatchController : MonoBehaviour
    {
        [Header("Match Flow")]
        [SerializeField, Min(1f)] private float randomModeAutoStartSeconds = 4f;
        [SerializeField, Min(1f)] private float privateModeAutoStartSeconds = 10f;
        [SerializeField, Min(0.1f)] private float matchStartCountdownSeconds = 3f;
        [Header("Networking")]
        [SerializeField, Min(0.02f)] private float inputSendIntervalSeconds = 0.05f;
        [SerializeField, Min(0.02f)] private float snapshotSendIntervalSeconds = 0.066f;

        private HotDynamiteGameManager gameManager;
        private HotDynamiteLobbyNetworkClient networkClient;
        private GameObject lobbyCanvas;
        private GameObject gameContainer;

        private bool isInitialized;
        private bool isSubscribed;
        private bool matchPreparing;
        private bool matchRunning;
        private bool matchEndSent;
        private float hostAutoStartAt = -1f;
        private float nextInputSendAt;
        private float nextSnapshotSendAt;
        private int snapshotTick;

        public void Initialize(
            HotDynamiteGameManager gameManager,
            GameObject lobbyCanvas,
            GameObject gameContainer,
            HotDynamiteLobbyNetworkClient networkClient)
        {
            if (gameManager == null || networkClient == null)
            {
                Debug.LogError("HotDynamiteOnlineMatchController: GameManager and NetworkClient are required.");
                return;
            }

            this.gameManager = gameManager;
            this.lobbyCanvas = lobbyCanvas;
            this.gameContainer = gameContainer;

            if (this.networkClient != networkClient)
            {
                Unsubscribe();
                this.networkClient = networkClient;
            }

            Subscribe();
            isInitialized = true;
        }

        private void Update()
        {
            if (!isInitialized || networkClient == null || gameManager == null)
            {
                return;
            }

            if (networkClient.IsHost && !matchPreparing && !matchRunning && hostAutoStartAt > 0f && Time.time >= hostAutoStartAt)
            {
                StartHostMatchPreparation();
            }

            if (matchRunning)
            {
                if (networkClient.IsHost)
                {
                    if (Time.time >= nextSnapshotSendAt)
                    {
                        nextSnapshotSendAt = Time.time + snapshotSendIntervalSeconds;
                        BroadcastSnapshot();
                    }

                    if (gameManager.IsGameOver && !matchEndSent)
                    {
                        matchEndSent = true;
                        BroadcastMatchEnd();
                    }
                }
                else if (Time.time >= nextInputSendAt)
                {
                    nextInputSendAt = Time.time + inputSendIntervalSeconds;
                    SendLocalInput();
                }
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom += HandleJoinedRoom;
            networkClient.RoomOccupancyChanged += HandleRoomOccupancyChanged;
            networkClient.MatchPrepareReceived += HandleMatchPrepareReceived;
            networkClient.StateSnapshotReceived += HandleStateSnapshotReceived;
            networkClient.MatchEndReceived += HandleMatchEndReceived;
            networkClient.RoomMessageReceived += HandleRoomMessageReceived;
            networkClient.Disconnected += HandleDisconnected;
            networkClient.LeftRoom += HandleLeftRoom;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom -= HandleJoinedRoom;
            networkClient.RoomOccupancyChanged -= HandleRoomOccupancyChanged;
            networkClient.MatchPrepareReceived -= HandleMatchPrepareReceived;
            networkClient.StateSnapshotReceived -= HandleStateSnapshotReceived;
            networkClient.MatchEndReceived -= HandleMatchEndReceived;
            networkClient.RoomMessageReceived -= HandleRoomMessageReceived;
            networkClient.Disconnected -= HandleDisconnected;
            networkClient.LeftRoom -= HandleLeftRoom;
            isSubscribed = false;
        }

        private void HandleJoinedRoom(HotDynamiteJoinedMessage joined)
        {
            ResetMatchState();

            if (networkClient.IsHost)
            {
                hostAutoStartAt = Time.time + GetAutoStartDelaySeconds();
            }
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            if (!networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            if (occupiedSeats >= capacity && capacity > 0)
            {
                StartHostMatchPreparation();
            }
        }

        private void HandleMatchPrepareReceived(HotDynamiteMatchPreparePayload payload)
        {
            if (networkClient.IsHost || payload == null)
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: false, payload.countdownSeconds);
            matchRunning = true;
            nextInputSendAt = Time.time;
        }

        private void HandleStateSnapshotReceived(HotDynamiteStateSnapshotPayload payload)
        {
            if (networkClient.IsHost || payload == null)
            {
                return;
            }

            gameManager.ApplyStateSnapshot(payload);
        }

        private void HandleMatchEndReceived(HotDynamiteMatchEndPayload payload)
        {
            matchRunning = false;
            matchPreparing = false;
        }

        private void HandleRoomMessageReceived(string fromUserId, string payloadJson)
        {
            if (!networkClient.IsHost || !matchRunning || string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "null")
            {
                return;
            }

            HotDynamiteRoomPayloadEnvelope envelope = JsonUtility.FromJson<HotDynamiteRoomPayloadEnvelope>(payloadJson);
            if (envelope == null || envelope.kind != HotDynamitePayloadKinds.PlayerInput)
            {
                return;
            }

            HotDynamitePlayerInputPayload payload = JsonUtility.FromJson<HotDynamitePlayerInputPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            gameManager.SetNetworkMoveInput(payload.chairId, new Vector2(payload.moveX, payload.moveY));
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            ResetMatchState();
        }

        private void HandleLeftRoom()
        {
            ResetMatchState();
        }

        private async void StartHostMatchPreparation()
        {
            if (!isInitialized || !networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            HotDynamiteMatchPreparePayload payload = BuildMatchPreparePayload();
            if (payload == null)
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: true, payload.countdownSeconds);
            matchRunning = true;
            nextSnapshotSendAt = Time.time + 0.1f;
            snapshotTick = 0;
            hostAutoStartAt = -1f;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast match prepare payload. {exception.Message}");
            }
        }

        private HotDynamiteMatchPreparePayload BuildMatchPreparePayload()
        {
            string[] chairs = networkClient.CurrentChairs;
            int capacity = Mathf.Max(networkClient.CurrentCapacity, 2);
            if (chairs == null || chairs.Length == 0)
            {
                chairs = new string[capacity];
            }

            List<HotDynamiteRosterEntry> roster = new List<HotDynamiteRosterEntry>(capacity);
            for (int chairId = 0; chairId < capacity; chairId++)
            {
                string userId = chairId < chairs.Length ? chairs[chairId] : null;
                bool isBot = string.IsNullOrEmpty(userId);
                if (isBot)
                {
                    userId = $"bot-{networkClient.CurrentRoomId}-{chairId}";
                }

                roster.Add(new HotDynamiteRosterEntry
                {
                    userId = userId,
                    chairId = chairId,
                    isBot = isBot,
                    isHost = userId == networkClient.HostUserId,
                    appearanceIndex = chairId
                });
            }

            return new HotDynamiteMatchPreparePayload
            {
                roomMode = networkClient.RoomMode,
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                fillMissingSeatsWithBots = true,
                targetPlayerCount = capacity,
                countdownSeconds = matchStartCountdownSeconds,
                roster = roster.ToArray()
            };
        }

        private async void BroadcastSnapshot()
        {
            if (!networkClient.IsHost || gameManager == null)
            {
                return;
            }

            HotDynamiteStateSnapshotPayload snapshot = gameManager.BuildStateSnapshot(networkClient.HostUserId, snapshotTick++);
            snapshot.roomId = networkClient.CurrentRoomId;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(snapshot));
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast snapshot. {exception.Message}");
            }
        }

        private async void BroadcastMatchEnd()
        {
            HotDynamiteMatchEndPayload payload = new HotDynamiteMatchEndPayload
            {
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                winningChairId = FindWinningChair()
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast match end. {exception.Message}");
            }
        }

        private async void SendLocalInput()
        {
            HotDynamitePlayer localPlayer = FindLocalPlayer();
            if (localPlayer == null || !localPlayer.IsAlive || localPlayer.HumanInput == null || !localPlayer.gameObject.activeInHierarchy)
            {
                return;
            }

            Vector2 move = localPlayer.HumanInput.MoveVector;
            HotDynamitePlayerInputPayload payload = new HotDynamitePlayerInputPayload
            {
                playerId = networkClient.LocalUserId,
                chairId = localPlayer.ChairId,
                sequence = 0,
                moveX = move.x,
                moveY = move.y
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to send player input. {exception.Message}");
            }
        }

        private HotDynamitePlayer FindLocalPlayer()
        {
            IReadOnlyList<HotDynamitePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                HotDynamitePlayer player = players[i];
                if (player != null && player.UserId == networkClient.LocalUserId)
                {
                    return player;
                }
            }

            return null;
        }

        private int FindWinningChair()
        {
            IReadOnlyList<HotDynamitePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                HotDynamitePlayer player = players[i];
                if (player != null && player.IsAlive)
                {
                    return player.ChairId;
                }
            }

            return -1;
        }

        private void ActivateGameView()
        {
            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(true);
            }
        }

        private void ResetMatchState()
        {
            matchPreparing = false;
            matchRunning = false;
            matchEndSent = false;
            hostAutoStartAt = -1f;
            nextInputSendAt = 0f;
            nextSnapshotSendAt = 0f;
            snapshotTick = 0;
        }

        private float GetAutoStartDelaySeconds()
        {
            return networkClient.RoomMode == HotDynamiteRoomModes.Private
                ? privateModeAutoStartSeconds
                : randomModeAutoStartSeconds;
        }
    }
}
