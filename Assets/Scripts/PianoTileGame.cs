using UnityEngine;
using TMPro;

public class PianoTileGame:MonoBehaviour
{
  public Transform centerEyeTransform;

  [Header("lane setup, drag empty transforms in for these")]
  [SerializeField] private Transform leftSpawnPoint;
  [SerializeField] private Transform rightSpawnPoint;
  [SerializeField] private Transform leftHitLine;
  [SerializeField] private Transform rightHitLine;

  [Header("tile settings")]
  [SerializeField] private float tileHeight = 0.3f;
  [SerializeField] private float tileWidth = 0.25f;
  [SerializeField] private float fallTimeSeconds = 3.5f;
  [SerializeField] private float fallTimeReductionPerPoint = 0.06f;
  [SerializeField] private float minFallTimeSeconds = 1f;

  [SerializeField] private int difficultyStepsBackOnMiss = 2;

  [Header("hit window, this is the rigging knob")]
  [Range(0.1f, 1.5f)]
  [SerializeField] private float hitWindowPercent = 1f;

  [SerializeField] private float triggerPressThreshold = 0.5f;

  [Header("resolve hold, tile freezes and popup shows for this long before the next tile")]
  [SerializeField] private float hitHoldSeconds = 0.6f;
  [SerializeField] private float penaltySeconds = 2f;

  [Header("timer")]
  [SerializeField] private float gameDurationSeconds = 120f;
  [SerializeField] private float warmupSeconds = 0.4f;

  [Header("visuals, neon arcade palette")]
  [SerializeField] private Color leftLaneColor = new Color(0.15f, 0.45f, 0.95f);
  [SerializeField] private Color rightLaneColor = new Color(0.25f, 0.85f, 0.35f);
  [SerializeField] private Color hitLineColor = new Color(1f, 0.85f, 0.15f);
  [SerializeField] private Color boardColor = new Color(0.04f, 0.03f, 0.1f);
  [SerializeField] private Color outlineColor = new Color(0.25f, 0.2f, 0.42f);
  [SerializeField] private float borderPadding = 0.025f;
  [SerializeField] private float railThickness = 0.022f;
  [SerializeField] private float boardMargin = 0.15f;

  [Header("visuals, rounding and glow")]
  [Range(0.05f, 0.4f)]
  [SerializeField] private float cardCornerRadiusFrac = 0.22f;
  [Range(0.02f, 0.3f)]
  [SerializeField] private float boardCornerRadiusFrac = 0.08f;
  [SerializeField] private int cardTextureRes = 96;
  [SerializeField] private int boardTextureRes = 256;
  [SerializeField] private float hitLineGlowSize = 0.18f;
  [Range(0f, 1f)]
  [SerializeField] private float hitLineGlowAlpha = 0.45f;

  [Header("ui, build these yourself on a canvas then drag them in")]
  [SerializeField] private TextMeshProUGUI scoreText;
  [SerializeField] private GameObject pointPopup;
  [SerializeField] private GameObject penaltyPopup;

  [Header("debug")]
  [SerializeField] private bool debugLogTriggers = false;

  public int Score { get; private set; } = 0;
  public float TimeRemaining { get; private set; }
  public bool GameActive { get; private set; } = false;
  public bool InPenalty { get; private set; } = false;
  public bool InHitHold { get; private set; } = false;

  int difficultyLevel = 0;

  GameObject activeTile;
  int activeLane = -1;
  float holdTimer;
  float warmupTimer;

  float prevLeftAxis = 0f;
  float prevRightAxis = 0f;
  bool leftJustPulled = false;
  bool rightJustPulled = false;

