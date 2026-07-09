using System;
using System.Collections;
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
    
    public IEnumerator LogCurrentEmotion(string text)
    {
        var dbTask = _reference
            .Child("currentEmotion2")
            .SetValueAsync(text);
        
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
        
        emotionDisplay.currentPartnerEmotion = snapshot.Value.ToString();
    }
    
    private string GetTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    }
}
