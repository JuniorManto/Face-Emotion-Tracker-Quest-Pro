using UnityEngine;
using System.Collections;

public class RiggingSetup:MonoBehaviour
{
    [SerializeField] private DatabaseManager databaseManager;
    [SerializeField] private PianoTileGame game;

    [SerializeField] private float advantagedPercent = 1.2f;
    [SerializeField] private float disadvantagedPercent = 0.7f;

    bool applied = false;
    bool requestInFlight = false;

    void Update()
    {
        if(applied || databaseManager == null || game == null)
            return;

        if(databaseManager.MyId == 0)
            return;

        if(databaseManager.advantagedId == 0)
        {
            if(requestInFlight)
                return;

            requestInFlight = true;
            if(databaseManager.MyId == 1)
                StartCoroutine(AssignThenClear());
            else
                StartCoroutine(ReadThenClear());
            return;
        }

        float percent = databaseManager.MyId == databaseManager.advantagedId ? advantagedPercent : disadvantagedPercent;
        game.SetHitWindowPercent(percent);
        applied = true;
        Debug.Log($"rigging applied, myId {databaseManager.MyId} advantagedId {databaseManager.advantagedId} percent {percent}");
    }

    IEnumerator AssignThenClear()
    {
        yield return databaseManager.AssignRigging();
        requestInFlight = false;
    }

    IEnumerator ReadThenClear()
    {
        yield return databaseManager.ReadRigging();
        requestInFlight = false;
    }
}