  void Start()
  {
    TimeRemaining = gameDurationSeconds;
    GameActive = false;
    warmupTimer = warmupSeconds;

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

    if(warmupTimer > 0f)
    {
      warmupTimer -= Time.deltaTime;
      if(warmupTimer <= 0f)
        GameActive = true;
      return;
    }

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

  void ReadTriggers()
  {
    float leftAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
    float rightAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

    if(debugLogTriggers && (leftAxis > 0.05f || rightAxis > 0.05f))
      Debug.Log($"trigger axis, left {leftAxis:F2} right {rightAxis:F2}");

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
    float effectiveFallTime = Mathf.Max(minFallTimeSeconds, fallTimeSeconds - difficultyLevel * fallTimeReductionPerPoint);
    float speed = totalDistance / effectiveFallTime;

    activeTile.transform.localPosition += Vector3.down * speed * Time.deltaTime;

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
    difficultyLevel++;
    UpdateScoreText();
    ClearActiveTile();
    InHitHold = true;
    holdTimer = hitHoldSeconds;
    if(pointPopup != null) pointPopup.SetActive(true);
  }

  void ResolveMiss()
  {
    difficultyLevel = Mathf.Max(0, difficultyLevel - difficultyStepsBackOnMiss);
    ClearActiveTile();
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

  //lets RiggingSetup override the inspector value at runtime once the coin flip result is known
  public void SetHitWindowPercent(float percent)
  {
    hitWindowPercent = percent;
  }

  GameObject SpawnTile(Transform spawnPoint, Color laneColor)
  {
    Color border = laneColor * 0.4f;
    border.a = 1f;
    return CreateCard(spawnPoint.position, tileWidth, tileHeight, 0.05f, laneColor, border, cardCornerRadiusFrac, cardTextureRes);
  }

  void SpawnFlash(Vector3 pos)
  {
    GameObject flash = CreateCard(pos, tileWidth * 1.4f, tileHeight * 1.4f, 0.06f, Color.white, new Color(0.85f, 0.85f, 0.85f), cardCornerRadiusFrac, cardTextureRes);
    Destroy(flash, 0.15f);
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

    CreateCard(new Vector3(centerX, centerY, z), width, height, 0.02f, boardColor, outlineColor, boardCornerRadiusFrac, boardTextureRes);

    float dividerX = (leftSpawnPoint.position.x + rightSpawnPoint.position.x) / 2f;
    CreateCard(new Vector3(dividerX, centerY, z - 0.02f), railThickness * 1.5f, height, railThickness, outlineColor, outlineColor * 0.6f, 0.4f, 32);
  }

  void BuildLaneVisuals(Transform spawnPoint, Transform hitLine, Color laneColor)
  {
    float topY = spawnPoint.position.y + tileHeight;
    float bottomY = hitLine.position.y - tileHeight;
    float midY = (topY + bottomY) / 2f;
    float railLength = topY - bottomY;

    Vector3 leftRailPos = new Vector3(spawnPoint.position.x - tileWidth / 2f, midY, spawnPoint.position.z);
    Vector3 rightRailPos = new Vector3(spawnPoint.position.x + tileWidth / 2f, midY, spawnPoint.position.z);

    Color railBorder = laneColor * 0.4f;
    railBorder.a = 1f;
    CreateCard(leftRailPos, railThickness, railLength, railThickness, laneColor, railBorder, 0.4f, 32);
    CreateCard(rightRailPos, railThickness, railLength, railThickness, laneColor, railBorder, 0.4f, 32);

    Color glow = hitLineColor;
    glow.a = hitLineGlowAlpha;
    CreateGlow(hitLine.position + Vector3.back * 0.01f, tileWidth + railThickness * 2f + hitLineGlowSize, hitLineGlowSize, glow);

    Color lineBorder = hitLineColor * 0.5f;
    lineBorder.a = 1f;
    CreateCard(hitLine.position, tileWidth + railThickness * 2f, railThickness * 2.5f, railThickness, hitLineColor, lineBorder, 0.45f, 48);
  }

  GameObject CreateCard(Vector3 pos, float worldWidth, float worldHeight, float zThickness, Color fill, Color border, float cornerRadiusFrac, int texRes)
  {
    int texWidth = texRes;
    int texHeight = Mathf.Max(4, Mathf.RoundToInt(texRes * (worldHeight / worldWidth)));
    int cornerPx = Mathf.RoundToInt(cornerRadiusFrac * Mathf.Min(texWidth, texHeight));
    int borderPx = Mathf.Max(1, Mathf.RoundToInt(borderPadding / worldWidth * texWidth));

    Texture2D tex = MakeCardTexture(texWidth, texHeight, fill, border, cornerPx, borderPx);

    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.transform.SetParent(transform);
    go.transform.position = pos;
    go.transform.localRotation = Quaternion.identity;
    go.transform.localScale = new Vector3(worldWidth, worldHeight, zThickness);
    Destroy(go.GetComponent<Collider>());
    go.GetComponent<Renderer>().material = MakeTexturedMaterial(tex);
    return go;
  }

  GameObject CreateGlow(Vector3 pos, float worldWidth, float worldHeight, Color glowColor)
  {
    Texture2D tex = MakeGlowTexture(16, 64, glowColor);

    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.transform.SetParent(this.transform);
    go.transform.position = pos;
    go.transform.localScale = new Vector3(worldWidth, worldHeight, 0.01f);
    Destroy(go.GetComponent<Collider>());
    go.GetComponent<Renderer>().material = MakeTexturedMaterial(tex);
    return go;
  }

  float RoundedBoxSDF(Vector2 p, Vector2 halfSize, float radius)
  {
    Vector2 q = new Vector2(Mathf.Abs(p.x) - halfSize.x + radius, Mathf.Abs(p.y) - halfSize.y + radius);
    float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
    float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
    return outside + inside - radius;
  }

  Texture2D MakeCardTexture(int texWidth, int texHeight, Color fill, Color border, int cornerPx, int borderPx)
  {
    Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
    Vector2 outerHalf = new Vector2(texWidth / 2f, texHeight / 2f);
    Vector2 innerHalf = new Vector2(Mathf.Max(outerHalf.x - borderPx, 0f), Mathf.Max(outerHalf.y - borderPx, 0f));
    float innerRadius = Mathf.Max(cornerPx - borderPx, 0f);

    for(int y = 0; y < texHeight; y++)
    {
      float t = (float)y / texHeight;
      Color gradFill = Color.Lerp(fill * 0.85f, fill * 1.15f, t);
      gradFill.a = fill.a;
      gradFill.r = Mathf.Clamp01(gradFill.r);
      gradFill.g = Mathf.Clamp01(gradFill.g);
      gradFill.b = Mathf.Clamp01(gradFill.b);

      for(int x = 0; x < texWidth; x++)
      {
        Vector2 p = new Vector2(x - outerHalf.x + 0.5f, y - outerHalf.y + 0.5f);
        float dOuter = RoundedBoxSDF(p, outerHalf, cornerPx);
        float dInner = RoundedBoxSDF(p, innerHalf, innerRadius);

        float outerAlpha = Mathf.Clamp01(0.5f - dOuter);
        float innerMask = Mathf.Clamp01(0.5f - dInner);

        Color c = Color.Lerp(border, gradFill, innerMask);
        c.a = outerAlpha;
        tex.SetPixel(x, y, c);
      }
    }

    tex.Apply();
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    return tex;
  }

  Texture2D MakeGlowTexture(int texWidth, int texHeight, Color glowColor)
  {
    Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
    for(int y = 0; y < texHeight; y++)
    {
      float t = Mathf.Abs((y - texHeight / 2f) / (texHeight / 2f));
      float falloff = Mathf.Clamp01(1f - t);
      falloff *= falloff;
      Color c = glowColor;
      c.a = glowColor.a * falloff;

      for(int x = 0; x < texWidth; x++)
        tex.SetPixel(x, y, c);
    }
    tex.Apply();
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    return tex;
  }

  Material MakeTexturedMaterial(Texture2D tex)
  {
    Shader shader = Shader.Find("Sprites/Default");
    if(shader == null) shader = Shader.Find("UI/Default");
    if(shader == null) shader = Shader.Find("Standard");

    Material mat = new Material(shader);
    mat.mainTexture = tex;
    mat.color = Color.white;
    return mat;
  }
}