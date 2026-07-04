using UnityEngine;
using TMPro;

public class DebugDisplay : MonoBehaviour
{
    [SerializeField] private FaceEmotionClassifier classifier;
    [SerializeField] private TextMeshProUGUI debugText;
    
    // Update is called once per frame
    void Update()
    {
        if (classifier == null || debugText == null)
            return;

        string output = "";
        for (int i = 0; i < classifier.LastScores.Length; i++)
        {
            output += $"{classifier.emotionNames[i]}: {classifier.LastScores[i]:F2}\n";
        }

        debugText.text = output;
    }
}
