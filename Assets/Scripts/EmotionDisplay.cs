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
//w9: switched both panels from CurrentEmotion to DisplayEmotion
//CurrentEmotion is the raw unsmoothed per frame argmax, thats what was causing the constant flicker
//DisplayEmotion only has a value once a smoothed score clears spikeThreshold, held for holdSeconds, blank otherwise
//blank means hide the panel completely, not fall back to neutral, thats the actual requirement
//also switched the firebase write to send DisplayEmotion/DisplayConfidence instead of Current, so the partner sees the gated version too
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
  //blank string here means the partner has no strong emotion triggering right now, same meaning as DisplayEmotion locally
  public string currentPartnerEmotion = "";
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

  void Start()
  {
    //start with nothing showing, dont rely on whatever state the panels happened to be left in in the editor
    SetSelfVisible(false);
    SetPartnerVisible(false);
  }

  void Update()
  {
    if(classifier == null)
      return;

    //still firing every frame like before, tested on device and the instant sync is worth keeping
    //now sending DisplayEmotion/DisplayConfidence so the partner sees the same gated, held value, not the raw flickering one
    if(databaseManager != null)
    {
      StartCoroutine(databaseManager.LogCurrentEmotion(classifier.DisplayEmotion, classifier.DisplayConfidence));
      StartCoroutine(databaseManager.ReadCurrentPartnerEmotion());
    }

    UpdateSelfPanel();
    UpdatePartnerPanel();
  }

  //your own panel, small, driven straight off the local classifier no network round trip needed
  //hides completely when DisplayEmotion is blank instead of ever falling back to neutral
  private void UpdateSelfPanel()
  {
    if(selfLabel == null)
      return;

    if(string.IsNullOrEmpty(classifier.DisplayEmotion))
    {
      if(lastSelfShown == "")
        return; //already hidden, nothing to do

      SetSelfVisible(false);
      lastSelfShown = "";
      return;
    }

    string shown = $"{classifier.DisplayEmotion}:{Mathf.RoundToInt(classifier.DisplayConfidence * 100)}";
    if(shown == lastSelfShown)
      return;

    SetSelfVisible(true);

    Color32 c = emotionColors.ContainsKey(classifier.DisplayEmotion) ? emotionColors[classifier.DisplayEmotion] : new Color32(255, 255, 255, 255);
    ApplyColor(selfPanel, c);
    selfLabel.text = $"You: <color=#{ColorUtility.ToHtmlStringRGBA(c)}>{classifier.DisplayEmotion}</color> ({Mathf.RoundToInt(classifier.DisplayConfidence * 100)}%)";
    lastSelfShown = shown;

    StartCoroutine(FadeIn(selfPanel));
  }

  //partners panel, bigger, comes from firebase
  //three states now, signal lost shows the no signal card, blank emotion hides the panel same as self, otherwise shows normally
  private void UpdatePartnerPanel()
  {
    if(partnerLabel == null)
      return;

    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    bool signalLost = currentPartnerTimestampMs == 0 || (nowMs - currentPartnerTimestampMs) > signalTimeoutSeconds * 1000;

    if(signalLost)
    {
      if(lastPartnerShown == "No Signal")
        return;

      SetPartnerVisible(true);
      Color32 c = emotionColors["No Signal"];
      ApplyColor(partnerPanel, c);
      partnerLabel.text = $"Partner: <color=#{ColorUtility.ToHtmlStringRGBA(c)}>No Signal</color>";
      lastPartnerShown = "No Signal";
      StartCoroutine(FadeIn(partnerPanel));
      return;
    }

    if(string.IsNullOrEmpty(currentPartnerEmotion))
    {
      if(lastPartnerShown == "")
        return; //already hidden

      SetPartnerVisible(false);
      lastPartnerShown = "";
      return;
    }

    string shown = $"{currentPartnerEmotion}:{Mathf.RoundToInt(currentPartnerConfidence * 100)}";
    if(shown == lastPartnerShown)
      return;

    SetPartnerVisible(true);

    Color32 cc = emotionColors.ContainsKey(currentPartnerEmotion) ? emotionColors[currentPartnerEmotion] : new Color32(255, 255, 255, 255);
    ApplyColor(partnerPanel, cc);
    partnerLabel.text = $"Partner: <color=#{ColorUtility.ToHtmlStringRGBA(cc)}>{currentPartnerEmotion}</color> ({Mathf.RoundToInt(currentPartnerConfidence * 100)}%)";
    lastPartnerShown = shown;

    StartCoroutine(FadeIn(partnerPanel));
  }

  //toggles both the background panel and the label together, covers it either way whether the label is a child of the panel or a sibling
  private void SetSelfVisible(bool visible)
  {
    if(selfPanel != null) selfPanel.gameObject.SetActive(visible);
    if(selfLabel != null) selfLabel.gameObject.SetActive(visible);
  }

  private void SetPartnerVisible(bool visible)
  {
    if(partnerPanel != null) partnerPanel.gameObject.SetActive(visible);
    if(partnerLabel != null) partnerLabel.gameObject.SetActive(visible);
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