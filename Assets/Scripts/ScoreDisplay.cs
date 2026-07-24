using UnityEngine;
using TMPro;

//shows both players scores, a you panel and a partner panel
//pushes your score to firebase whenever it changes, polls for the partners score on an interval
//reads DatabaseManager directly now, ScoreSync.cs is gone, it was claiming its own separate presence slots
public class ScoreDisplay:MonoBehaviour
{
    [SerializeField] private PianoTileGame game;
    [SerializeField] private DatabaseManager dbManager;
    [SerializeField] private TextMeshProUGUI yourScoreText;
    [SerializeField] private TextMeshProUGUI partnerScoreText;

    //firebase reads/writes arent free, this is how often we check for the partners score, not every frame
    [SerializeField] private float partnerPollSeconds = 1f;

    int lastWrittenScore = -1;
    float pollTimer = 0f;

    void Update()
    {
        if(game == null || dbManager == null)
            return;

        if(game.Score != lastWrittenScore)
        {
            lastWrittenScore = game.Score;
            StartCoroutine(dbManager.LogCurrentScore(game.Score));
        }

        pollTimer -= Time.deltaTime;
        if(pollTimer <= 0f)
        {
            pollTimer = partnerPollSeconds;
            StartCoroutine(dbManager.ReadCurrentPartnerScore());
        }

        if(yourScoreText != null)
            yourScoreText.text = "You: " + game.Score;

        if(partnerScoreText != null)
            partnerScoreText.text = "Partner: " + dbManager.currentPartnerScore;
    }
}