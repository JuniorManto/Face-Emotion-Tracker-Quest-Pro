using UnityEngine;
using TMPro;

//shows both players scores the same way emotions are shown, a you panel and a partner panel
public class ScoreDisplay:MonoBehaviour
{
    [SerializeField] private PianoTileGame game;
    [SerializeField] private ScoreSync scoreSync;
    [SerializeField] private TextMeshProUGUI yourScoreText;
    [SerializeField] private TextMeshProUGUI partnerScoreText;

    void Update()
    {
        if(game != null && yourScoreText != null)
            yourScoreText.text = "You: " + game.Score;

        if(scoreSync != null && partnerScoreText != null)
            partnerScoreText.text = "Partner: " + scoreSync.PartnerScore;
    }
}