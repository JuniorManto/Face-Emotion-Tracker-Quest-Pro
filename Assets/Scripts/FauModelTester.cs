//quick standalone test, does the trained mlp handle natural expressions better than cosine
//not meant to be final code, just shows the top emotion so we can eyeball it
//compare this against DebugDisplay on the same natural expression, side by side
//label order below was empirically calibrated by testing each expression one at a time
//does not match emohevrdb's documented order, so dont trust the docs over this
using UnityEngine;
using Unity.InferenceEngine;
using TMPro;

public class FauModelTester : MonoBehaviour
{
    //drag fau_model.onnx here
    public ModelAsset modelAsset;

    //drag the gameobject that has OVRFaceExpressions on it here
    public OVRFaceExpressions faceExpressions;

    //drag a TMP text object here so we can read this on headset, not just editor console
    public TMP_Text displayText;

    //this order came from testing each face one at a time and noting which index fired
    //not the order emohevrdb documents, that order was wrong for this exported model
    public string[] emotionLabels = { "neutral", "happiness", "sadness", "surprise", "fear", "disgust", "anger" };

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

        if (!faceExpressions.ValidExpressions)
            return;

        for (int i = 0; i < 63; i++)
        {
            feaVector[i] = faceExpressions.GetWeight((OVRFaceExpressions.FaceExpression)i);
        }

        Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 63), feaVector);
        worker.Schedule(inputTensor);

        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        float[] scores = outputTensor.DownloadToArray();

        int bestIndex = 0;
        float bestScore = scores[0];
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i] > bestScore)
            {
                bestScore = scores[i];
                bestIndex = i;
            }
        }

        string bestLabel = bestIndex < emotionLabels.Length ? emotionLabels[bestIndex] : "unknown";
        string verdict = bestLabel + " (" + bestScore.ToString("F2") + ")";

        if (displayText != null)
            displayText.text = verdict;

        string fullBreakdown = "";
        for (int i = 0; i < scores.Length && i < emotionLabels.Length; i++)
        {
            fullBreakdown += emotionLabels[i] + "=" + scores[i].ToString("F2") + " ";
        }
        Debug.Log("mlp verdict: " + verdict + " | full: " + fullBreakdown);

        inputTensor.Dispose();
    }

    void OnDestroy()
    {
        worker.Dispose();
    }
}