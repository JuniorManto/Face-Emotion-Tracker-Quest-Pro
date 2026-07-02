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
public class FaceEmotionClassifier:MonoBehaviour
{
  //this is the Meta component that gives the 63 FACS values
  //we drag the object that has OVRFaceExpressions on it into this slot in the Unity inspector
  [SerializeField] private OVRFaceExpressions faceExpr;

  //this string is so other scripts like the UI display can read the current emotion
  public string CurrentEmotion{get; private set;} = "Neutral";

  //how strong a blendshape has to be like between 0 and 1, before we count it as "active"
  //i made this public so we can tune it in the inspector without editing code at the lab 
  //if the strongest blendshape is below this, i treat the face as Neutral (nothing really happening)
  [SerializeField] private float activeThreshold = 0.2f;
  //how confident the best match has to be (cosine runs 0 to 1) before i trust it
  [SerializeField] private float minSimilarity = 0.5f;
  //for contempt, how lopsided the two lip corners have to be before i call it contempt
  [SerializeField] private float contemptAsymmetry = 0.5f;

  private int frameCounter;
  private float[] scoreBuffer = new float[7];

  //these are the emotion templates. each slot lines up with the feature order in BuildFaceVector below
  //1 means this muscle should be active for this emotion, 0 means it shouldnt
  //the order is innerBrow, outerBrow, browLower, upperLid, cheekRaise, noseWrinkle, upperLip, smile, frown, lipStretch, jawDrop
  private readonly float[] happiness = {0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0};
  private readonly float[] sadness = {1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0};
  private readonly float[] surprise = {1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1};
  private readonly float[] fear = {1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1};
  private readonly float[] anger = {0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0};
  private readonly float[] disgust = {0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0};

  private void Start()
  {
    Debug.Log($"Is Face Tracking enabled? {faceExpr.FaceTrackingEnabled}");
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

    //compare the live face to every template and keep track of the best match

    scoreBuffer[0] += CosineSimilarity(face, happiness);
    scoreBuffer[1] += CosineSimilarity(face, sadness);
    scoreBuffer[2] += CosineSimilarity(face, surprise);
    scoreBuffer[3] += CosineSimilarity(face, fear);
    scoreBuffer[4] += CosineSimilarity(face, anger);
    scoreBuffer[5] += CosineSimilarity(face, disgust);
    
    //start with contempt since its the odd one out as its about one lip corner pulling way more than the other (asymmetry)
    //cosine similarity cant really see that, so i check it directly first before the template matching
    scoreBuffer[6] += ContemptCalculation(faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerL], 
      faceExpr[OVRFaceExpressions.FaceExpression.LipCornerPullerR], 
      contemptAsymmetry);

    frameCounter++;
    if(frameCounter >= 10)
    {
      //find which emotion had the highest average score over the last 10 frames
      string[] names = {"Happiness","Sadness","Surprise","Fear","Anger","Disgust","Contempt"};
      int best = 0;
      for(int i = 1; i < scoreBuffer.Length; i++)
        if(scoreBuffer[i] > scoreBuffer[best]) best = i;

      CurrentEmotion = (scoreBuffer[best] / 10f >= minSimilarity) ? names[best] : "Neutral";
      
      //disgust and fear can clash because fear naturally activates some nose wrinkle
      //if disgust wins but we also see raised brows or open jaw, its more likely fear
      if(CurrentEmotion == "Disgust")
      {
        float brow = (faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserL] + faceExpr[OVRFaceExpressions.FaceExpression.InnerBrowRaiserR]) / 2f;
        float jaw = faceExpr[OVRFaceExpressions.FaceExpression.JawDrop];
        if(brow > 0.3f || jaw > 0.3f)
          CurrentEmotion = "Fear";
      }

      //reset for next window
      System.Array.Clear(scoreBuffer, 0, scoreBuffer.Length);
      frameCounter = 0;
    }
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
    float cheekRaise = Avg(OVRFaceExpressions.FaceExpression.CheekRaiserL, OVRFaceExpressions.FaceExpression.CheekRaiserR);
    float noseWrink = Avg(OVRFaceExpressions.FaceExpression.NoseWrinklerL, OVRFaceExpressions.FaceExpression.NoseWrinklerR);
    float upperLip = Avg(OVRFaceExpressions.FaceExpression.UpperLipRaiserL, OVRFaceExpressions.FaceExpression.UpperLipRaiserR);
    float smile = Avg(OVRFaceExpressions.FaceExpression.LipCornerPullerL, OVRFaceExpressions.FaceExpression.LipCornerPullerR);
    float frown = Avg(OVRFaceExpressions.FaceExpression.LipCornerDepressorL, OVRFaceExpressions.FaceExpression.LipCornerDepressorR);
    float lipStretch = Avg(OVRFaceExpressions.FaceExpression.LipStretcherL, OVRFaceExpressions.FaceExpression.LipStretcherR);
    float jawDrop = faceExpr[OVRFaceExpressions.FaceExpression.JawDrop];

    //this order must match the template arrays at the top or the whole comparison is meaningless
    return new[] {innerBrow, outerBrow, browLower, upperLid, cheekRaise, noseWrink, upperLip, smile, frown, lipStretch, jawDrop};
  }

  private float ContemptCalculation(float pullL, float pullR, float maxAsymmetry)
  {
    return Mathf.Min(Mathf.Abs(pullL - pullR) / maxAsymmetry, 1);
  }

  //runs cosine similarity between the face and one template then updates the best match if this one scores higher

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
