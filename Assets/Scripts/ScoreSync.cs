using UnityEngine;

//thin wrapper now, doesnt touch firebase directly
//reuses the same connection and slot claim DatabaseManager already sets up for emotion sync, instead of duplicating it
//old version had its own separate FirebaseApp.DefaultInstance call and its own slot claiming transaction
//that raced against DatabaseManagers own claim on the same presence nodes, and had no DatabaseUrl configured, hence the exception
public class ScoreSync:MonoBehaviour
{
    [SerializeField] private PianoTileGame game;
    [SerializeField] private DatabaseManager databaseManager;

    int lastWrittenScore = -1;

    //ScoreDisplay reads this the same way it did before, just pulled from databaseManager now instead of tracked locally
    public int PartnerScore => databaseManager != null ? databaseManager.currentPartnerScore : 0;

    void Update()
    {
        if(databaseManager == null || game == null)
            return;

        if(game.Score != lastWrittenScore)
        {
            lastWrittenScore = game.Score;
            StartCoroutine(databaseManager.LogCurrentScore(game.Score));
        }

        StartCoroutine(databaseManager.ReadCurrentPartnerScore());
    }
}