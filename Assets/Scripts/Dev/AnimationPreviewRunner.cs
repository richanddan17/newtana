using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Body-only animation preview for the 23 gameplay clips.
/// Independent of PlayerAnimation / PlayerStateController so it can be inspected in isolation.
/// </summary>
public class AnimationPreviewRunner : MonoBehaviour
{
    [SerializeField] PlayerAnimationSet set;
    [SerializeField] SpriteRenderer target;

    public static readonly string[] ClipIds =
    {
        "idle", "run", "jump", "fall", "crouch", "dash",
        "attack1", "attack2", "attack3", "jumpAttack", "upAttack", "downAttack",
        "heavyAttack", "bow", "doubleJump", "wallSlide", "climb", "enterDoor", "grapple",
        "cast", "hurt", "hurtKnockback", "death"
    };

    public PlayerAnimationSet AnimationSet => set;
    public SpriteRenderer TargetRenderer => target;

    public void ConfigureAndPreview(PlayerAnimationSet animationSet, SpriteRenderer body, Camera camera)
    {
        set = animationSet;
        target = body;
        Debug.Log("[AnimationPreview] ConfigureAndPreview set=" + (set != null) + ", target=" + (target != null));
        ShowClip(0, 0);
        AlignFeetToGround();
        FramePreviewCamera(camera);
    }
    public string CurrentClipId => ClipIds != null && ClipIds.Length > 0 ? ClipIds[clipIndex] : string.Empty;
    public int CurrentClipIndex => clipIndex;
    public int CurrentFrameIndex => frameIndex;
    public bool IsPaused => paused;
    public float Speed => speed;

    int clipIndex;
    int frameIndex;
    float frameTimer;
    float speed = 1f;
    bool paused;

    GUIStyle listStyle;
    GUIStyle hudStyle;
    Camera previewCamera;
    LineRenderer groundGuide;
    SpriteRenderer vfxRenderer;

    void Start()
    {
        if (!HasRequiredReferences())
        {
            enabled = false;
            return;
        }

        EnsureVfxRenderer();
        ApplyInitialPreviewState();
        EnsureGroundGuide();
    }

    void Update()
    {
        if (!HasRequiredReferences())
            return;

        HandleInput();
        Advance();
    }

    bool HasRequiredReferences()
    {
        if (set != null && target != null)
            return true;

        Debug.LogError("[AnimationPreview] runner is missing set and/or target.");
        return false;
    }

    /// <summary>Start() and the editor builder both use this so the scene opens already framed.</summary>
    public void ApplyInitialPreviewState(Camera camera = null)
    {
        if (!HasRequiredReferences())
            return;

        EnsureVfxRenderer();
        ShowClip(0, 0);
        AlignFeetToGround();
        FramePreviewCamera(camera);
    }

    void AlignFeetToGround()
    {
        if (target == null)
            return;

        float feetY = target.bounds.min.y;
        if (Mathf.Abs(feetY) <= 0.0001f)
            return;

        target.transform.position -= new Vector3(0f, feetY, 0f);
        Debug.Log("[AnimationPreview] feet aligned by " + (-feetY).ToString("0.###") + " units");
    }

