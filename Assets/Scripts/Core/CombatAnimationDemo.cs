using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class CombatAnimationDemo : MonoBehaviour
{
    enum WeaponMode { Sword, Bow, Cast }
    enum CastMode { Charge, Rapid }
    enum BowState { None, Draw, HoldLoop, Release }
    enum MagicState { None, BasicPlay, RapidFirst, RapidLoop, BigLoop, BigRelease }

    [SerializeField] PlayerAnimation playerAnimation;
    [SerializeField] PlayerInputHandler inputHandler;
    [SerializeField] PlayerStateController stateController;
    [SerializeField] PlayerMovement playerMovement;
    [SerializeField] bool enableDemo = true;
    [SerializeField] float heavyHoldThreshold = 0.22f;
    [SerializeField] float comboResetWindow = 0.55f;
    [SerializeField] float castHoldThreshold = 0.5f;
    [SerializeField] float comboChainDelay = 0.03f;
    [SerializeField] float heavyLungeDistance = 1.6f;
    [SerializeField] float heavyLungeDuration = 0.18f;
    [SerializeField] float castMuzzleYOffset = 0.35f;

    WeaponMode weaponMode = WeaponMode.Sword;
    int comboStep = -1;
    float lastAttackTime = -999f;
    bool attackHeld;
    float attackHoldTime;
    bool heavyCharging;
    bool heavyLooping;
    float heavyWindupTimer;
    float heavyWindupDuration;
    bool attackLocked;
    bool attackQueued;
    float chainTimer;

    BowState bowState = BowState.None;
    bool bowActive;
    int rapidCount = 0;
    bool magicFiredThisState = false;

    CastMode castMode = CastMode.Rapid;
    PlayerCastFx castFx;
    bool castActive;
    MagicState magicState = MagicState.None;

    PlayerMeleeVfx meleeVfx;
    PlayerAnimationClip meleeFx;
    int meleeFirstFrame;
    bool meleeApplyVfxSpeed = true;
    int meleeSortingOffset = 1;

    void Awake()
    {
        if (playerAnimation == null) playerAnimation = GetComponent<PlayerAnimation>();
        if (inputHandler == null) inputHandler = GetComponent<PlayerInputHandler>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
        meleeVfx = GetComponent<PlayerMeleeVfx>();
        if (meleeVfx == null) meleeVfx = gameObject.AddComponent<PlayerMeleeVfx>();
        castFx = GetComponent<PlayerCastFx>();
        if (castFx == null) castFx = gameObject.AddComponent<PlayerCastFx>();
    }

    void OnEnable()
    {
        if (playerAnimation != null) playerAnimation.OnDebugClipFrame += OnDebugClipFrame;
        if (playerAnimation != null) playerAnimation.OnDebugClipFinished += OnDebugClipFinished;
        if (inputHandler == null) return;
        inputHandler.OnShootPressed += OnAttackPressed;
        inputHandler.OnShootReleased += OnAttackReleased;
    }

    void OnDisable()
    {
        if (playerAnimation != null) playerAnimation.OnDebugClipFrame -= OnDebugClipFrame;
        if (playerAnimation != null) playerAnimation.OnDebugClipFinished -= OnDebugClipFinished;
        if (inputHandler != null)
        {
            inputHandler.OnShootPressed -= OnAttackPressed;
            inputHandler.OnShootReleased -= OnAttackReleased;
        }
        attackHeld = false;
        bowActive = false;
        bowState = BowState.None;
        attackLocked = false;
        attackQueued = false;
        chainTimer = 0f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = false;
        if (playerMovement != null) playerMovement.CancelLunge();
        ResetHeavy();
        ResetCast();
        if (castFx != null) castFx.StopAll();
    }

    void Update()
    {
        if (!enableDemo || playerAnimation == null) return;
        HandleWeaponSwitch();
        if (weaponMode == WeaponMode.Cast) { UpdateCast(); return; }
        if (weaponMode != WeaponMode.Sword) return;
        if (attackHeld)
        {
            attackHoldTime += Time.deltaTime;
            if (!heavyCharging && attackHoldTime >= heavyHoldThreshold) StartHeavy();
        }
        if (heavyCharging && !heavyLooping)
        {
            heavyWindupTimer += Time.deltaTime;
            if (heavyWindupTimer >= heavyWindupDuration) { heavyLooping = true; PlayHeavyLoop(); }
        }
        if (chainTimer > 0f)
        {
            chainTimer -= Time.deltaTime;
            if (chainTimer <= 0f && attackQueued) { attackQueued = false; var s = GetAnimationSet(); if (s != null) PlaySwordCombo(s, true); }
        }
    }

    void HandleWeaponSwitch()
    {
        Keyboard kb = Keyboard.current;
        Mouse ms = Mouse.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) SetWeapon(WeaponMode.Sword);
        if (kb.digit2Key.wasPressedThisFrame) SetWeapon(WeaponMode.Bow);
        if (kb.digit3Key.wasPressedThisFrame) SetWeapon(WeaponMode.Cast);
        if (kb.eKey.wasPressedThisFrame) ToggleCastMode();
        if (kb.qKey.wasPressedThisFrame && stateController != null) stateController.TryDash();
        if (ms != null)
        {
            Vector2 sc = ms.scroll.ReadValue();
            if (sc.y > 0.01f) CycleWeapon(1);
            else if (sc.y < -0.01f) CycleWeapon(-1);
        }
    }

    void CycleWeapon(int dir) { int next = ((int)weaponMode + dir + 3) % 3; SetWeapon((WeaponMode)next); }
    void SetWeapon(WeaponMode mode)
    {
        if (weaponMode == mode) return;
        weaponMode = mode;
        attackHeld = false; bowActive = false; bowState = BowState.None;
        attackLocked = false; attackQueued = false; chainTimer = 0f; comboStep = -1; lastAttackTime = -999f;
        ResetHeavy(); ResetCast(); if (castFx != null) castFx.StopAll();
        if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); }
    }

    void OnAttackPressed(InputAction.CallbackContext ctx)
    {
        if (!enableDemo || playerAnimation == null) return;
        var set = GetAnimationSet(); if (set == null) return;
        if (weaponMode == WeaponMode.Bow) { bowHoldDown = true; if (!bowActive) StartBow(set); return; }
        if (weaponMode == WeaponMode.Cast) { StartCast(set); return; }
        if (weaponMode == WeaponMode.Sword)
        {
            if (playerMovement != null && !playerMovement.IsGrounded) { PlaySwordJumpAttack(set); return; }
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                bool w = kb.wKey.isPressed; bool s = kb.sKey.isPressed;
                if (w && !s) { PlaySwordUpAttack(set); return; }
                if (s && !w) { PlaySwordDownAttack(set); return; }
            }
            attackHeld = true; attackHoldTime = 0f; heavyCharging = false; heavyLooping = false; heavyWindupTimer = 0f;
            RequestSwordAttack(set);
            return;
        }
    }

    void OnAttackReleased(InputAction.CallbackContext ctx)
    {
        if (!enableDemo) return;
        if (weaponMode == WeaponMode.Bow)
        {
            if (bowActive && bowState == BowState.HoldLoop) ReleaseBow();
            else if (bowActive && bowState == BowState.Draw) ReleaseBow();
            else StopBow();
            return;
        }
        if (weaponMode == WeaponMode.Cast) { if (castActive) ReleaseCast(); return; }
        if (weaponMode == WeaponMode.Sword)
        {
            attackHeld = false;
            if (heavyCharging && !heavyLooping) { ResetHeavy(); if (playerAnimation != null) playerAnimation.FreezeFacing = false; }
        }
    }

    PlayerAnimationSet GetAnimationSet() { if (playerAnimation == null) return null; return playerAnimation.AnimationSet; }

    #region Bow
    void StartBow(PlayerAnimationSet set)
    {
        var bow = set.bow; if (bow == null || bow.frames == null || bow.frames.Length <= 10) return;
        bowActive = true; bowState = BowState.Draw; attackLocked = true;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
        PlayRange(bow, 2, 10, false, "bowDraw");
    }
    void PlayBowHold(PlayerAnimationSet set)
    {
        if (set == null) set = GetAnimationSet(); if (set == null || set.bow == null) return;
        bowState = BowState.HoldLoop;
        PlayRange(set.bow, 11, 16, true, "bowHold");
    }
    void ReleaseBow()
    {
        if (!bowActive) return;
        var set = GetAnimationSet(); var bow = set != null ? set.bow : null;
        if (bow == null || bow.frames == null) { StopBow(); return; }
        bowState = BowState.Release; attackLocked = true;
        PlayRangeReverse(bow, 16, 4, false, "bowRelease");
    }
    void StopBow()
    {
        bowActive = false; bowState = BowState.None; attackLocked = false;
        if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); }
    }
    void PlayRangeReverse(PlayerAnimationClip clip, int startFrame, int endFrame, bool loop, string id)
    {
        if (clip == null || clip.frames == null) return;
        if (startFrame < 0) startFrame = 0; if (endFrame >= clip.frames.Length) endFrame = clip.frames.Length - 1;
        int count = Mathf.Abs(startFrame - endFrame) + 1; if (count <= 0) return;
        Sprite[] frames = new Sprite[count]; float[] durs = new float[count];
        for (int i = 0; i < count; i++) { int src = startFrame - i; if (src < endFrame) src = endFrame; frames[i] = clip.frames[src]; durs[i] = clip.GetFrameDuration(src) / (playerAnimation != null ? playerAnimation.PlaybackSpeed : 1f); }
        var pb = new PlayerAnimationClip { id = id, frames = frames, frameInterval = clip.frameInterval, frameDurations = durs, loop = loop };
        if (playerAnimation != null) { playerAnimation.PlayDebugClip(pb); playerAnimation.FreezeFacing = true; }
    }
    #endregion

    #region Magic
    void StartCast(PlayerAnimationSet set)
    {
        if (set == null) return;
        ResetCast(); castActive = true; attackLocked = true; magicFiredThisState = false;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
        if (castFx != null) castFx.StopAll();
        if (castMode == CastMode.Rapid)
        {
            rapidCount++;
            if (rapidCount == 1) { magicState = MagicState.RapidFirst; if (set.magicRapid != null) PlayRange(set.magicRapid, 0, 8, false, "magicRapidFirst"); }
            else { magicState = MagicState.RapidLoop; if (set.magicRapid != null) PlayRange(set.magicRapid, 9, 11, true, "magicRapidLoop"); }
            return;
        }
        if (castMode == CastMode.Charge) { magicState = MagicState.BigLoop; if (set.magicBig != null) PlayRange(set.magicBig, 16, 32, true, "magicBigLoop"); return; }
        magicState = MagicState.BasicPlay; if (set.magicBasic != null) PlayRange(set.magicBasic, 0, 12, false, "magicBasic");
    }
    void UpdateCast() { }
    void ReleaseCast()
    {
        if (!castActive) return;
        var set = GetAnimationSet();
        if (castMode == CastMode.Charge && magicState == MagicState.BigLoop)
        {
            magicState = MagicState.BigRelease; magicFiredThisState = false;
            if (set != null && set.magicBig != null) PlayRange(set.magicBig, 33, set.magicBig.frames.Length - 1, false, "magicBigRelease");
            return;
        }
        if (castMode == CastMode.Rapid && magicState == MagicState.RapidLoop)
        {
            ResetCast(); if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); } attackLocked = false; return;
        }
        ResetCast(); if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); } attackLocked = false;
    }
    void ResetCast() { castActive = false; magicState = MagicState.None; magicFiredThisState = false; if (castFx != null) castFx.HideAura(); }
    void ToggleCastMode() { if (castActive) return; castMode = castMode == CastMode.Charge ? CastMode.Rapid : CastMode.Charge; rapidCount = 0; }
    #endregion

    #region Frames
    void OnDebugClipFrame(int frame)
    {
        if (bowActive && weaponMode == WeaponMode.Bow)
        {
            if (bowState == BowState.Draw && frame == 7) FireBow();
            if (bowState == BowState.HoldLoop && frame == 4) FireBow();
        }
        if (weaponMode == WeaponMode.Cast && castActive)
        {
            if (magicState == MagicState.RapidFirst && frame == 6 && !magicFiredThisState) { magicFiredThisState = true; FireCast(CastShotType.Rapid); }
            if (magicState == MagicState.RapidLoop && frame == 0 && !magicFiredThisState) { magicFiredThisState = true; FireCast(CastShotType.Rapid); }
            if (magicState == MagicState.BasicPlay && frame == 12 && !magicFiredThisState) { magicFiredThisState = true; FireCast(CastShotType.Basic); }
            if (magicState == MagicState.BigRelease && frame == 3 && !magicFiredThisState) { magicFiredThisState = true; FireCast(CastShotType.Big); }
        }
    }

    void OnDebugClipFinished(PlayerAnimationClip clip)
    {
        if (clip == null) return;
        string id = clip.id;
        if (id == "bowDraw") { if (bowActive && bowHoldDown) PlayBowHold(GetAnimationSet()); else if (bowActive) ReleaseBow(); return; }
        if (id == "bowRelease") { StopBow(); return; }
        if (id == "magicRapidFirst") { if (castActive && castMode == CastMode.Rapid) { magicState = MagicState.RapidLoop; var s = GetAnimationSet(); if (s != null && s.magicRapid != null) PlayRange(s.magicRapid, 9, 11, true, "magicRapidLoop"); } return; }
        if (id == "magicBasic") { ResetCast(); if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); } attackLocked = false; return; }
        if (id == "magicBigRelease") { ResetCast(); if (playerAnimation != null) { playerAnimation.FreezeFacing = false; playerAnimation.ClearDebugClip(); } attackLocked = false; return; }
        if (id == "heavyRelease") { attackLocked = false; attackQueued = false; chainTimer = 0f; if (playerAnimation != null) playerAnimation.FreezeFacing = false; return; }
        if (id == "attack1" || id == "attack2" || id == "attack3" || id == "jumpAttack" || id == "upAttack" || id == "downAttack") { attackLocked = false; lastAttackTime = Time.time; if (attackQueued) chainTimer = comboChainDelay; else if (playerAnimation != null) playerAnimation.FreezeFacing = false; return; }
    }
    #endregion

    #region Sword/Heavy
    void RequestSwordAttack(PlayerAnimationSet set) { if (attackLocked) { attackQueued = true; return; } PlaySwordCombo(set, false); }
    void PlaySwordCombo(PlayerAnimationSet set, bool chained)
    {
        if (!chained && (Time.time - lastAttackTime > comboResetWindow || comboStep < 0)) comboStep = 0; else comboStep = (comboStep + 1) % 3;
        lastAttackTime = Time.time; attackLocked = true; attackQueued = false; chainTimer = 0f; bowActive = false; bowState = BowState.None;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
        var clip = comboStep == 0 ? set.attack1 : comboStep == 1 ? set.attack2 : set.attack3;
        if (clip == null) { attackLocked = false; return; }
        if (playerAnimation != null) playerAnimation.PlayDebugClip(clip);
    }
    void PlaySwordJumpAttack(PlayerAnimationSet set) { if (attackLocked) { attackQueued = true; return; } attackLocked = true; if (playerAnimation != null) playerAnimation.FreezeFacing = true; if (set.jumpAttack != null && playerAnimation != null) playerAnimation.PlayDebugClip(set.jumpAttack); }
    void PlaySwordUpAttack(PlayerAnimationSet set) { if (attackLocked) { attackQueued = true; return; } attackLocked = true; if (playerAnimation != null) playerAnimation.FreezeFacing = true; if (set.upAttack != null && playerAnimation != null) playerAnimation.PlayDebugClip(set.upAttack); }
    void PlaySwordDownAttack(PlayerAnimationSet set) { if (attackLocked) { attackQueued = true; return; } attackLocked = true; if (playerAnimation != null) playerAnimation.FreezeFacing = true; if (set.downAttack != null && playerAnimation != null) playerAnimation.PlayDebugClip(set.downAttack); }
    void StartHeavy()
    {
        var set = GetAnimationSet(); if (set == null || set.heavyAttack == null) { ResetHeavy(); return; }
        heavyCharging = true; heavyLooping = false; heavyWindupTimer = 0f; heavyWindupDuration = 0.3f; attackLocked = true;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
        PlayRange(set.heavyAttack, 0, 2, false, "heavyWindup");
    }
    void PlayHeavyLoop() { var set = GetAnimationSet(); if (set == null || set.heavyAttack == null) { ResetHeavy(); return; } PlayRange(set.heavyAttack, 3, 20, true, "heavyLoop"); }
    void ResetHeavy() { heavyCharging = false; heavyLooping = false; heavyWindupTimer = 0f; }
    void PlayRange(PlayerAnimationClip clip, int start, int end, bool loop, string id)
    {
        if (clip == null || clip.frames == null) return;
        if (start < 0) start = 0; if (end >= clip.frames.Length) end = clip.frames.Length - 1;
        int count = end - start + 1; if (count <= 0) return;
        Sprite[] frames = new Sprite[count]; float[] durs = new float[count];
        for (int i = 0; i < count; i++) { frames[i] = clip.frames[start + i]; durs[i] = clip.GetFrameDuration(start + i) / (playerAnimation != null ? playerAnimation.PlaybackSpeed : 1f); }
        var pb = new PlayerAnimationClip { id = id, frames = frames, frameInterval = clip.frameInterval, frameDurations = durs, loop = loop };
        if (playerAnimation != null) { playerAnimation.PlayDebugClip(pb); playerAnimation.FreezeFacing = true; }
    }
    void FireBow()
    {
        var set = GetAnimationSet(); if (set == null || playerAnimation == null) return;
        Vector2 dir = playerAnimation.GetCurrentAimDirectionVector(); if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        Vector3 tip = PlayerBowShot.GetBowTip(playerAnimation.BodyRenderer, dir, playerAnimation.GetMuzzleWorldPosition());
        PlayerBowShot.Fire(set, tip, dir, gameObject, playerAnimation.BodyRenderer);
    }
    void FireCast(CastShotType type)
    {
        var set = GetAnimationSet(); if (set == null || playerAnimation == null) return;
        float facing = transform.localScale.x < 0f ? -1f : 1f;
        Vector2 dir = new Vector2(facing, 0f);
        Vector3 origin = playerAnimation.GetMuzzleWorldPosition(); origin.y -= castMuzzleYOffset;
        PlayerCastShot.Fire(set, type, origin, dir, gameObject, playerAnimation.BodyRenderer);
    }
    #endregion

    void OnGUI()
    {
        if (!enableDemo) return;
        GUILayout.BeginArea(new Rect(10,10,560,80));
        GUILayout.Label("Sword: LMB combo/Hold heavy   Bow: 2-10 draw->hold 11-16 (fire)   Cast: Rapid first 0-8 fire6 / loop 9-11 fire9   Big hold 16-32 release 33+ fire36   Basic 0-12 fire12");
        GUILayout.EndArea();
    }
}
