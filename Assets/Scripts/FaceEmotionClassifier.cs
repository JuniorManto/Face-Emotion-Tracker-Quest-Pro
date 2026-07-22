using UnityEngine;
using Unity.InferenceEngine;

//swapped from cosine similarity to the trained mlp
//this mirrors FauModelTester exactly: raw per-frame inference, straight argmax, no smoothing or debounce
//trained on emohevrdb which only has 7 classes: neutral happiness sadness surprise fear disgust anger, no contempt
//contempt is dropped for now, not being reported at all
//verified the models real output order empirically on device since it didnt match what the dataset docs say
//added ema smoothing plus a spike and hold display gate on top, CurrentEmotion stays raw for firebase sync
//DisplayEmotion is the new one the ar overlay should actually read from
public class FaceEmotionClassifier:MonoBehaviour
{
  //this is the Meta component that gives the 63 FACS values
  [SerializeField] private OVRFaceExpressions faceExpr;

  //drag fau_model.onnx here
  [SerializeField] private ModelAsset modelAsset;

  //this string is so other scripts like the UI display can read the current emotion
  public string CurrentEmotion{get; private set;} = "Neutral";

  //how confident the winning emotion actually is, other scripts (network sync, UI) read this alongside CurrentEmotion
  public float CurrentConfidence{get; private set;} = 0f;

  //contempt kept in this list so EmotionDisplay and DebugDisplay dont need to change (array size/order stays the same)
  //but its slot always stays at 0 below and is skipped in the winner check, so it can never actually be shown
  public readonly string[] emotionNames =
    { "Happiness", "Sadness", "Surprise", "Fear", "Anger", "Disgust", "Contempt", "Neutral" };

  //raw scores this frame, no smoothing, matches what FauModelTester was already showing
  public float[] LastScores { get; private set; } = new float[8];

  //how fast the ema reacts, higher means it follows the raw score more closely
  [SerializeField] private float alpha = 0.3f;
  //smoothed score has to clear this before we show anything on screen at all
  [SerializeField] private float spikeThreshold = 0.85f;
  //once triggered, how long the emotion stays locked on screen
  [SerializeField] private float holdSeconds = 3f;

  //smoothed version of LastScores, same 8 slot layout
  float[] smoothedScores = new float[8];
  bool holding = false;
  float holdTimer = 0f;

  //what the ar overlay should actually read, blank string means show nothing, not even neutral
  public string DisplayEmotion { get; private set; } = "";
  public float DisplayConfidence { get; private set; } = 0f;

  Model runtimeModel;
  Worker worker;
  float[] feaVector;

  private void Start()
  {
    Debug.Log($"Is Face Tracking enabled? {faceExpr.FaceTrackingEnabled}");
    runtimeModel = ModelLoader.Load(modelAsset);
    worker = new Worker(runtimeModel, BackendType.CPU);
    feaVector = new float[63];
  }

  void Update()
  {
    if(faceExpr == null || !faceExpr.ValidExpressions)
      return;

    for(int i = 0; i < 63; i++)
      feaVector[i] = faceExpr.GetWeight((OVRFaceExpressions.FaceExpression)i);

    Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 63), feaVector);
    worker.Schedule(inputTensor);
    Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
    float[] mlpScores = outputTensor.DownloadToArray();
    inputTensor.Dispose();

    //mlp comes out in its own real order, verified on device: neutral happiness sadness surprise fear disgust anger
    //mapped straight into the 8 slot layout emotionNames already uses, no averaging, this frame only
    LastScores[0] = mlpScores[1]; //happiness
    LastScores[1] = mlpScores[2]; //sadness
    LastScores[2] = mlpScores[3]; //surprise
    LastScores[3] = mlpScores[4]; //fear
    LastScores[4] = mlpScores[6]; //anger
    LastScores[5] = mlpScores[5]; //disgust
    LastScores[6] = 0f;           //contempt, not used
    LastScores[7] = mlpScores[0]; //neutral

    //straight argmax, same as FauModelTester, no debounce
    //this stays raw on purpose since firebase sync depends on it not lagging behind
    int best = 0;
    for(int i = 1; i < 8; i++)
    {
      if(i == 6) continue;
      if(LastScores[i] > LastScores[best])
        best = i;
    }
    CurrentEmotion = emotionNames[best];
    CurrentConfidence = LastScores[best];

    //ema smoothing on every slot, same formula as the old cosine version just applied to mlp output now
    for(int i = 0; i < 8; i++)
      smoothedScores[i] = alpha * LastScores[i] + (1f - alpha) * smoothedScores[i];

    UpdateDisplayGate();
  }

  //handles the spike and hold logic for DisplayEmotion, separate from the raw CurrentEmotion above
  void UpdateDisplayGate()
  {
    if(holding)
    {
      holdTimer -= Time.deltaTime;
      if(holdTimer <= 0f)
      {
        holding = false;
        DisplayEmotion = "";
        DisplayConfidence = 0f;
      }
      //while holding we dont even look for a new spike, if anger is already spiking its probably still anger
      return;
    }

    //only slots 0 to 5 count, contempt(6) is always 0 and neutral(7) should never trigger a display
    int spikeIndex = -1;
    float spikeScore = 0f;
    for(int i = 0; i < 6; i++)
    {
      if(smoothedScores[i] >= spikeThreshold && smoothedScores[i] > spikeScore)
      {
        spikeScore = smoothedScores[i];
        spikeIndex = i;
      }
    }

    if(spikeIndex != -1)
    {
      holding = true;
      holdTimer = holdSeconds;
      DisplayEmotion = emotionNames[spikeIndex];
      DisplayConfidence = smoothedScores[spikeIndex];
    }
  }

  private void OnDestroy()
  {
    worker?.Dispose();
  }
}