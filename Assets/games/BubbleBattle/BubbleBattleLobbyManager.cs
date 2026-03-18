using UnityEngine;

namespace BubbleBattle
{
    public class BubbleBattleLobbyManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private GameObject lobbyRoot;
        [SerializeField] private GameObject gameContainer;
        [SerializeField] private BubbleBattleGameManager gameManager;

        [Header("Local Match")]
        [SerializeField] private bool startLocalGameOnStart;
        [SerializeField, Min(2)] private int offlinePlayerCount = 2;
        [SerializeField, Min(1)] private int offlineHumanPlayers = 1;

        private void Start()
        {
            if (startLocalGameOnStart)
            {
                StartLocalMatch();
                return;
            }

            ShowLobby();
        }

        public void ShowLobby()
        {
            SetSceneState(inMatch: false);
        }

        public void StartLocalMatch()
        {
            if (gameManager == null)
            {
                Debug.LogError("BubbleBattleLobbyManager: Missing serialized reference `gameManager`.", this);
                return;
            }

            SetSceneState(inMatch: true);
            gameManager.StartLocalGame(
                Mathf.Max(2, offlinePlayerCount),
                Mathf.Clamp(offlineHumanPlayers, 1, Mathf.Max(2, offlinePlayerCount)));
        }

        public void ReturnToLobby()
        {
            if (gameManager != null)
            {
                gameManager.StopMatch();
            }

            SetSceneState(inMatch: false);
        }

        private void SetSceneState(bool inMatch)
        {
            if (lobbyRoot != null)
            {
                lobbyRoot.SetActive(!inMatch);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(inMatch);
            }
        }
    }
}
