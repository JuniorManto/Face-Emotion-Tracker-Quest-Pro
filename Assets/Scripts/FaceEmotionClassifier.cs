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
//neutral isnt its own template anymore, its just what happens when nothing else clears its bar
//two conditions both have to be true for neutral to actually show, nothing cleared its threshold AND the highest score overall is genuinely low
//if something scored decently but still missed its own bar thats an ambiguous face not a blank one, so we just keep showing whatever was already up instead of dropping to neutral
public class FaceEmotionClassifier:MonoBehaviour
{
  //this is the Meta component that gives the 63 FACS values
  //we drag the object that has OVRFaceExpressions on it into this slot in the Unity inspector
  [SerializeField] private OVRFaceExpressions faceExpr;

  //this string is so other scripts like the UI display can read the current emotion
  public string CurrentEmotion{get; private set;} = "Neutral";

  //how confident the winning emotion actually is, other scripts (network sync, UI) read this alongside CurrentEmotion
  public float CurrentConfidence{get; private set;} = 0f;

  //each emotion caps out at a different cosine score depending on how many muscles are in its template
  //so one global threshold for all of them didnt make sense, this is one threshold per emotion instead
  //order has to match emotionNames below: Happiness, Sadness, Surprise, Fear, Anger, Disgust, Contempt, Neutral
  //last slot (Neutral) isnt actually used for scoring since neutral has no template of its own anymore, kept only so the array lines up with emotionNames for the debug display
  [SerializeField] private float[] thresholds =
    { 0.70f, 0.55f, 0.70f, 0.55f, 0.50f, 0.50f, 0.80f, 1f };

  //second neutral condition, checked only if nothing cleared its own threshold above
  //the highest score among all 7 still has to be under this before we actually call it neutral
  //if nothing cleared but the highest score is still above this, treat it as an ambiguous expression instead of blank
  [SerializeField] private float neutralCeiling = 0.3f;

  //for contempt, how lopsided the two lip corners have to be before i call it contempt
  [SerializeField] private float contemptAsymmetry = 0.12f;

  //window size for the sliding average, in frames
  [SerializeField] private int windowFrames = 20;

  //how many frames in a row a result has to show up before the DISPLAYED emotion actually changes
  //this is what stops a single noisy frame from flickering the display, the smoothed score can wobble slightly near a threshold and this absorbs that
  [SerializeField] private int requiredStreak = 5;

  //moved names of emotions to class level to let other scripts read like a debug display
  public readonly string[] emotionNames =
    { "Happiness", "Sadness", "Surprise", "Fear", "Anger", "Disgust", "Contempt", "Neutral" };

  //hold the current sliding average for the 7 emotions
  //not just who won, but i need to see all 7 for debugging purposes
  public float[] LastScores { get; private set; } = new float[8];

  //circular buffer of raw per-frame scores, one row per frame slot, one column per emotion
  //this is what lets us slide the window every frame instead of waiting for it to fill and dumping it
  private float[,] scoreHistory;
  //running total per emotion, kept in sync with scoreHistory so we never re-sum the whole window
  private float[] runningSum = new float[8];
  //where the next frame's scores get written, wraps back to 0 at windowFrames
  private int writeIndex;
  //counts up to windowFrames during warmup so early frames dont get diluted by empty slots
  private int samplesWritten;

  //debounce state, what the last computed result was and how many frames in a row its shown up
  //CurrentEmotion only actually updates once pendingStreak reaches requiredStreak
  private string pendingCandidate = "Neutral";
  private int pendingStreak = 0;

  //these are the emotion templates. each slot lines up with the feature order in BuildFaceVector below
  //1 means this muscle should be active for this emotion, 0 means it shouldnt
  //the order is innerBrow (1), outerBrow (2), browLower (3), upperLid (4), cheekRaise (5), noseWrinkle (6), upperLip (7), smile (8), frown (9), lipStretch (10), jawDrop (11), tighterLid (12), mouthPucker (13), mouthLowerDown (14)
  //no neutral template here anymore, neutral is purely the fallback logic in Update() now, not something compared against with cosine similarity
  private readonly float[] happiness = {0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0};
  private readonly float[] sadness = {1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0};
  private readonly float[] surprise = {1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0};
  private readonly float[] fear = {1, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 0};
  private readonly float[] anger = {0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0};
  private readonly float[] disgust = {0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1};

  private void Start()
  {
    Debug.Log($"Is Face Tracking enabled? {faceExpr.FaceTrackingEnabled}");
    //sized once here since windowFrames is only set in the inspector, not at runtime
    scoreHistory = new float[windowFrames, 8];
  }

