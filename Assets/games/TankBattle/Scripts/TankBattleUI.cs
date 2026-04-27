using TMPro;
using TankBattle.Networking;
using UnityEngine;

namespace TankBattle
{
    public class TankBattleUI : MonoBehaviour
    {
        [Header("Player 1")]
        [SerializeField] private Transform player1HealthContainer;
        [SerializeField] private TextMeshProUGUI player1ScoreText;

        [Header("Player 2")]
        [SerializeField] private Transform player2HealthContainer;
        [SerializeField] private TextMeshProUGUI player2ScoreText;

        [Header("Game Over")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject player1WinPanel;
        [SerializeField] private GameObject player2WinPanel;
        [SerializeField] private GameObject gameplayUiRoot;

        private TankBattleGameManager gameManager;
        private TankBattlePlayer player1;
        private TankBattlePlayer player2;

        private int player1Health = 3;
        private int player2Health = 3;
        private int player1Score;
        private int player2Score;
        private bool gameIsOver;
        private int winnerPlayerNumber;

        public int WinnerPlayerNumber => winnerPlayerNumber;

        public void Initialize(TankBattleGameManager manager, TankBattlePlayer p1, TankBattlePlayer p2)
        {
            gameManager = manager;
            player1 = p1;
            player2 = p2;
            EnsureGameplayRootReference();
            player1Health = 3;
            player2Health = 3;
            player1Score = 0;
            player2Score = 0;
            gameIsOver = false;
            winnerPlayerNumber = 0;

            ApplyGameOverPanels();
            UpdateHealthDisplay();
            UpdateScoreDisplays();
        }

        public void SetGameplayUiVisible(bool visible)
        {
            EnsureGameplayRootReference();
            if (gameplayUiRoot != null)
            {
                gameplayUiRoot.SetActive(visible);
            }
            else
            {
                gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            ApplyGameOverPanels();
        }

        public void OnPlayerHit(TankBattlePlayer hitPlayer, TankBattlePlayer shooter)
        {
            if (gameIsOver)
            {
                return;
            }

            if (gameManager != null && gameManager.IsOnlineMatch && !gameManager.HasSimulationAuthority)
            {
                bool shooterIsLocalHuman = shooter != null && shooter.ControlMode == TankBattlePlayerControlMode.LocalHuman;
                if (!shooterIsLocalHuman)
                {
                    return;
                }
            }

            ApplyHitOutcome(hitPlayer);
        }

        public void ApplyNetworkHitByChair(int victimChairId)
        {
            if (gameIsOver)
            {
                return;
            }

            TankBattlePlayer victim = victimChairId == 0 ? player1 : victimChairId == 1 ? player2 : null;
            if (victim == null)
            {
                return;
            }

            ApplyHitOutcome(victim);
        }

        private void ApplyHitOutcome(TankBattlePlayer hitPlayer)
        {
            if (hitPlayer == player1)
            {
                player1Health--;
                player2Score++;
                UpdateHealthDisplay();
                UpdateScoreDisplays();

                if (player1Health <= 0)
                {
                    GameOver(2);
                }
            }
            else if (hitPlayer == player2)
            {
                player2Health--;
                player1Score++;
                UpdateHealthDisplay();
                UpdateScoreDisplays();

                if (player2Health <= 0)
                {
                    GameOver(1);
                }
            }
        }

        public TankBattleUiStateSnapshot BuildUiSnapshot()
        {
            return new TankBattleUiStateSnapshot
            {
                player1Health = player1Health,
                player2Health = player2Health,
                player1Score = player1Score,
                player2Score = player2Score,
                gameIsOver = gameIsOver,
                winnerPlayerNumber = winnerPlayerNumber
            };
        }

        public void ApplyOnlineSnapshot(TankBattleUiStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            player1Health = Mathf.Max(0, snapshot.player1Health);
            player2Health = Mathf.Max(0, snapshot.player2Health);
            player1Score = Mathf.Max(0, snapshot.player1Score);
            player2Score = Mathf.Max(0, snapshot.player2Score);
            gameIsOver = snapshot.gameIsOver;
            winnerPlayerNumber = snapshot.winnerPlayerNumber;

            UpdateHealthDisplay();
            UpdateScoreDisplays();
            ApplyGameOverPanels();
        }

        public void ShowWinnerByChair(int winningChairId)
        {
            int winner = winningChairId == 0 ? 1 : winningChairId == 1 ? 2 : 0;
            if (winner <= 0)
            {
                return;
            }

            gameIsOver = true;
            winnerPlayerNumber = winner;
            ApplyGameOverPanels();
        }

        private void UpdateHealthDisplay()
        {
            if (player1HealthContainer != null)
            {
                for (int i = 0; i < player1HealthContainer.childCount; i++)
                {
                    player1HealthContainer.GetChild(i).gameObject.SetActive(i < player1Health);
                }
            }

            if (player2HealthContainer != null)
            {
                for (int i = 0; i < player2HealthContainer.childCount; i++)
                {
                    player2HealthContainer.GetChild(i).gameObject.SetActive(i < player2Health);
                }
            }
        }

        private void UpdateScoreDisplays()
        {
            if (player1ScoreText != null)
            {
                player1ScoreText.text = "<color=#0E83FE>" + player1Score + "</color>";
            }

            if (player2ScoreText != null)
            {
                player2ScoreText.text = "<color=#FE2525>" + player2Score + "</color>";
            }
        }

        private void GameOver(int winner)
        {
            gameIsOver = true;
            winnerPlayerNumber = winner;
            ApplyGameOverPanels();

            if (gameManager != null)
            {
                gameManager.OnGameOver(winner);
            }
        }

        private void ApplyGameOverPanels()
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(gameIsOver);
            }

            if (player1WinPanel != null)
            {
                player1WinPanel.SetActive(gameIsOver && winnerPlayerNumber == 1);
            }

            if (player2WinPanel != null)
            {
                player2WinPanel.SetActive(gameIsOver && winnerPlayerNumber == 2);
            }
        }

        public void OnGameRestart()
        {
            if (gameManager != null)
            {
                gameManager.OnBackToLobby();
            }
        }

        private void EnsureGameplayRootReference()
        {
            if (gameplayUiRoot == null)
            {
                GameObject root = GameObject.Find("InGameScoreCanvas");
                gameplayUiRoot = root != null ? root : gameObject;
            }
        }
    }
}
