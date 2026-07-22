using UnityEngine;
using TMPro;

//piano tiles style minigame for the pilot study, each headset runs its own local instance of this
//two lanes only since we only have two triggers, left trigger and right trigger
//only one tile is ever active at a time, randomized which lane it spawns in
//on a hit or miss the tile freezes in place, a popup shows, then after a short hold the next tile spawns
//pulling the wrong side trigger while a tile is active also counts as a miss, thats what stops mashing both
//hit window size is the rigging knob, set hitWindowPercent lower on the headset thats supposed to be disadvantaged
//this only handles the game itself, not synced to firebase yet, thats the next step after this
//score/popups are just GameObjects the script toggles, the actual look of them is built in the editor not in code
//switched trigger detection off Button.PrimaryIndexTrigger since GetDown on it wasnt registering at all
//now reading the raw Axis1D value every frame and doing the edge detection ourselves, same idea as before
//just not relying on that specific button enum which may need a much harder press or not map right on this sdk version
public class PianoTileGame:MonoBehaviour
{
  [Header("lane setup, drag empty transforms in for these")]
  [SerializeField] private Transform leftSpawnPoint;
  [SerializeField] private Transform rightSpawnPoint;
  [SerializeField] private Transform leftHitLine;
  [SerializeField] private Transform rightHitLine;

  [Header("tile settings")]
  [SerializeField] private float tileHeight = 0.3f;
  [SerializeField] private float tileWidth = 0.3f;
  [SerializeField] private float fallTimeSeconds = 3.5f;
  [SerializeField] private float fallTimeReductionPerPoint = 0.02f;
  [SerializeField] private float minFallTimeSeconds = 1f;

  [Header("hit window, this is the rigging knob")]
  [Range(0.1f, 1.5f)]
  [SerializeField] private float hitWindowPercent = 1f;

  //how far the analog trigger has to be pulled before we count it as pressed
  [SerializeField] private float triggerPressThreshold = 0.5f;

  [Header("resolve hold, tile freezes and popup shows for this long before the next tile")]
  [SerializeField] private float hitHoldSeconds = 0.6f;
  [SerializeField] private float penaltySeconds = 2f;

  [Header("timer")]
  [SerializeField] private float gameDurationSeconds = 120f;

  [Header("visuals, neon arcade palette")]
  [SerializeField] private Color leftLaneColor = new Color(0.1f, 0.9f, 0.95f);
  [SerializeField] private Color rightLaneColor = new Color(1f, 0.15f, 0.6f);
  [SerializeField] private Color hitLineColor = new Color(1f, 0.85f, 0.15f);
  [SerializeField] private Color boardColor = new Color(0.04f, 0.03f, 0.1f);
  [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f);
  [SerializeField] private float borderPadding = 0.025f;
  [SerializeField] private float railThickness = 0.03f;
  [SerializeField] private float boardMargin = 0.15f;

  [Header("ui, build these yourself on a canvas then drag them in")]
  [SerializeField] private TextMeshProUGUI scoreText;
  [SerializeField] private GameObject pointPopup;
  [SerializeField] private GameObject penaltyPopup;

  //turn this on to print raw trigger values to the console every frame theyre above a tiny deadzone
  //this is just to confirm ovrinput is actually receiving something, turn back off once triggers are confirmed working
  [Header("debug")]
  [SerializeField] private bool debugLogTriggers = true;

  public int Score { get; private set; } = 0;
  public float TimeRemaining { get; private set; }
  public bool GameActive { get; private set; } = false;
  public bool InPenalty { get; private set; } = false;
  public bool InHitHold { get; private set; } = false;

  GameObject activeTile;
  int activeLane = -1;
  float holdTimer;

  //tracked every frame regardless of game state so the edge detection is never confused by a state change
  float prevLeftAxis = 0f;
  float prevRightAxis = 0f;
  bool leftJustPulled = false;
  bool rightJustPulled = false;