    public void FramePreviewCamera(Camera camera = null)
    {
        if (target == null || target.sprite == null)
        {
            Debug.LogWarning("[AnimationPreview] cannot frame camera: target/sprite missing.");
            return;
        }

        var cam = camera != null ? camera : (previewCamera != null ? previewCamera : Camera.main);
        if (cam == null)
        {
            Debug.LogWarning("[AnimationPreview] cannot frame camera: no Camera found.");
            return;
        }
        previewCamera = cam;

        Bounds bounds = target.bounds;
        float padding = Mathf.Max(0.05f, bounds.size.y * 0.08f);
        float bottom = Mathf.Min(bounds.min.y, 0f) - padding;
        float top = Mathf.Max(bounds.max.y, 0f) + padding;
        float halfHeight = (top - bottom) * 0.5f;
        float halfWidth = bounds.extents.x + padding;
        float aspect = cam.aspect > 0.01f ? cam.aspect : (16f / 9f);

        cam.orthographic = true;
        cam.transform.position = new Vector3(bounds.center.x, (bottom + top) * 0.5f, -10f);
        cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(0x1E, 0x1E, 0x1E, 0xFF);

        float viewBottom = cam.transform.position.y - cam.orthographicSize;
        float viewTop = cam.transform.position.y + cam.orthographicSize;
        Debug.Log("[AnimationPreview] camera framed: pos=" + cam.transform.position +
                  ", ortho=" + cam.orthographicSize.ToString("0.###") +
                  ", spriteBounds=" + bounds +
                  ", groundVisible=" + (viewBottom <= 0f && viewTop >= 0f) +
                  ", feetY=" + bounds.min.y.ToString("0.###"));
    }

    void HandleInput()
    {
        if (KeyDown(KeyCode.LeftArrow))
            ShowClip(clipIndex - 1, 0);
        if (KeyDown(KeyCode.RightArrow))
            ShowClip(clipIndex + 1, 0);
        if (KeyDown(KeyCode.Space))
            paused = !paused;
        if (KeyDown(KeyCode.R))
            ShowClip(clipIndex, 0, unpause: true);
        if (KeyDown(KeyCode.UpArrow))
            speed = Mathf.Clamp(speed + 0.25f, 0.25f, 4f);
        if (KeyDown(KeyCode.DownArrow))
            speed = Mathf.Clamp(speed - 0.25f, 0.25f, 4f);
    }

