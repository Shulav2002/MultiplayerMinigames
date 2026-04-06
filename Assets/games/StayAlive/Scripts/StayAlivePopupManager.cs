using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace StayAlive.Lobby
{
    public enum StayAlivePopupCanvasType
    {
        Lobby = 0,
        Overlay = 1
    }

        public enum StayAlivePopupIntent
        {
            None = 0,
            JoinRoom = 1,
            CreateRoom = 2,
            WaitingForPlayers = 3,
            PrivateRoomWaitingHost = 4,
            PrivateRoomWaitingGuest = 5,
            NetworkError = 6,
            Custom = 7,
            PauseMenu = 8
        }

    public enum StayAlivePopupButtonType
    {
        Primary = 0,
        Secondary = 1,
        Close = 2
    }

    public class StayAlivePopupManager : MonoBehaviour
    {
        [Serializable]
        private sealed class PopupCanvasBinding
        {
            public GameObject canvasRoot;
            public GameObject popupPanel;
            public GameObject popupBoard;
            public GameObject titleRoot;
            public GameObject detailsRoot;
            public Component titleLabel;
            public Component detailsLabel;
            public GameObject roomIdInputRoot;
            public GameObject homeButtonRoot;
            public Button homeButton;
            public GameObject joinButtonRoot;
            public Button joinButton;
            public Component joinButtonLabel;
            public GameObject volumeSliderRoot;
            public Slider volumeSlider;
            public GameObject confirmButtonRoot;
            public Button confirmButton;
            public Component confirmButtonLabel;
            public GameObject startButtonRoot;
            public Button startButton;
            public Component startButtonLabel;
            public GameObject closeButtonRoot;
            public Button closeButton;
            public Component closeButtonLabel;

            [NonSerialized] public StayAlivePopupIntent currentIntent;
        }

        [Header("Lobby Popup")]
        [SerializeField] private PopupCanvasBinding lobbyPopup = new PopupCanvasBinding();
        [Header("Overlay Popup")]
        [SerializeField] private PopupCanvasBinding overlayPopup = new PopupCanvasBinding();

        private string lobbyJoinErrorMessage = string.Empty;
        private bool missingReferencesLogged;

        public event Action<StayAlivePopupCanvasType, StayAlivePopupIntent, StayAlivePopupButtonType> ButtonClicked;

        public StayAlivePopupIntent LobbyIntent => lobbyPopup.currentIntent;
        public bool IsOverlayPopupVisible => overlayPopup != null
            && overlayPopup.popupPanel != null
            && overlayPopup.popupPanel.activeInHierarchy;

        private void Awake()
        {
            EnsureReferences();
        }

        public void ShowJoinPopup()
        {
            lobbyJoinErrorMessage = string.Empty;
            ShowLobbyPopup(
                StayAlivePopupIntent.JoinRoom,
                "Join Room",
                "Use this popup for room code entry and the join confirmation button.");
        }

        public void ShowCreatePopup()
        {
            ShowLobbyPopup(
                StayAlivePopupIntent.CreateRoom,
                "Create Room",
                "Use this popup to confirm private room creation and show the room code.");
        }

        public void ShowWaitingForPlayersPopup()
        {
            ShowWaitingForPlayersPopup(1, 4);
        }

        public void ShowWaitingForPlayersPopup(int connectedPlayers, int capacity)
        {
            ShowLobbyPopup(
                StayAlivePopupIntent.WaitingForPlayers,
                "Finding Players",
                BuildRandomMatchDetails(connectedPlayers, capacity));
        }

        public void ShowPrivateRoomWaitingPopup(bool isHost, string roomId, int occupiedSeats, int capacity, bool canStart)
        {
            string title = isHost ? "Create Room" : "Join Room";
            string details = BuildPrivateRoomDetails(isHost, roomId, occupiedSeats, capacity);
            ShowLobbyPopup(
                isHost ? StayAlivePopupIntent.PrivateRoomWaitingHost : StayAlivePopupIntent.PrivateRoomWaitingGuest,
                title,
                details);
            ConfigureButtons(lobbyPopup, isHost ? StayAlivePopupIntent.PrivateRoomWaitingHost : StayAlivePopupIntent.PrivateRoomWaitingGuest, canStart);
        }

        public void ShowNetworkErrorPopup(string message, bool preferOverlay)
        {
            EnsureReferences();

            PopupCanvasBinding binding = ResolveNetworkErrorBinding(preferOverlay);
            ShowPopup(
                binding,
                StayAlivePopupIntent.NetworkError,
                "Network Error",
                string.IsNullOrWhiteSpace(message) ? "Connection failed." : message);
        }

        public void ShowLobbyPopup(StayAlivePopupIntent intent, string title, string details)
        {
            EnsureReferences();
            ShowPopup(lobbyPopup, intent, title, details);
        }

        public void ShowOverlayPopup(StayAlivePopupIntent intent, string title, string details)
        {
            EnsureReferences();
            ShowPopup(overlayPopup, intent, title, details);
        }

        public void ShowPauseOverlayPopup()
        {
            EnsureReferences();
            ShowPopup(overlayPopup, StayAlivePopupIntent.PauseMenu, string.Empty, string.Empty);
        }

        public void SetOverlayVolumeSliderValueWithoutNotify(float value)
        {
            EnsureReferences();
            if (overlayPopup.volumeSlider != null)
            {
                overlayPopup.volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(value));
            }
        }

        public void HideLobbyPopup()
        {
            HidePopup(lobbyPopup);
        }

        public void HideOverlayPopup()
        {
            HidePopup(overlayPopup);
        }

        public void HidePopupByIntent(StayAlivePopupIntent intent)
        {
            if (lobbyPopup.currentIntent == intent)
            {
                HidePopup(lobbyPopup);
            }

            if (overlayPopup.currentIntent == intent)
            {
                HidePopup(overlayPopup);
            }
        }

        public void HideAllPopups()
        {
            HideLobbyPopup();
            HideOverlayPopup();
        }

        public void SetLobbyPrimaryButtonInteractable(bool isInteractable)
        {
            EnsureReferences();
            if (lobbyPopup.currentIntent == StayAlivePopupIntent.JoinRoom && lobbyPopup.joinButton != null)
            {
                lobbyPopup.joinButton.interactable = isInteractable;
                return;
            }

            if (lobbyPopup.confirmButton != null)
            {
                lobbyPopup.confirmButton.interactable = isInteractable;
            }
        }

        public void ShowLobbyJoinError(string message)
        {
            EnsureReferences();
            lobbyJoinErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unable to join room." : message;
            if (lobbyPopup.currentIntent != StayAlivePopupIntent.JoinRoom)
            {
                return;
            }

            ConfigureContent(lobbyPopup, StayAlivePopupIntent.JoinRoom);
        }

        public void ClearLobbyJoinError()
        {
            EnsureReferences();
            lobbyJoinErrorMessage = string.Empty;
            if (lobbyPopup.currentIntent != StayAlivePopupIntent.JoinRoom)
            {
                return;
            }

            ConfigureContent(lobbyPopup, StayAlivePopupIntent.JoinRoom);
        }

        public void OnLobbyPrimaryButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Lobby, StayAlivePopupButtonType.Primary);
        }

        public void OnLobbyCloseButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Lobby, StayAlivePopupButtonType.Close);
            HideLobbyPopup();
        }

        private void EnsureReferences()
        {
            if (missingReferencesLogged)
            {
                return;
            }

            missingReferencesLogged = true;
            LogMissingBindingReferences("Lobby", lobbyPopup);
            LogMissingBindingReferences("Overlay", overlayPopup);
        }

        private PopupCanvasBinding ResolveNetworkErrorBinding(bool preferOverlay)
        {
            if (preferOverlay && overlayPopup.canvasRoot != null && overlayPopup.canvasRoot.activeInHierarchy)
            {
                return overlayPopup;
            }

            return lobbyPopup;
        }

        private void ShowPopup(PopupCanvasBinding binding, StayAlivePopupIntent intent, string title, string details)
        {
            if (binding == null)
            {
                return;
            }

            SetComponentText(binding.titleLabel, title);
            SetComponentText(binding.detailsLabel, details);
            ConfigureDetailsAlignment(binding, intent);
            SetGameObjectActive(binding.titleRoot, true);

            if (binding.popupPanel != null)
            {
                binding.popupPanel.SetActive(true);
            }

            binding.currentIntent = intent;
            ConfigureContent(binding, intent);
            ConfigureButtons(binding, intent, canStart: false);
        }

        private void HidePopup(PopupCanvasBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            if (binding.popupPanel != null)
            {
                binding.popupPanel.SetActive(false);
            }

            binding.currentIntent = StayAlivePopupIntent.None;
            if (ReferenceEquals(binding, lobbyPopup))
            {
                lobbyJoinErrorMessage = string.Empty;
            }

            SetContentState(binding, showDetails: true, showRoomIdInput: false);
            SetGameObjectActive(binding.titleRoot, true);
            SetGameObjectActive(binding.homeButtonRoot, false);
            SetGameObjectActive(binding.volumeSliderRoot, false);
            SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, string.Empty);
            SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, false, true, string.Empty);
            SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, string.Empty);
        }

        private void RaiseButtonClicked(StayAlivePopupCanvasType canvasType, StayAlivePopupButtonType buttonType)
        {
            PopupCanvasBinding binding = canvasType == StayAlivePopupCanvasType.Lobby ? lobbyPopup : overlayPopup;
            ButtonClicked?.Invoke(canvasType, binding.currentIntent, buttonType);
        }

        private void ConfigureButtons(PopupCanvasBinding binding, StayAlivePopupIntent intent, bool canStart)
        {
            if (binding == null)
            {
                return;
            }

            bool useConfirmAsCloseFallback = binding.closeButtonRoot == null && ReferenceEquals(binding, lobbyPopup);

            switch (intent)
            {
                case StayAlivePopupIntent.JoinRoom:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, true, true, "Join");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, useConfirmAsCloseFallback, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, !useConfirmAsCloseFallback, true, "Close");
                    break;

                case StayAlivePopupIntent.PrivateRoomWaitingHost:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, "Join");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, useConfirmAsCloseFallback, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, true, canStart, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, !useConfirmAsCloseFallback, true, "Close");
                    break;

                case StayAlivePopupIntent.PrivateRoomWaitingGuest:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, "Join");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, useConfirmAsCloseFallback, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, !useConfirmAsCloseFallback, true, "Close");
                    break;

                case StayAlivePopupIntent.NetworkError:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, "Join");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, useConfirmAsCloseFallback, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, !useConfirmAsCloseFallback, true, "Close");
                    break;

                case StayAlivePopupIntent.PauseMenu:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, "Join");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, false, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, true, true, "Close");
                    break;

                default:
                    SetButtonState(binding.joinButtonRoot, binding.joinButton, binding.joinButtonLabel, false, true, "Join");
                    SetButtonState(binding.confirmButtonRoot, binding.confirmButton, binding.confirmButtonLabel, useConfirmAsCloseFallback, true, "OK");
                    SetButtonState(binding.startButtonRoot, binding.startButton, binding.startButtonLabel, false, false, "Start");
                    SetButtonState(binding.closeButtonRoot, binding.closeButton, binding.closeButtonLabel, !useConfirmAsCloseFallback, true, "Close");
                    break;
            }
        }

        private void ConfigureContent(PopupCanvasBinding binding, StayAlivePopupIntent intent)
        {
            if (binding == null)
            {
                return;
            }

            bool showRoomIdInput = intent == StayAlivePopupIntent.JoinRoom;
            bool showDetails = !showRoomIdInput;
            bool showTitle = true;
            bool showHomeButton = false;
            bool showVolumeSlider = false;
            if (ReferenceEquals(binding, lobbyPopup) && intent == StayAlivePopupIntent.JoinRoom)
            {
                showDetails = !string.IsNullOrWhiteSpace(lobbyJoinErrorMessage);
                if (showDetails)
                {
                    SetComponentText(binding.detailsLabel, lobbyJoinErrorMessage);
                }
            }

            if (intent == StayAlivePopupIntent.PauseMenu)
            {
                showTitle = false;
                showDetails = false;
                showHomeButton = true;
                showVolumeSlider = true;
            }

            ConfigureDetailsAlignment(binding, intent);
            SetContentState(binding, showDetails, showRoomIdInput);
            SetGameObjectActive(binding.titleRoot, showTitle);
            SetGameObjectActive(binding.homeButtonRoot, showHomeButton);
            SetGameObjectActive(binding.volumeSliderRoot, showVolumeSlider);
        }

        private static void ConfigureDetailsAlignment(PopupCanvasBinding binding, StayAlivePopupIntent intent)
        {
            if (binding == null)
            {
                return;
            }

            bool centerAlign = intent == StayAlivePopupIntent.CreateRoom
                || intent == StayAlivePopupIntent.PrivateRoomWaitingHost
                || intent == StayAlivePopupIntent.PrivateRoomWaitingGuest;
            SetComponentAlignment(binding.detailsLabel, centerAlign);
        }

        private static void SetContentState(PopupCanvasBinding binding, bool showDetails, bool showRoomIdInput)
        {
            if (binding.detailsRoot != null)
            {
                binding.detailsRoot.SetActive(showDetails);
            }

            if (binding.roomIdInputRoot != null)
            {
                binding.roomIdInputRoot.SetActive(showRoomIdInput);
            }
        }

        private static void SetGameObjectActive(GameObject target, bool isActive)
        {
            if (target != null)
            {
                target.SetActive(isActive);
            }
        }

        private static void SetButtonState(GameObject buttonRoot, Button button, Component label, bool isVisible, bool isInteractable, string text)
        {
            if (buttonRoot != null)
            {
                buttonRoot.SetActive(isVisible);
            }

            if (button != null)
            {
                button.interactable = isInteractable;
            }

            if (!string.IsNullOrEmpty(text))
            {
                SetComponentText(label, text);
            }
        }

        private static void SetComponentAlignment(Component component, bool centerAlign)
        {
            if (component == null)
            {
                return;
            }

            if (component is Text text)
            {
                text.alignment = centerAlign ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
                return;
            }

            PropertyInfo property = GetProperty(component.GetType(), "alignment");
            if (property == null || !property.CanWrite)
            {
                return;
            }

            Type propertyType = property.PropertyType;
            if (!propertyType.IsEnum)
            {
                return;
            }

            string enumName = centerAlign ? "Center" : "TopLeft";
            Array values = propertyType.GetEnumValues();
            for (int i = 0; i < values.Length; i++)
            {
                object value = values.GetValue(i);
                if (string.Equals(value.ToString(), enumName, StringComparison.Ordinal))
                {
                    property.SetValue(component, value, null);
                    return;
                }
            }
        }

        private static string BuildPrivateRoomDetails(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? "Creating..." : roomId;
            string statusLine = string.Format("{0}/{1} joined", occupiedSeats, Mathf.Max(1, capacity));
            string instructions = isHost
                ? "Share the room code and start when another player joins."
                : "Waiting for the host to start the match.";

            return string.Format("Room ID\n{0}\n\nPlayers\n{1}\n\n{2}", normalizedRoomId, statusLine, instructions);
        }

        private static string BuildRandomMatchDetails(int connectedPlayers, int capacity)
        {
            int normalizedCapacity = Mathf.Max(1, capacity);
            int normalizedConnectedPlayers = Mathf.Clamp(connectedPlayers, 0, normalizedCapacity);
            return string.Format("Players\n{0}/{1} connected\n\nSearching for available players.", normalizedConnectedPlayers, normalizedCapacity);
        }

        private static void LogMissingBindingReferences(string bindingName, PopupCanvasBinding binding)
        {
            if (binding == null)
            {
                Debug.LogWarning($"StayAlivePopupManager: {bindingName} popup binding is missing.");
                return;
            }

            if (binding.canvasRoot == null)
            {
                Debug.LogWarning($"StayAlivePopupManager: {bindingName} popup canvas root is not assigned.", null);
            }

            if (binding.popupPanel == null)
            {
                Debug.LogWarning($"StayAlivePopupManager: {bindingName} popup panel is not assigned.", null);
            }

            if (binding.titleLabel == null)
            {
                Debug.LogWarning($"StayAlivePopupManager: {bindingName} popup title label is not assigned.", null);
            }

            if (binding.detailsLabel == null)
            {
                Debug.LogWarning($"StayAlivePopupManager: {bindingName} popup details label is not assigned.", null);
            }
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

        private static PropertyInfo GetProperty(Type type, string propertyName)
        {
            return type != null
                ? type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                : null;
        }
    }
}
