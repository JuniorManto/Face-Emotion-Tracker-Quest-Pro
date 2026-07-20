//quick standalone test, does the trained mlp handle natural expressions better than cosine
//not meant to be final code, just dumps raw scores to console so we can eyeball it
//compare this against ScoreDebugPanel on the same natural expression, side by side
using UnityEngine;
using Unity.InferenceEngine;

public class FauModelTester : MonoBehaviour
{
    //drag fau_model.onnx here
    public ModelAsset modelAsset;

    //drag the gameobject that has OVRFaceExpressions on it here
    public OVRFaceExpressions faceExpressions;

    public string[] emotionLabels = { "anger", "disgust", "fear", "happiness", "neutral", "sadness", "surprise" };

    Model runtimeModel;
    Worker worker;
    float[] feaVector;

    void Start()
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.CPU);
        feaVector = new float[63];
    }

    void Update()
    {
        if (faceExpressions == null)
            return;

        //pull the 63 fea floats straight off the headset
        //same enum order emohevrdb was collected in since it comes from this same api
        for (int i = 0; i < 63; i++)
        {
            feaVector[i] = faceExpressions.GetWeight((OVRFaceExpressions.FaceExpression)i);
        }

        Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 63), feaVector);
        worker.Schedule(inputTensor);

        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        float[] scores = outputTensor.DownloadToArray();

        string line = "mlp scores: ";
        for (int i = 0; i < scores.Length && i < emotionLabels.Length; i++)
        {
            line += emotionLabels[i] + "=" + scores[i].ToString("F2") + " ";
        }
        Debug.Log(line);

        inputTensor.Dispose();
    }

    void OnDestroy()
    {
        worker.Dispose();
    }
}
