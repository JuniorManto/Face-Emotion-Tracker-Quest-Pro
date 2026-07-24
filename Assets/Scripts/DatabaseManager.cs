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

    //shared across both headsets once assigned, groups both headsets log entries under the same session
    public string SessionId { get; private set; } = "";

    //true once both presence/1 and presence/2 exist, other scripts read this to know when to actually start
    public bool BothPlayersPresent { get; private set; } = false;

    //true once both restartReady/1 and restartReady/2 exist, this is what gates a synced restart after game over
    public bool BothPlayersReadyToRestart { get; private set; } = false;

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

        _reference.Child("presence").ValueChanged += OnPresenceChanged;
        _reference.Child("restartReady").ValueChanged += OnRestartReadyChanged;

        StartCoroutine(ClaimId());
    }

    private void OnPresenceChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.Snapshot == null)
        {
            BothPlayersPresent = false;
            return;
        }

        BothPlayersPresent = args.Snapshot.HasChild("1") && args.Snapshot.HasChild("2");
    }

    private void OnRestartReadyChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.Snapshot == null)
        {
            BothPlayersReadyToRestart = false;
            return;
        }

        BothPlayersReadyToRestart = args.Snapshot.HasChild("1") && args.Snapshot.HasChild("2");
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
    //also generates a sessionId here, id 2 picks it up via ReadRigging so both headsets log under the same session
    public IEnumerator AssignRigging()
    {
        if (myId != 1)
            yield break;

        int winner = UnityEngine.Random.value < 0.5f ? 1 : 2;
        string sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var payload = new Dictionary<string, object>
        {
            {"advantagedId", winner},
            {"sessionId", sessionId}
        };

        var dbTask = _reference.Child("rigging").UpdateChildrenAsync(payload);
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"rigging assign failed: {dbTask.Exception}");
            yield break;
        }

        advantagedId = winner;
        SessionId = sessionId;
    }

    //id 2 cant know the result until id 1 writes it, so this just polls, now also picks up the shared sessionId
    public IEnumerator ReadRigging()
    {
        var dbTask = _reference.Child("rigging").GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError(dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        if (!snapshot.Exists || !snapshot.HasChild("advantagedId"))
            yield break;

        advantagedId = Convert.ToInt32(snapshot.Child("advantagedId").Value);
        SessionId = snapshot.Child("sessionId").Value?.ToString() ?? "";
    }

    //permanent record tagged by the physical headset, the tape label, not by whatever myId this session happened to be
    //so after the pilot you can see exactly which taped device was advantaged each session, regardless of connection order
    public IEnumerator LogRiggingResult(int physicalHeadsetId, bool isAdvantaged)
    {
        if (string.IsNullOrEmpty(SessionId))
            yield break;

        string result = isAdvantaged ? "advantaged" : "disadvantaged";
        var dbTask = _reference.Child("sessions").Child(SessionId).Child("headset" + physicalHeadsetId).SetValueAsync(result);

        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"rigging log failed: {dbTask.Exception}");
        }
    }

    //called once this headset has pulled a trigger on the game over screen, signals im ready for a new round
    public IEnumerator RequestRestart()
    {
        if (myId == 0)
            yield break;

        var dbTask = _reference.Child("restartReady").Child(myId.ToString()).SetValueAsync(true);
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"restart request failed: {dbTask.Exception}");
        }
    }

    //called right after this headset actually restarts, clears just this players flag so the node is fresh for next time
    public IEnumerator ClearMyRestartReady()
    {
        if (myId == 0)
            yield break;

        var dbTask = _reference.Child("restartReady").Child(myId.ToString()).RemoveValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError($"restart clear failed: {dbTask.Exception}");
        }
    }

    private void OnDestroy()
    {
        if (_reference != null)
        {
            _reference.Child("presence").ValueChanged -= OnPresenceChanged;
            _reference.Child("restartReady").ValueChanged -= OnRestartReadyChanged;
        }
    }

    private string GetTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    }
}