using UnityEngine;

//im gonna rehaul the script and try something else because after doing more research, this looks like a better idea. if im wrong and this doesnt work in the lab, i can just revert to the previous script
//the first idea (see git history) was simple threshold rules: if browLower > 0.3 then anger, etc.
//the problem with that was basically a first match wins scenario, so if two emotions overlapped the order decided it, not the strongest signal
//after researching this concern a bit more, i switched to something i found in a paper called cosine similarity instead
//the idea is you build a vector of the face's action units and compare it to a template vector per emotion
//the technique comes from a 2026 paper (Sensors, Aldenhoven et al., https://doi.org/10.3390/s26031060) on ARKit blendshapes (cosine similarity scored 68% and beat the average human rater of 59%)
//the actual muscle combos in the templates come from Ekman and Friesens EM-FACS
//i wrote the code myself for the Quest Pro, the paper studies iPhone ARKit and doesnt give Unity code
//contempt is kept as a separate asymmetry check since cosine similarity cant catch a one-sided lip pull
//neutral used to be its own vector plus a separate blank face check plus a muscle gate, all three could independently force neutral and they kept fighting each other
//simplified: neutral is now just what happens when nothing clears its own threshold, thats it
public class FaceEmotionClassifier:MonoBehaviour
{
  [SerializeField] private OVRFaceExpressions faceExpr;

  public string CurrentEmotion{get; private set;} = "Neutral";
  public float CurrentConfidence{get; private set;} = 0f;

  //order has to match emotionNames below: Happiness, Sadness, Surprise, Fear, Anger, Disgust, Contempt, Neutral
  //last slot (Neutral) isnt actually used for scoring, kept only so the array lines up with emotionNames for the debug display
  [SerializeField] private float[] thresholds =
    { 0.70f, 0.55f, 0.70f, 0.55f, 0.50f, 0.50f, 0.80f, 1f };

  [SerializeField] private float contemptAsymmetry = 0.12f;
  [SerializeField] private int windowFrames = 20;

  //how many frames in a row a result has to show up before the DISPLAYED emotion actually changes, same rule for every transition now
  [SerializeField] private int requiredStreak = 5;

  public readonly string[] emotionNames =
    { "Happiness", "Sadness", "Surprise", "Fear", "Anger", "Disgust", "Contempt", "Neutral" };

  public float[] LastScores { get; private set; } = new float[8];

  private float[,] scoreHistory;
  private float[] runningSum = new float[8];

  private int writeIndex;
  private int samplesWritten;

  private string pendingCandidate = "Neutral";
  private int pendingStreak = 0;

  //order is innerBrow (1), outerBrow (2), browLower (3), upperLid (4), cheekRaise (5), noseWrinkle (6), upperLip (7), smile (8), frown (9), lipStretch (10), jawDrop (11), tighterLid (12), mouthPucker (13), mouthLowerDown (14)
  private readonly float[] happiness = {0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0};
  private readonly float[] sadness = {1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0};
  private readonly float[] surprise = {1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0};
  private readonly float[] fear = {1, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 0};
  private readonly float[] anger = {0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0};
  private readonly float[] disgust = {0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1};

  private void Start()
  {
    Debug.Log($"Is Face Tracking enabled? {faceExpr.FaceTrackingEnabled}");
    scoreHistory = new float[windowFrames, 8];
  }

