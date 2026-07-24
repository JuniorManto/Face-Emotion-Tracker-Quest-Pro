using UnityEngine;

//advantage is fixed now, not randomized, not synced through firebase at all
//headset tape 1 is always advantaged, headset tape 2 is always disadvantaged, every session, no exceptions
//physicalHeadsetId is set once per device in the inspector to match its tape, baked into that headsets build
public class RiggingSetup:MonoBehaviour
{
    [SerializeField] private PianoTileGame game;

    //set this to match the tape on this specific headset, 1 or 2, do it once per device and leave it
    [SerializeField] private int physicalHeadsetId = 1;

    [SerializeField] private float advantagedPercent = 1.2f;
    [SerializeField] private float disadvantagedPercent = 0.7f;

    void Start()
    {
        bool isAdvantaged = physicalHeadsetId == 1;
        float percent = isAdvantaged ? advantagedPercent : disadvantagedPercent;
        game.SetHitWindowPercent(percent);
        Debug.Log($"rigging applied, physical headset {physicalHeadsetId}, advantaged {isAdvantaged}, percent {percent}");
    }
}