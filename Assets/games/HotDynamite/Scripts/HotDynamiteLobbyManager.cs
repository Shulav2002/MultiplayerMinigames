using UnityEngine;
using UnityEngine.SceneManagement;

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

        private void Start()
        {
            // Ensure proper initial state
            if (lobbyCanvas != null)
                lobbyCanvas.SetActive(true);
            
            if (gameContainer != null)
                gameContainer.SetActive(false);
        }

        /// <summary>
        /// Call this function from your "Play" UI Button.
        /// It hides the lobby, turns on the game container, and starts the game.
        /// </summary>
        public void OnPlayButtonClicked()
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
                gameManager.StartGameLocal();
            }
            else
            {
                Debug.LogError("HotDynamiteLobbyManager: HotDynamiteGameManager reference is missing!");
            }
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
    }
}
