using UnityEngine;
using TMPro;

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

        private TankBattleGameManager gameManager;
        private TankBattlePlayer player1;
        private TankBattlePlayer player2;
        private int player1Health = 3;
        private int player2Health = 3;
        private int player1Score = 0;
        private int player2Score = 0;

        public void Initialize(TankBattleGameManager manager, TankBattlePlayer p1, TankBattlePlayer p2)
        {
            gameManager = manager;
            player1 = p1;
            player2 = p2;
            player1Health = 3;
            player2Health = 3;
            player1Score = 0;
            player2Score = 0;
            UpdateHealthDisplay();
            UpdateScoreDisplays();
        }

        public void OnPlayerHit(TankBattlePlayer hitPlayer, TankBattlePlayer shooter)
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
                player1ScoreText.text = "<color=#0E83FE>" + player1Score.ToString() + "</color>";
            }

            if (player2ScoreText != null)
            {
                player2ScoreText.text = "<color=#FE2525>" + player2Score.ToString() + "</color>";
            }
        }

        private void GameOver(int winnerPlayerNumber)
        {
            Debug.Log("GAME OVER! Player " + winnerPlayerNumber + " WINS!");
            
            if (gameManager != null)
            {
                gameManager.OnGameOver(winnerPlayerNumber);
            }
        }
    }
}
