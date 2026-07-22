using UnityEngine;
using Firebase.Database;
using Firebase.Extensions;

//syncs this players score to firebase and listens for the partners score
//reuses the same presence/1 and presence/2 nodes the rest of the system already claims
//score gets written as a field on that same node, not a separate path
//writes only happen when score actually changes, not every frame like the emotion classifier does
public class ScoreSync:MonoBehaviour
{
  [SerializeField] private PianoTileGame game;

  DatabaseReference rootRef;
  DatabaseReference myRef;
  DatabaseReference partnerRef;
  int lastWrittenScore = -1;

  public int PartnerScore{get; private set;} = 0;

  void Start()
  {
    rootRef = FirebaseDatabase.DefaultInstance.RootReference;
    ClaimSlot();
  }

  //this sdk version returns Task<DataSnapshot> from RunTransaction, not a result object with a Committed flag
  //if the transaction handler returns Abort, the task comes back faulted instead, thats how we tell it failed
  void ClaimSlot()
  {
    DatabaseReference slot1 = rootRef.Child("presence").Child("1");

    slot1.RunTransaction(mutable =>
    {
      if(mutable.Value == null)
      {
        mutable.Value = true;
        return TransactionResult.Success(mutable);
      }
      return TransactionResult.Abort();
    }).ContinueWithOnMainThread(task =>
    {
      if(!task.IsFaulted && !task.IsCanceled)
      {
        AssignSlots("1", "2");
        return;
      }

      //slot 1 was taken, try slot 2 the same way
      DatabaseReference slot2 = rootRef.Child("presence").Child("2");
      slot2.RunTransaction(mutable =>
      {
        if(mutable.Value == null)
        {
          mutable.Value = true;
          return TransactionResult.Success(mutable);
        }
        return TransactionResult.Abort();
      }).ContinueWithOnMainThread(task2 =>
      {
        if(!task2.IsFaulted && !task2.IsCanceled)
          AssignSlots("2", "1");
      });
    });
  }

  void AssignSlots(string my, string partner)
  {
    myRef = rootRef.Child("presence").Child(my);
    partnerRef = rootRef.Child("presence").Child(partner);

    myRef.OnDisconnect().RemoveValue();
    partnerRef.Child("score").ValueChanged += OnPartnerScoreChanged;
  }

  void OnPartnerScoreChanged(object sender, ValueChangedEventArgs args)
  {
    if(args.Snapshot != null && args.Snapshot.Value != null)
      PartnerScore = int.Parse(args.Snapshot.Value.ToString());
  }

  void Update()
  {
    if(myRef == null || game == null)
      return;

    if(game.Score != lastWrittenScore)
    {
      lastWrittenScore = game.Score;
      myRef.Child("score").SetValueAsync(game.Score);
    }
  }

  void OnDestroy()
  {
    if(partnerRef != null)
      partnerRef.Child("score").ValueChanged -= OnPartnerScoreChanged;
  }
}