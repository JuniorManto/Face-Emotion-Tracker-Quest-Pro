using UnityEngine;
using Unity.InferenceEngine;

//swapped from cosine similarity to the trained mlp
//this mirrors FauModelTester exactly: raw per-frame inference, straight argmax, no smoothing or debounce
//trained on emohevrdb which only has 7 classes: neutral happiness sadness surprise fear disgust anger, no contempt
//contempt is dropped for now, not being reported at all
//verified the models real output order empirically on device since it didnt match what the dataset docs say
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
    int best = 0;
    for(int i = 1; i < 8; i++)
    {
      if(i == 6) continue;
      if(LastScores[i] > LastScores[best])
        best = i;
    }
    CurrentEmotion = emotionNames[best];
    CurrentConfidence = LastScores[best];
  }

  private void OnDestroy()
  {
    worker?.Dispose();
  }
}