  void Update()
  {
    if(faceExpr == null || !faceExpr.ValidExpressions)
      return;

    float[] face = BuildFaceVector();

    float[] rawScores = new float[8]; //index 7 (neutral) stays 0, unused for scoring
    rawScores[0] = CosineSimilarity(face, happiness);
    rawScores[1] = CosineSimilarity(face, sadness);
    rawScores[2] = CosineSimilarity(face, surprise);
    rawScores[3] = CosineSimilarity(face, fear);
    rawScores[4] = CosineSimilarity(face, anger);
    rawScores[5] = CosineSimilarity(face, disgust);
    rawScores[6] = ContemptCalculation(faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerL],
      faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerR],
      contemptAsymmetry);

    for(int i = 0; i < rawScores.Length; i++)
    {
      runningSum[i] -= scoreHistory[writeIndex, i];
      scoreHistory[writeIndex, i] = rawScores[i];
      runningSum[i] += rawScores[i];
    }
    writeIndex = (writeIndex + 1) % windowFrames;
    if(samplesWritten < windowFrames)
      samplesWritten++;

    //only the 7 real emotions compete for best, neutral never enters this loop
    int best = 0;
    for(int i = 0; i < 7; i++)
    {
      LastScores[i] = runningSum[i] / samplesWritten;
      if(LastScores[i] > LastScores[best]) best = i;
    }
    LastScores[7] = 0f;

    //neutral is just the fallback when the winner doesnt clear its own bar, nothing more
    string candidate = (LastScores[best] >= thresholds[best]) ? emotionNames[best] : "Neutral";

    //disgust and fear can clash because fear naturally activates some nose wrinkle
    if (candidate == "Disgust")
    {
      float brow = (faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserL] +
                    faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserR]) / 2f;
      float jaw = faceExpr[OVRFaceExpressions.FaceExpression.JawDrop];
      if (brow > 0.3f || jaw > 0.3f)
        candidate = "Fear";
    }

    //debounce: same rule for every transition, no more special casing between neutral and real emotions
    if(candidate == pendingCandidate)
      pendingStreak++;
    else
    {
      pendingCandidate = candidate;
      pendingStreak = 1;
    }
    if(pendingStreak >= requiredStreak)
      CurrentEmotion = candidate;

    int shownIndex = System.Array.IndexOf(emotionNames, CurrentEmotion);
    CurrentConfidence = shownIndex >= 0 ? LastScores[shownIndex] : 0f;
  }

  private float[] BuildFaceVector()
  {
    float innerBrow = Avg(OVRFaceExpressions.FaceExpression.InnerBrowRaiserL, OVRFaceExpressions.FaceExpression.InnerBrowRaiserR);
    float outerBrow = Avg(OVRFaceExpressions.FaceExpression.OuterBrowRaiserL, OVRFaceExpressions.FaceExpression.OuterBrowRaiserR);
    float browLower = Avg(OVRFaceExpressions.FaceExpression.BrowLowererL, OVRFaceExpressions.FaceExpression.BrowLowererR);
    float upperLid = Avg(OVRFaceExpressions.FaceExpression.UpperLidRaiserL, OVRFaceExpressions.FaceExpression.UpperLidRaiserR);
    float tighterLid = Avg(OVRFaceExpressions.FaceExpression.LidTightenerL, OVRFaceExpressions.FaceExpression.LidTightenerR);
    float mouthPucker = Avg(OVRFaceExpressions.FaceExpression.LipPuckerL, OVRFaceExpressions.FaceExpression.LipPuckerR);
    float cheekRaise = Avg(OVRFaceExpressions.FaceExpression.CheekRaiserL, OVRFaceExpressions.FaceExpression.CheekRaiserR);
    float noseWrink = Avg(OVRFaceExpressions.FaceExpression.NoseWrinklerL, OVRFaceExpressions.FaceExpression.NoseWrinklerR);
    float upperLip = Avg(OVRFaceExpressions.FaceExpression.UpperLipRaiserL, OVRFaceExpressions.FaceExpression.UpperLipRaiserR);
    float smile = Avg(OVRFaceExpressions.FaceExpression.LipCornerPullerL, OVRFaceExpressions.FaceExpression.LipCornerPullerR);
    float frown = Avg(OVRFaceExpressions.FaceExpression.LipCornerDepressorL, OVRFaceExpressions.FaceExpression.LipCornerDepressorR);
    float lipStretch = Avg(OVRFaceExpressions.FaceExpression.LipStretcherL, OVRFaceExpressions.FaceExpression.LipStretcherR);
    float jawDrop = faceExpr[OVRFaceExpressions.FaceExpression.JawDrop];
    float mouthLowerDown = Avg(OVRFaceExpressions.FaceExpression.LowerLipDepressorL, OVRFaceExpressions.FaceExpression.LowerLipDepressorR);

    return new[] {innerBrow, outerBrow, browLower, upperLid, cheekRaise, noseWrink, upperLip, smile, frown, lipStretch, jawDrop, tighterLid, mouthPucker, mouthLowerDown};
  }

  private float ContemptCalculation(float pullL, float pullR, float maxAsymmetry)
  {
    return Mathf.Min(Mathf.Abs(pullL - pullR) / maxAsymmetry, 1);
  }

  private float CosineSimilarity(float[] a, float[] b)
  {
    float dot = 0f;
    float magA = 0f;
    float magB = 0f;
    for(int i = 0; i < a.Length; i++)
    {
      dot += a[i] * b[i];
      magA += a[i] * a[i];
      magB += b[i] * b[i];
    }
    if(magA == 0f || magB == 0f)
      return 0f;
    return dot / (Mathf.Sqrt(magA) * Mathf.Sqrt(magB));
  }

  private float Avg(OVRFaceExpressions.FaceExpression left, OVRFaceExpressions.FaceExpression right)
  {
    return (faceExpr[left] + faceExpr[right]) / 2f;
  }
}