    static bool KeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        switch (key)
        {
            case KeyCode.LeftArrow: return keyboard.leftArrowKey.wasPressedThisFrame;
            case KeyCode.RightArrow: return keyboard.rightArrowKey.wasPressedThisFrame;
            case KeyCode.UpArrow: return keyboard.upArrowKey.wasPressedThisFrame;
            case KeyCode.DownArrow: return keyboard.downArrowKey.wasPressedThisFrame;
            case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
            case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
            default: return false;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    void Advance()
    {
        if (paused)
            return;

        var clip = CurrentClip;
        if (clip == null || clip.frames == null || clip.frames.Length <= 1 || clip.frameInterval <= 0f)
            return;

        frameTimer += Time.deltaTime * speed;
        int guard = 0;
        while (frameTimer >= clip.frameInterval && guard++ < 1000)
        {
            frameTimer -= clip.frameInterval;
            if (frameIndex + 1 < clip.frames.Length)
            {
                frameIndex++;
            }
            else if (clip.loop)
            {
                frameIndex = 0;
            }
            else
            {
                frameIndex = clip.frames.Length - 1;
                frameTimer = 0f;
                break;
            }
        }

        ApplyFrame();
    }

    public void ShowClip(int index, int frame, bool unpause = false)
    {
        if (ClipIds == null || ClipIds.Length == 0)
            return;

        clipIndex = ((index % ClipIds.Length) + ClipIds.Length) % ClipIds.Length;
        var clip = CurrentClip;
        if (clip == null || clip.frames == null || clip.frames.Length == 0)
        {
            frameIndex = 0;
            frameTimer = 0f;
            if (unpause)
                paused = false;
            return;
        }

        frameIndex = Mathf.Clamp(frame, 0, clip.frames.Length - 1);
        frameTimer = 0f;
        if (unpause)
            paused = false;
        ApplyFrame();
    }

    PlayerAnimationClip CurrentClip => set != null && ClipIds != null && ClipIds.Length > 0
        ? ResolveClip(set, ClipIds[clipIndex])
        : null;

    void ApplyFrame()
    {
        var clip = CurrentClip;
        if (clip == null || clip.frames == null || clip.frames.Length == 0)
            return;

        frameIndex = Mathf.Clamp(frameIndex, 0, clip.frames.Length - 1);
        var sprite = clip.frames[frameIndex];
        if (sprite == null)
        {
            Debug.LogError("[AnimationPreview] null frame " + frameIndex + " in clip " + CurrentClipId);
            return;
        }

        if (target.sprite != sprite)
            target.sprite = sprite;

        UpdateVfxForCurrentClip();
    }

    void EnsureVfxRenderer()
    {
        if (vfxRenderer != null)
            return;

        var go = new GameObject("PreviewFX");
        go.transform.SetParent(transform, false);
        vfxRenderer = go.AddComponent<SpriteRenderer>();
        vfxRenderer.enabled = false;
    }

    void HideVfx()
    {
        if (vfxRenderer != null)
            vfxRenderer.enabled = false;
    }

    void UpdateVfxForCurrentClip()
    {
        if (set == null || target == null)
        {
            HideVfx();
            return;
        }

        EnsureVfxRenderer();

        PlayerAnimationClip vfx = null;
        int firstFrame = 0;
        switch (CurrentClipId)
        {
            case "attack1":
                vfx = set.attack1Fx;
                firstFrame = 3;
                break;
            case "attack2":
                vfx = set.attack2Fx;
                firstFrame = 1;
                break;
            case "attack3":
                vfx = set.attack3Fx;
                firstFrame = 5;
                break;
            case "heavyAttack":
                vfx = set.heavyFx;
                firstFrame = 21;
                break;
            case "bow":
                if (frameIndex == 9)
                {
                    vfx = set.arrowRelease;
                    firstFrame = 9;
                }
                break;
            case "cast":
                if (frameIndex == 2)
                {
                    vfx = set.castGather;
                    firstFrame = 2;
                }
                else if (frameIndex == 3 || frameIndex == 4)
                {
                    vfx = set.castChargeLoop;
                    firstFrame = 3;
                }
                else if (frameIndex >= 6 && frameIndex <= 10)
                {
                    vfx = set.castRelease;
                    firstFrame = 6;
                }
                break;
        }

        int vfxFrame = frameIndex - firstFrame;
        if (vfx == null || vfx.frames == null || vfx.frames.Length == 0 ||
            vfxFrame < 0 || vfxFrame >= vfx.frames.Length)
        {
            HideVfx();
            return;
        }

        Bounds bounds = target.bounds;
        float facing = target.transform.lossyScale.x < 0f ? -1f : 1f;
        Vector2 offset = vfx.frameOffsets != null && vfxFrame < vfx.frameOffsets.Length
            ? vfx.frameOffsets[vfxFrame]
            : Vector2.zero;

        Vector3 position = vfx.footAnchored
            ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)
            : bounds.center;

        if (vfx.footAnchored)
        {
            position.x += offset.x * facing;
            position.y += offset.y;
        }

        vfxRenderer.sprite = vfx.frames[vfxFrame];
        vfxRenderer.enabled = true;
        vfxRenderer.transform.position = position;
        vfxRenderer.transform.localScale = new Vector3(facing, 1f, 1f);
        vfxRenderer.sortingLayerID = target.sortingLayerID;
        vfxRenderer.sortingOrder = target.sortingOrder + 1;
    }

    /// <summary>Explicit id -> public field mapping. Only the 22 allowlisted body clips are returned.</summary>
    public static PlayerAnimationClip ResolveClip(PlayerAnimationSet animationSet, string id)
    {
        if (animationSet == null || id == null)
            return null;

        switch (id)
        {
            case "idle": return animationSet.idle;
            case "run": return animationSet.run;
            case "jump": return animationSet.jump;
            case "fall": return animationSet.fall;
            case "crouch": return animationSet.crouch;
            case "dash": return animationSet.dash;
            case "attack1": return animationSet.attack1;
            case "attack2": return animationSet.attack2;
            case "attack3": return animationSet.attack3;
            case "jumpAttack": return animationSet.jumpAttack;
            case "upAttack": return animationSet.upAttack;
            case "downAttack": return animationSet.downAttack;
            case "heavyAttack": return animationSet.heavyAttack;
            case "bow": return animationSet.bow;
            case "doubleJump": return animationSet.doubleJump;
            case "wallSlide": return animationSet.wallSlide;
            case "climb": return animationSet.climb;
            case "enterDoor": return animationSet.enterDoor;
            case "grapple": return animationSet.grapple;
            case "cast": return animationSet.cast;
            case "hurt": return animationSet.hurt;
            case "hurtKnockback": return animationSet.hurtKnockback;
            case "death": return animationSet.death;
            default: return null;
        }
    }

