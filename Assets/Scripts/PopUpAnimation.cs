using UnityEngine;
using System.Collections;

//quick scale bounce whenever this object gets activated
//makes the point and penalty popups feel like an actual hit instead of text just appearing
//attach to PointPopup and PenaltyPopup, nothing to wire, runs automatically on enable
public class PopupPunch:MonoBehaviour
{
    [SerializeField] private float punchDuration = 0.15f;
    [SerializeField] private float overshoot = 1.3f;

    void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(Punch());
    }

    IEnumerator Punch()
    {
        Vector3 baseScale = Vector3.one;
        Vector3 bigScale = baseScale * overshoot;

        float t = 0f;
        while(t < punchDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(baseScale, bigScale, t / punchDuration);
            yield return null;
        }

        t = 0f;
        while(t < punchDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(bigScale, baseScale, t / punchDuration);
            yield return null;
        }

        transform.localScale = baseScale;
    }
}