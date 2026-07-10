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

    //my slot number, 1 or 2, gets set once ClaimId finishes
    //0 means not claimed yet, LogCurrentEmotion and ReadCurrentPartnerEmotion both check for this
    private int myId;

    //the other headsets slot, just flips whatever mine is
    private int PartnerId => myId == 1 ? 2 : 1;

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

        StartCoroutine(ClaimId());
    }

    //claims a free slot under presence/1 or presence/2
    //no integer counter anymore, whoevers slot exists under presence IS the online users
    //the closure can run more than once if theres a write conflict, thats normal, claimedId just ends up matching whatever version actually commits
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
                //both slots taken, dont touch the data, bail below
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

        //this is the part that actually works with this sdk, remove just my child if i disconnect for any reason, clean quit or not
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

    //now writes emotion, confidence, and a timestamp together instead of just the emotion string
    public IEnumerator LogCurrentEmotion(string emotion, float confidence)
    {
        //id not claimed yet, skip this write instead of hitting currentEmotion0
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
        //id not claimed yet, we dont know which slot is the partner so skip
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