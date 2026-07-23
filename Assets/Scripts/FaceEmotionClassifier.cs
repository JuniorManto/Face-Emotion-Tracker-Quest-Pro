using UnityEngine;
using Unity.InferenceEngine;

//swapped from cosine similarity to the trained mlp
//this mirrors FauModelTester exactly: raw per-frame inference, straight argmax, no smoothing or debounce
//trained on emohevrdb which only has 7 classes: neutral happiness sadness surprise fear disgust anger, no contempt
//contempt is dropped for now, not being reported at all
//verified the models real output order empirically on device since it didnt match what the dataset docs say
//ema smoothing plus a spike and hold display gate on top, CurrentEmotion stays raw for firebase sync
//DisplayEmotion is the one the ar overlay reads from
//spikeThreshold lowered to 0.3, this models confidence outputs run low even on real expressions, 0.85 was never reachable
public class FaceEmotionClassifier:MonoBehaviour
{
  [SerializeField] private OVRFaceExpressions faceExpr;
  [SerializeField] private ModelAsset modelAsset;

  public string CurrentEmotion{get; private set;} = "Neutral";
  public float CurrentConfidence{get; private set;} = 0f;

  public readonly string[] emotionNames =
    { "Happiness", "Sadness", "Surprise", "Fear", "Anger", "Disgust", "Contempt", "Neutral" };

  public float[] LastScores { get; private set; } = new float[8];

  [SerializeField] private float alpha = 0.3f;
  [SerializeField] private float spikeThreshold = 0.3f;
  [SerializeField] private float holdSeconds = 3f;

  float[] smoothedScores = new float[8];
  bool holding = false;
  float holdTimer = 0f;

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

    LastScores[0] = mlpScores[1]; //happiness
    LastScores[1] = mlpScores[2]; //sadness
    LastScores[2] = mlpScores[3]; //surprise
    LastScores[3] = mlpScores[4]; //fear
    LastScores[4] = mlpScores[6]; //anger
    LastScores[5] = mlpScores[5]; //disgust
    LastScores[6] = 0f;           //contempt, not used
    LastScores[7] = mlpScores[0]; //neutral

    int best = 0;
    for(int i = 1; i < 8; i++)
    {
      if(i == 6) continue;
      if(LastScores[i] > LastScores[best])
        best = i;
    }
    CurrentEmotion = emotionNames[best];
    CurrentConfidence = LastScores[best];

    for(int i = 0; i < 8; i++)
      smoothedScores[i] = alpha * LastScores[i] + (1f - alpha) * smoothedScores[i];

    UpdateDisplayGate();
  }

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
      return;
    }

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