  void Start()
  {
    TimeRemaining = gameDurationSeconds;
    GameActive = true;

    if(pointPopup != null) pointPopup.SetActive(false);
    if(penaltyPopup != null) penaltyPopup.SetActive(false);
    UpdateScoreText();

    BuildBoard();
    BuildLaneVisuals(leftSpawnPoint, leftHitLine, leftLaneColor);
    BuildLaneVisuals(rightSpawnPoint, rightHitLine, rightLaneColor);
  }

  void Update()
  {
    ReadTriggers();

    if(!GameActive)
      return;

    TimeRemaining -= Time.deltaTime;
    if(TimeRemaining <= 0f)
    {
      TimeRemaining = 0f;
      GameActive = false;
      return;
    }

    if(InPenalty)
    {
      holdTimer -= Time.deltaTime;
      if(holdTimer <= 0f)
      {
        InPenalty = false;
        if(penaltyPopup != null) penaltyPopup.SetActive(false);
        ClearActiveTile();
      }
      return;
    }

    if(InHitHold)
    {
      holdTimer -= Time.deltaTime;
      if(holdTimer <= 0f)
      {
        InHitHold = false;
        if(pointPopup != null) pointPopup.SetActive(false);
        ClearActiveTile();
      }
      return;
    }

    if(activeTile == null)
    {
      SpawnRandomTile();
      return;
    }

    MoveAndCheckActiveTile();
  }

  //reads the raw analog value every frame and does our own edge detection instead of relying on ovrinput button mapping
  void ReadTriggers()
  {
    float leftAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
    float rightAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

    if(debugLogTriggers && (leftAxis > 0.05f || rightAxis > 0.05f))
      Debug.Log($"trigger axis, left {leftAxis:F2} right {rightAxis:F2}");

    //just pulled means it crossed the threshold this frame, wasnt above it last frame
    leftJustPulled = leftAxis >= triggerPressThreshold && prevLeftAxis < triggerPressThreshold;
    rightJustPulled = rightAxis >= triggerPressThreshold && prevRightAxis < triggerPressThreshold;

    prevLeftAxis = leftAxis;
    prevRightAxis = rightAxis;
  }

  void SpawnRandomTile()
  {
    activeLane = Random.value < 0.5f ? 0 : 1;
    Transform spawnPoint = activeLane == 0 ? leftSpawnPoint : rightSpawnPoint;
    Color laneColor = activeLane == 0 ? leftLaneColor : rightLaneColor;
    activeTile = SpawnTile(spawnPoint, laneColor);
  }

  void MoveAndCheckActiveTile()
  {
    Transform spawnPoint = activeLane == 0 ? leftSpawnPoint : rightSpawnPoint;
    Transform hitLine = activeLane == 0 ? leftHitLine : rightHitLine;

    float totalDistance = spawnPoint.position.y - hitLine.position.y;
    float effectiveFallTime = Mathf.Max(minFallTimeSeconds, fallTimeSeconds - Score * fallTimeReductionPerPoint);
    float speed = totalDistance / effectiveFallTime;

    activeTile.transform.position += Vector3.down * speed * Time.deltaTime;

    float distanceFromLine = Mathf.Abs(activeTile.transform.position.y - hitLine.position.y);
    float windowRadius = (tileHeight / 2f) * hitWindowPercent;

    if(leftJustPulled || rightJustPulled)
    {
      bool correctLane = (activeLane == 0 && leftJustPulled) || (activeLane == 1 && rightJustPulled);
      bool wrongLane = (activeLane == 0 && rightJustPulled) || (activeLane == 1 && leftJustPulled);

      if(correctLane && !wrongLane && distanceFromLine <= windowRadius)
        ResolveHit();
      else
        ResolveMiss();
      return;
    }

    if(activeTile.transform.position.y < hitLine.position.y - windowRadius)
      ResolveMiss();
  }

  void ResolveHit()
  {
    Score++;
    UpdateScoreText();
    InHitHold = true;
    holdTimer = hitHoldSeconds;
    if(pointPopup != null) pointPopup.SetActive(true);
  }

