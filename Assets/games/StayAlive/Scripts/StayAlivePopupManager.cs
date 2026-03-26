using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        Custom = 7
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
            public string canvasName;
            public GameObject canvasRoot;
            public GameObject popupPanel;
            public GameObject popupBoard;
            public Component titleLabel;
            public Component detailsLabel;

            [NonSerialized] public StayAlivePopupIntent currentIntent;
        }

        [Header("Popup Object Names")]
        [SerializeField] private string popupPanelName = "PopUpPanel";
        [SerializeField] private string popupBoardName = "PopUpBoard";
        [SerializeField] private string popupTitleName = "PopUpTitle";
        [SerializeField] private string popupDetailsName = "Details";

        [Header("Scene Canvases")]
        [SerializeField] private PopupCanvasBinding lobbyPopup = new PopupCanvasBinding { canvasName = "LobbyCanvas" };
        [SerializeField] private PopupCanvasBinding overlayPopup = new PopupCanvasBinding { canvasName = "InGameOverLayCanvas" };

        public event Action<StayAlivePopupCanvasType, StayAlivePopupIntent, StayAlivePopupButtonType> ButtonClicked;

        public StayAlivePopupIntent LobbyIntent => lobbyPopup.currentIntent;
        public StayAlivePopupIntent OverlayIntent => overlayPopup.currentIntent;

        private void Awake()
        {
            EnsureReferences();
        }

        public void ShowJoinPopup()
        {
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
            ShowLobbyPopup(
                StayAlivePopupIntent.WaitingForPlayers,
                "Finding Players",
                "Searching for available players.");
        }

        public void ShowPrivateRoomWaitingPopup(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            string title = isHost ? "Create Room" : "Join Room";
            string details = BuildPrivateRoomDetails(isHost, roomId, occupiedSeats, capacity);
            ShowLobbyPopup(
                isHost ? StayAlivePopupIntent.PrivateRoomWaitingHost : StayAlivePopupIntent.PrivateRoomWaitingGuest,
                title,
                details);
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

        public void OnLobbyPrimaryButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Lobby, StayAlivePopupButtonType.Primary);
        }

        public void OnLobbySecondaryButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Lobby, StayAlivePopupButtonType.Secondary);
            HideLobbyPopup();
        }

        public void OnLobbyCloseButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Lobby, StayAlivePopupButtonType.Close);
            HideLobbyPopup();
        }

        public void OnOverlayPrimaryButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Overlay, StayAlivePopupButtonType.Primary);
        }

        public void OnOverlaySecondaryButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Overlay, StayAlivePopupButtonType.Secondary);
            HideOverlayPopup();
        }

        public void OnOverlayCloseButtonClicked()
        {
            RaiseButtonClicked(StayAlivePopupCanvasType.Overlay, StayAlivePopupButtonType.Close);
            HideOverlayPopup();
        }

        private void EnsureReferences()
        {
            EnsureBinding(lobbyPopup);
            EnsureBinding(overlayPopup);
        }

        private void EnsureBinding(PopupCanvasBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            if (binding.canvasRoot == null)
            {
                Transform canvasTransform = FindInActiveScene(binding.canvasName);
                if (canvasTransform != null)
                {
                    binding.canvasRoot = canvasTransform.gameObject;
                }
            }

            if (binding.canvasRoot == null)
            {
                return;
            }

            if (binding.popupPanel == null)
            {
                Transform popupPanelTransform = FindInChildren(binding.canvasRoot.transform, popupPanelName);
                if (popupPanelTransform != null)
                {
                    binding.popupPanel = popupPanelTransform.gameObject;
                }
            }

            if (binding.popupPanel == null)
            {
                return;
            }

            if (binding.popupBoard == null)
            {
                Transform popupBoardTransform = FindInChildren(binding.popupPanel.transform, popupBoardName);
                if (popupBoardTransform != null)
                {
                    binding.popupBoard = popupBoardTransform.gameObject;
                }
            }

            if (binding.titleLabel == null)
            {
                Transform titleTransform = FindInChildren(binding.popupPanel.transform, popupTitleName);
                if (titleTransform != null)
                {
                    binding.titleLabel = FindTextComponent(titleTransform.gameObject);
                }
            }

            if (binding.detailsLabel == null)
            {
                Transform detailsTransform = FindInChildren(binding.popupPanel.transform, popupDetailsName);
                if (detailsTransform != null)
                {
                    binding.detailsLabel = FindTextComponent(detailsTransform.gameObject);
                }
            }
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

            if (binding.popupPanel != null)
            {
                binding.popupPanel.SetActive(true);
            }

            binding.currentIntent = intent;
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
        }

        private void RaiseButtonClicked(StayAlivePopupCanvasType canvasType, StayAlivePopupButtonType buttonType)
        {
            PopupCanvasBinding binding = canvasType == StayAlivePopupCanvasType.Lobby ? lobbyPopup : overlayPopup;
            ButtonClicked?.Invoke(canvasType, binding.currentIntent, buttonType);
        }

        private static string BuildPrivateRoomDetails(bool isHost, string roomId, int occupiedSeats, int capacity)
        {
            string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? "Creating..." : roomId;
            string statusLine = string.Format("Players: {0}/{1}", occupiedSeats, Mathf.Max(1, capacity));
            string instructions = isHost
                ? "Share the room code and start when another player joins."
                : "Waiting for the host to start the match.";

            return string.Format("Room ID: {0}\n{1}\n{2}", normalizedRoomId, statusLine, instructions);
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