  //update runs once every frame, which is exactly what we want for real time detection
  void Update()
  {
    //if the headset has not given us valid face data yet, do nothing this frame
    //(i think this also avoids something called InvalidOperationException that happens if you read too early)
    if(faceExpr == null || !faceExpr.ValidExpressions)
      return;

    //build the live face vector (same feature order as the templates up top)
    float[] face = BuildFaceVector();

    //this frames raw score per emotion, not yet averaged
    //index 7 (neutral) stays 0 here since its not compared with cosine similarity anymore
    float[] rawScores = new float[8];
    rawScores[0] = CosineSimilarity(face, happiness);
    rawScores[1] = CosineSimilarity(face, sadness);
    rawScores[2] = CosineSimilarity(face, surprise);
    rawScores[3] = CosineSimilarity(face, fear);
    rawScores[4] = CosineSimilarity(face, anger);
    rawScores[5] = CosineSimilarity(face, disgust);

    //contempt is the odd one out as its about one lip corner pulling way more than the other (asymmetry)
    //cosine similarity cant really see that, so its checked directly here instead of template matching
    rawScores[6] = ContemptCalculation(faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerL],
      faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerR],
      contemptAsymmetry);

    //slide the window: drop whatever was in this slot last time it was used, write the new value in its place
    //keeping runningSum updated this way means the average below is a simple divide, no re-looping over the window
    for(int i = 0; i < rawScores.Length; i++)
    {
      runningSum[i] -= scoreHistory[writeIndex, i];
      scoreHistory[writeIndex, i] = rawScores[i];
      runningSum[i] += rawScores[i];
    }
    writeIndex = (writeIndex + 1) % windowFrames;
    if(samplesWritten < windowFrames)
      samplesWritten++;

    //during warmup this divides by however many frames weve actually seen so far, not the full window
    //once the buffer fills, samplesWritten just stays at windowFrames and this behaves like a normal average

    //two things get tracked in the same loop, which emotions clear their OWN threshold, and the highest score overall regardless of threshold
    //best only gets set if that emotion actually beats its own bar, so it can end up -1 meaning nobody qualified
    int best = -1;
    float highestScore = 0f;
    for(int i = 0; i < 7; i++)
    {
      LastScores[i] = runningSum[i] / samplesWritten;

      //tracked separately from best, just the biggest number regardless of whether it cleared anything
      //used below to tell a genuinely blank face apart from an ambiguous one that just missed its threshold
      if(LastScores[i] > highestScore)
        highestScore = LastScores[i];

      if(LastScores[i] >= thresholds[i])
      {
        if(best == -1 || LastScores[i] > LastScores[best])
          best = i;
      }
    }
    //neutral slot never gets a real score since it has no template, kept at 0 just so LastScores lines up with emotionNames for the debug display
    LastScores[7] = 0f;

    string candidate;
    if(best != -1)
    {
      //something clearly cleared its own bar, thats the answer
      candidate = emotionNames[best];
    }
    else if(highestScore < neutralCeiling)
    {
      //nothing cleared a threshold AND everything is genuinely low, the face really is blank
      candidate = "Neutral";
    }
    else
    {
      //nothing cleared but something scored decently, thats ambiguous not blank
      //dont downgrade to neutral here, just keep showing whatever was already displayed
      candidate = CurrentEmotion;
    }

    //disgust and fear can clash because fear naturally activates some nose wrinkle
    //if disgust wins but we also see raised brows or open jaw, its more likely fear
    if (candidate == "Disgust")
    {
      float brow = (faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserL] +
                    faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserR]) / 2f;
      float jaw = faceExpr[OVRFaceExpressions.FaceExpression.JawDrop];
      if (brow > 0.3f || jaw > 0.3f)
        candidate = "Fear";
    }

    //debounce: only commit this candidate to CurrentEmotion once it shows up requiredStreak frames in a row
    //a single frame flickering to a different answer resets the streak instead of instantly changing the display
    if(candidate == pendingCandidate)
      pendingStreak++;
    else
    {
      pendingCandidate = candidate;
      pendingStreak = 1;
    }
    if(pendingStreak >= requiredStreak)
      CurrentEmotion = candidate;

    //confidence is whatever score the FINAL displayed label has, so this stays correct even after the disgust to fear override above
    int shownIndex = System.Array.IndexOf(emotionNames, CurrentEmotion);
    CurrentConfidence = shownIndex >= 0 ? LastScores[shownIndex] : 0f;
  }

  //now we read the blendshapes we care about and pack them into one vector
  //this is something im trying which i might remove but i average the left and right sides so the rules read more cleanly
  //my reason is since most emotions are symmetric so this is a fair simplification
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

    //this order must match the template arrays at the top or the whole comparison is meaningless
    return new[] {innerBrow, outerBrow, browLower, upperLid, cheekRaise, noseWrink, upperLip, smile, frown, lipStretch, jawDrop, tighterLid, mouthPucker, mouthLowerDown};
  }

  private float ContemptCalculation(float pullL, float pullR, float maxAsymmetry)
  {
    return Mathf.Min(Mathf.Abs(pullL - pullR) / maxAsymmetry, 1);
  }

  //runs cosine similarity between the face and one template

  //what cosine similarity means is how aligned two vectors are from 0 (unrelated) to 1 (same direction)
  //the formula from the paper is dot product of a and b divided by length of a times length of b
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
        
    //guard against dividing by zero if either vector is all zeros like a totally neutral face
    if(magA == 0f || magB == 0f)
      return 0f;
      
    return dot / (Mathf.Sqrt(magA) * Mathf.Sqrt(magB));
  }

  //small helper which averages the left and right value of one blendshape
  private float Avg(OVRFaceExpressions.FaceExpression left, OVRFaceExpressions.FaceExpression right)
  {
    return (faceExpr[left] + faceExpr[right]) / 2f;
  }
}