    void EnsureGroundGuide()
    {
        if (groundGuide != null)
            return;

        var go = new GameObject("GroundGuide");
        go.transform.SetParent(transform, false);
        groundGuide = go.AddComponent<LineRenderer>();
        groundGuide.useWorldSpace = true;
        groundGuide.positionCount = 2;
        groundGuide.SetPosition(0, new Vector3(-100f, 0f, 0f));
        groundGuide.SetPosition(1, new Vector3(100f, 0f, 0f));
        groundGuide.startWidth = 0.02f;
        groundGuide.endWidth = 0.02f;
        groundGuide.startColor = new Color(0.55f, 0.75f, 1f, 0.9f);
        groundGuide.endColor = groundGuide.startColor;
        groundGuide.sortingOrder = -1;

        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
            groundGuide.material = new Material(shader) { name = "GroundGuide (Runtime)" };
        else
            Debug.LogWarning("[AnimationPreview] Sprites/Default shader not found.");
    }

    void OnGUI()
    {
        if (set == null)
            return;

        EnsureStyles();

        const float listX = 10f;
        const float listY = 10f;
        const float rowHeight = 20f;
        const float listWidth = 190f;
        float listHeight = ClipIds.Length * rowHeight + 8f;

        GUI.Box(new Rect(listX - 6f, listY - 6f, listWidth + 12f, listHeight + 12f), GUIContent.none);
        for (int i = 0; i < ClipIds.Length; i++)
        {
            var rect = new Rect(listX, listY + i * rowHeight, listWidth, rowHeight - 2f);
            if (i == clipIndex)
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 0.9f);
                GUI.Box(rect, GUIContent.none);
                GUI.backgroundColor = previous;
            }

            GUI.Label(rect, (i + 1).ToString("00") + "  " + ClipIds[i], listStyle);
        }

        var clip = CurrentClip;
        int frameCount = clip != null && clip.frames != null ? clip.frames.Length : 0;
        int shownFrame = frameCount > 0 ? frameIndex + 1 : 0;
        string info = "Clip: " + CurrentClipId + "\n" +
                      "Frame: " + shownFrame + "/" + frameCount + "\n" +
                      "Interval: " + (clip != null ? clip.frameInterval.ToString("0.###") : "-") + " s\n" +
                      "Loop: " + (clip != null ? clip.loop.ToString() : "-") + "\n" +
                      "Speed: " + speed.ToString("0.00") + "x\n" +
                      "Paused: " + paused + "\n" +
                      "Left/Right clip   Space pause   R restart   Up/Down speed";

        GUI.Box(new Rect(Screen.width - 350f, 10f, 340f, 160f), GUIContent.none);
        GUI.Label(new Rect(Screen.width - 340f, 18f, 320f, 150f), info, hudStyle);
    }

    void EnsureStyles()
    {
        if (listStyle != null)
            return;

        listStyle = new GUIStyle(GUI.skin.label);
        listStyle.fontSize = 13;
        listStyle.alignment = TextAnchor.MiddleLeft;
        listStyle.normal.textColor = Color.white;

        hudStyle = new GUIStyle(GUI.skin.label);
        hudStyle.fontSize = 14;
        hudStyle.alignment = TextAnchor.UpperLeft;
        hudStyle.normal.textColor = Color.white;
    }
}
