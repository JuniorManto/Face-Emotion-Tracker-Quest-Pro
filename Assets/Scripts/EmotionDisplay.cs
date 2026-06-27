using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

//updated for w7 ar overlay
//previous version was just floating colored text which looked bad in passthrough
//now theres a semi-transparent tinted panel behind the label that shifts color with the emotion
//approach: script drives both Image.color on the panel and TextMeshProUGUI.color on the label
//color-per-emotion pattern from semsioğlu & yantaç 2022 emote tool (ah2022)
public class EmotionDisplay:MonoBehaviour
{
  [SerializeField] private FaceEmotionClassifier classifier;

  //the tmp label that shows the emotion word
  [SerializeField] private TextMeshProUGUI emotionLabel;

  //the Image component on the panel background behind the text
  //drag the panel gameobject (with Image component) in here in the inspector
  [SerializeField] private Image backgroundPanel;

  private string lastEmotion = "";

  //full brightness emotion colors for the text
  //also used at 1/6 brightness for the panel tint so its the same hue but dark
  private static readonly Dictionary<string, Color32> emotionColors = new Dictionary<string, Color32>
  {
    {"Happiness", new Color32(255, 215,   0, 255)},
    {"Sadness",   new Color32( 68, 136, 255, 255)},
    {"Surprise",  new Color32(255, 140,   0, 255)},
    {"Fear",      new Color32(180,  68, 255, 255)},
    {"Anger",     new Color32(255,  51,  51, 255)},
    {"Disgust",   new Color32( 68, 187,  68, 255)},
    {"Contempt",  new Color32(170, 170, 170, 255)},
    {"Neutral",   new Color32(200, 200, 200, 255)},
  };

  void Update()
  {
    if(classifier == null || emotionLabel == null)
      return;

    string current = classifier.CurrentEmotion;

    if(current == lastEmotion)
      return;

    Color32 c = emotionColors.ContainsKey(current) ? emotionColors[current] : new Color32(255, 255, 255, 255);

    //text gets the full saturated color
    emotionLabel.color = c;

    //panel gets the same hue but dimmed to ~1/6 brightness at 80% opacity
    //so it looks like a dark frosted card that tints the same color as the emotion
    if(backgroundPanel != null)
    {
      backgroundPanel.color = new Color32(
        (byte)(c.r / 6),
        (byte)(c.g / 6),
        (byte)(c.b / 6),
        200
      );
    }

    lastEmotion = current;
  }
}