  void ResolveMiss()
  {
    InPenalty = true;
    holdTimer = penaltySeconds;
    if(penaltyPopup != null) penaltyPopup.SetActive(true);
  }

  void ClearActiveTile()
  {
    if(activeTile != null)
      Destroy(activeTile);
    activeTile = null;
    activeLane = -1;
  }

  void UpdateScoreText()
  {
    if(scoreText != null)
      scoreText.text = "Score: " + Score;
  }

  GameObject SpawnTile(Transform spawnPoint, Color color)
  {
    CreateBorderedCube(spawnPoint.position, new Vector3(tileWidth, tileHeight, 0.05f), outlineColor, borderPadding);
    return CreateCube(spawnPoint.position + Vector3.back * 0.005f, new Vector3(tileWidth, tileHeight, 0.05f), color);
  }

  void BuildBoard()
  {
    float minX = Mathf.Min(leftSpawnPoint.position.x, rightSpawnPoint.position.x) - tileWidth / 2f - boardMargin;
    float maxX = Mathf.Max(leftSpawnPoint.position.x, rightSpawnPoint.position.x) + tileWidth / 2f + boardMargin;
    float topY = Mathf.Max(leftSpawnPoint.position.y, rightSpawnPoint.position.y) + tileHeight + boardMargin;
    float bottomY = Mathf.Min(leftHitLine.position.y, rightHitLine.position.y) - tileHeight - boardMargin;

    float centerX = (minX + maxX) / 2f;
    float centerY = (topY + bottomY) / 2f;
    float width = maxX - minX;
    float height = topY - bottomY;
    float z = leftSpawnPoint.position.z + 0.08f;

    CreateCube(new Vector3(centerX, centerY, z), new Vector3(width, height, 0.02f), boardColor);

    float dividerX = (leftSpawnPoint.position.x + rightSpawnPoint.position.x) / 2f;
    CreateCube(new Vector3(dividerX, centerY, z - 0.03f), new Vector3(railThickness, height, railThickness), outlineColor);
  }

  void BuildLaneVisuals(Transform spawnPoint, Transform hitLine, Color laneColor)
  {
    float topY = spawnPoint.position.y + tileHeight;
    float bottomY = hitLine.position.y - tileHeight;
    float midY = (topY + bottomY) / 2f;
    float railLength = topY - bottomY;

    Vector3 leftRailPos = new Vector3(spawnPoint.position.x - tileWidth / 2f, midY, spawnPoint.position.z);
    Vector3 rightRailPos = new Vector3(spawnPoint.position.x + tileWidth / 2f, midY, spawnPoint.position.z);
    Vector3 railScale = new Vector3(railThickness, railLength, railThickness);

    CreateCube(leftRailPos, railScale, laneColor);
    CreateCube(rightRailPos, railScale, laneColor);

    Vector3 lineScale = new Vector3(tileWidth + railThickness * 2f, railThickness * 2.5f, railThickness);
    CreateBorderedCube(hitLine.position, lineScale, outlineColor, borderPadding);
    CreateCube(hitLine.position + Vector3.back * 0.005f, lineScale, hitLineColor);
  }

  void CreateBorderedCube(Vector3 pos, Vector3 baseScale, Color borderColor, float padding)
  {
    Vector3 borderScale = baseScale + new Vector3(padding, padding, 0f);
    CreateCube(pos + Vector3.forward * 0.005f, borderScale, borderColor);
  }

  GameObject CreateCube(Vector3 pos, Vector3 scale, Color color)
  {
    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.transform.position = pos;
    go.transform.localScale = scale;
    Destroy(go.GetComponent<Collider>());
    go.GetComponent<Renderer>().material = MakeUnlitMaterial(color);
    return go;
  }

  Material MakeUnlitMaterial(Color c)
  {
    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
    if(shader == null) shader = Shader.Find("Unlit/Color");
    if(shader == null) shader = Shader.Find("Standard");
    Material mat = new Material(shader);
    mat.color = c;
    return mat;
  }
}