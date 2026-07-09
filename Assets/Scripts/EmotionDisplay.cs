using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

//w7: previous version was just floating colored text which looked bad in passthrough
//semi-transparent tinted panel behind the label that shifts color with the emotion
//color-per-emotion pattern from semsioğlu & yantaç 2022 emote tool (ah2022)
//w8: now shows both headsets at once, small YOU panel plus a bigger PARTNER panel since thats the actual point of the system
//also shows confidence as a percent next to the label and falls back to a no signal state if the partner goes quiet
public class EmotionDisplay:MonoBehaviour
{
  [SerializeField] private FaceEmotionClassifier classifier;

  //your own emotion, small panel, always live off the local classifier no network involved
  [SerializeField] private TextMeshProUGUI selfLabel;
  [SerializeField] private Image selfPanel;

  //partners emotion, bigger panel, comes over firebase
  [SerializeField] private TextMeshProUGUI partnerLabel;
  [SerializeField] private Image partnerPanel;

  public DatabaseManager databaseManager;

  //written into directly by DatabaseManager.ReadCurrentPartnerEmotion
  public string currentPartnerEmotion = "Neutral";
  public float currentPartnerConfidence = 0f;
  public long currentPartnerTimestampMs = 0L;

  //how long with no update before we call it a dropped connection instead of showing stale data
  [SerializeField] private float signalTimeoutSeconds = 3f;

  private string lastSelfShown = "";
  private string lastPartnerShown = "";

  //full brightness emotion colors for the text, also used at 1/4 brightness for the panel tint
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
    {"No Signal", new Color32( 90,  90,  90, 255)},
  };

  void Update()
  {
    if(classifier == null)
      return;

    //still firing every frame like before, tested on device and the instant sync is worth keeping
    if(databaseManager != null)
    {
      StartCoroutine(databaseManager.LogCurrentEmotion(classifier.CurrentEmotion, classifier.CurrentConfidence));
      StartCoroutine(databaseManager.ReadCurrentPartnerEmotion());
    }

    UpdateSelfPanel();
    UpdatePartnerPanel();
  }

  //your own panel, small, driven straight off the local classifier no network round trip needed
  private void UpdateSelfPanel()
  {
    if(selfLabel == null)
      return;

    string shown = $"{classifier.CurrentEmotion}:{Mathf.RoundToInt(classifier.CurrentConfidence * 100)}";
    if(shown == lastSelfShown)
      return;

    Color32 c = emotionColors.ContainsKey(classifier.CurrentEmotion) ? emotionColors[classifier.CurrentEmotion] : new Color32(255, 255, 255, 255);
    ApplyColor(selfPanel, c);
    selfLabel.text = $"You: <color=#{ColorUtility.ToHtmlStringRGBA(c)}>{classifier.CurrentEmotion}</color> ({Mathf.RoundToInt(classifier.CurrentConfidence * 100)}%)";
    lastSelfShown = shown;

    StartCoroutine(FadeIn(selfPanel));
  }

  //partners panel, bigger, comes from firebase, falls back to no signal if the timestamp is too old
  private void UpdatePartnerPanel()
  {
    if(partnerLabel == null)
      return;

    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    bool signalLost = currentPartnerTimestampMs == 0 || (nowMs - currentPartnerTimestampMs) > signalTimeoutSeconds * 1000;

    string displayEmotion = signalLost ? "No Signal" : currentPartnerEmotion;
    string shown = signalLost ? "No Signal" : $"{currentPartnerEmotion}:{Mathf.RoundToInt(currentPartnerConfidence * 100)}";
    if(shown == lastPartnerShown)
      return;

    Color32 c = emotionColors.ContainsKey(displayEmotion) ? emotionColors[displayEmotion] : new Color32(255, 255, 255, 255);
    ApplyColor(partnerPanel, c);

    partnerLabel.text = signalLost
      ? $"Partner: <color=#{ColorUtility.ToHtmlStringRGBA(c)}>No Signal</color>"
      : $"Partner: <color=#{ColorUtility.ToHtmlStringRGBA(c)}>{currentPartnerEmotion}</color> ({Mathf.RoundToInt(currentPartnerConfidence * 100)}%)";
    lastPartnerShown = shown;

    StartCoroutine(FadeIn(partnerPanel));
  }

  //panel gets the same hue but dimmed to ~1/4 brightness at 80% opacity so it looks like a dark frosted card
  private void ApplyColor(Image panel, Color32 c)
  {
    if(panel != null)
    {
      panel.color = new Color32(
        (byte)(c.r / 4),
        (byte)(c.g / 4),
        (byte)(c.b / 4),
        200
      );
    }
  }

  //quick fade in on the panel whenever a label actually changes instead of just snapping to the new tint
  private IEnumerator FadeIn(Image panel, float duration = 0.2f)
  {
    if(panel == null)
      yield break;

    float t = 0f;
    Color target = panel.color;
    while(t < duration)
    {
      t += Time.deltaTime;
      float alpha = Mathf.Clamp01(t / duration);
      panel.color = new Color(target.r, target.g, target.b, target.a * alpha);
      yield return null;
    }
    panel.color = target;
  }
}