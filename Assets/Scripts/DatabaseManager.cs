using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Firebase.Database;
using UnityEngine;

public class DatabaseManager : MonoBehaviour
{
    private DatabaseReference _reference;
    public EmotionDisplay emotionDisplay;

    private void Start()
    {
        var options = new Firebase.AppOptions();
        options.ApiKey = "AIzaSyDOXeUKSzyUnC7gu6ONKd28joPdrmU9Ud8";
        options.AppId = "1:94282840552:android:c02a7523c43a3a064f0c5d";
        options.DatabaseUrl = new Uri("https://face-emotion-tracker-default-rtdb.firebaseio.com");
        options.ProjectId = "face-emotion-tracker";
        options.StorageBucket = "face-emotion-tracker.firebasestorage.app";

        var app = Firebase.FirebaseApp.Create(options);
        _reference = FirebaseDatabase.GetInstance(app).RootReference;
    }

    public IEnumerator ReadHappinessThreshold()
    {
        var dbTask = _reference.Child("happiness").GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;
        
        Debug.Log("Happiness threshold is: " + snapshot.Value);
    }

    //now writes emotion, confidence, and a timestamp together instead of just the emotion string
    public IEnumerator LogCurrentEmotion(string emotion, float confidence)
    {
        var payload = new Dictionary<string, object>
        {
            {"emotion", emotion},
            {"confidence", confidence},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var dbTask = _reference.Child("currentEmotion2").SetValueAsync(payload);

        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"Firebase write failed: {dbTask.Exception}");
        }
    }
    
    public IEnumerator ReadCurrentPartnerEmotion()
    {
        var dbTask = _reference.Child("currentEmotion1").GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        //bail out quietly if the partner headset hasnt written anything at all yet
        if (!snapshot.Exists)
            yield break;

        emotionDisplay.currentPartnerEmotion = snapshot.Child("emotion").Value?.ToString() ?? "Neutral";
        emotionDisplay.currentPartnerConfidence = snapshot.Child("confidence").Value != null
            ? Convert.ToSingle(snapshot.Child("confidence").Value)
            : 0f;
        emotionDisplay.currentPartnerTimestampMs = snapshot.Child("timestamp").Value != null
            ? Convert.ToInt64(snapshot.Child("timestamp").Value)
            : 0L;
    }
    
    private string GetTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    }
}