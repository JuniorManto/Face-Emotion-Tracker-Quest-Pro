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

    private int myId;
    public int MyId => myId;

    private int PartnerId => myId == 1 ? 2 : 1;

    public int currentPartnerScore = 0;

    //0 means not decided yet for this session, set by AssignRigging or ReadRigging
    public int advantagedId = 0;

    private void Start()
    {
        var options = new Firebase.AppOptions();
        options.ApiKey = "AIzaSyDOXeUKSzyUnC7gu6ONKd28joPdrmU9Ud8";
        options.AppId = "1:94282840552:android:c02a7523c43a3a064f0c5d";
        options.DatabaseUrl = new Uri("https://face-emotion-tracker-default-rtdb.firebaseio.com");
        options.ProjectId = "face-emotion-tracker";
        options.StorageBucket = "face-emotion-tracker.firebasestorage.app";

        var app = Firebase.FirebaseApp.Create(options);
        _reference = FirebaseDatabase.GetInstance(app, "https://face-emotion-tracker-default-rtdb.firebaseio.com").RootReference;

        StartCoroutine(ClaimId());
    }

    private IEnumerator ClaimId()
    {
        var presenceRef = _reference.Child("presence");
        int claimedId = 0;

        var dbTask = presenceRef.RunTransaction(mutableData =>
        {
            if (!mutableData.HasChild("1"))
            {
                mutableData.Child("1").Value = true;
                claimedId = 1;
            }
            else if (!mutableData.HasChild("2"))
            {
                mutableData.Child("2").Value = true;
                claimedId = 2;
            }
            else
            {
                claimedId = 0;
            }

            return TransactionResult.Success(mutableData);
        });

        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"id claim failed: {dbTask.Exception}");
            yield break;
        }

        myId = claimedId;

        if (myId == 0)
        {
            Debug.LogError("both slots full, cant join");
            yield break;
        }

        Debug.Log($"claimed id {myId}");

        _reference.Child("presence").Child(myId.ToString()).OnDisconnect().RemoveValue();
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

    public IEnumerator LogCurrentEmotion(string emotion, float confidence)
    {
        if (myId == 0)
            yield break;

        var payload = new Dictionary<string, object>
        {
            {"emotion", emotion},
            {"confidence", confidence},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var dbTask = _reference.Child("currentEmotion" + myId).SetValueAsync(payload);

        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"Firebase write failed: {dbTask.Exception}");
        }
    }

    public IEnumerator ReadCurrentPartnerEmotion()
    {
        if (myId == 0)
            yield break;

        var dbTask = _reference.Child("currentEmotion" + PartnerId).GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

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

    public IEnumerator LogCurrentScore(int score)
    {
        if (myId == 0)
            yield break;

        var dbTask = _reference.Child("currentScore" + myId).SetValueAsync(score);

        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"Firebase score write failed: {dbTask.Exception}");
        }
    }

    public IEnumerator ReadCurrentPartnerScore()
    {
        if (myId == 0)
            yield break;

        var dbTask = _reference.Child("currentScore" + PartnerId).GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        if (!snapshot.Exists)
            yield break;

        currentPartnerScore = snapshot.Value != null ? Convert.ToInt32(snapshot.Value) : 0;
    }

    //only the id 1 headset calls this, flips a coin and writes who gets the advantage this session
    public IEnumerator AssignRigging()
    {
        if (myId != 1)
            yield break;

        int winner = UnityEngine.Random.value < 0.5f ? 1 : 2;

        var dbTask = _reference.Child("rigging").Child("advantagedId").SetValueAsync(winner);
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"rigging assign failed: {dbTask.Exception}");
            yield break;
        }

        advantagedId = winner;
    }

    //id 2 cant know the result until id 1 writes it, so this just polls
    public IEnumerator ReadRigging()
    {
        var dbTask = _reference.Child("rigging").Child("advantagedId").GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        if (!snapshot.Exists)
            yield break;

        advantagedId = Convert.ToInt32(snapshot.Value);
    }

    private string GetTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    }
}