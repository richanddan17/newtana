using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class CombatAnimationDemo : MonoBehaviour
{
    enum WeaponMode
    {
        Sword,
        Bow,
        Cast
    }

    enum CastMode
    {
        Charge,
        Rapid
    }

    [SerializeField] PlayerAnimation playerAnimation;
    [SerializeField] PlayerInputHandler inputHandler;
    [SerializeField] PlayerStateController stateController;
    [SerializeField] PlayerMovement playerMovement;
    [SerializeField] bool enableDemo = true;
    [SerializeField] float heavyHoldThreshold = 0.22f;
    [SerializeField] float comboResetWindow = 0.55f;
    [SerializeField] float castHoldThreshold = 0.5f;
    [SerializeField] float rapidShotInterval = 0.2f;
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
    bool bowActive;
    bool bowLooping;
    bool pendingLightAttack;
    bool attackLocked;
    bool attackQueued;
    float chainTimer;

    CastMode castMode = CastMode.Rapid;
    PlayerCastFx castFx;
    bool castActive;
    float castHoldTime;
    bool castCharging;
    bool castBigCharging;
    bool castRapidFiring;
    float castNextRapidTime;
    CastShotType castPendingShot;
    bool hasPendingCastShot;
    bool castGatherStarted;

    PlayerMeleeVfx meleeVfx;
    PlayerAnimationClip meleeFx;
    PlayerAnimationClip meleeHitFx;
    int meleeFirstFrame;
    int meleeLastFrame;
    bool meleeApplyVfxSpeed = true;
    int meleeSortingOffset = 1;
    float meleeHitRadius = 1.1f;
    bool meleeHitDone;

    const int BowLoopStartFrame = 11;
    const int BowLoopEndFrame = 16;
    const int BowFirstFireFrame = 9;
    const int BowRepeatFireFrame = 15;

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
        if (heavyHoldThreshold <= 0f) heavyHoldThreshold = 0.22f;
        if (comboResetWindow <= 0f) comboResetWindow = 0.55f;
        if (castHoldThreshold <= 0f) castHoldThreshold = 0.5f;
        if (rapidShotInterval <= 0f) rapidShotInterval = 0.2f;
        if (comboChainDelay <= 0f) comboChainDelay = 0.03f;
        if (heavyLungeDistance <= 0f) heavyLungeDistance = 1.6f;
        if (heavyLungeDuration <= 0f) heavyLungeDuration = 0.18f;
        if (castMuzzleYOffset <= 0f) castMuzzleYOffset = 0.35f;
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
        bowLooping = false;
        pendingLightAttack = false;
        attackLocked = false;
        attackQueued = false;
        chainTimer = 0f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = false;
        if (playerMovement != null) playerMovement.CancelLunge();
        ClearMeleeFx();
        ResetHeavy();
        ResetCast();
        if (castFx != null) castFx.StopAll();
    }

    void Update()
    {
        if (!enableDemo || playerAnimation == null) return;

        HandleWeaponSwitch();

        if (weaponMode == WeaponMode.Cast)
        {
            UpdateCast();
            return;
        }

        if (weaponMode != WeaponMode.Sword) return;

        if (attackHeld)
        {
            attackHoldTime += Time.deltaTime;
            if (!heavyCharging && attackHoldTime >= heavyHoldThreshold)
            {
                StartHeavy();
            }
        }

        if (heavyCharging && !heavyLooping)
        {
            heavyWindupTimer += Time.deltaTime;
            if (heavyWindupTimer >= heavyWindupDuration)
            {
                heavyLooping = true;
                PlayHeavyLoop();
            }
        }

        if (chainTimer > 0f)
        {
            chainTimer -= Time.deltaTime;
            if (chainTimer <= 0f && attackQueued)
            {
                attackQueued = false;
                PlayerAnimationSet set = GetAnimationSet();
                if (set != null) PlaySwordCombo(set, true);
            }
        }
    }

    float lastWeaponScrollTime = -999f;
    [SerializeField] float weaponScrollCooldown = 0.15f;

    void HandleWeaponSwitch()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame) SetWeapon(WeaponMode.Sword);
        if (keyboard.digit2Key.wasPressedThisFrame) SetWeapon(WeaponMode.Bow);
        if (keyboard.digit3Key.wasPressedThisFrame) SetWeapon(WeaponMode.Cast);

        if (keyboard.eKey.wasPressedThisFrame) ToggleCastMode();
        if (keyboard.qKey.wasPressedThisFrame)
        {
            if (stateController != null) stateController.TryDash();
        }

        if (mouse != null && Time.unscaledTime - lastWeaponScrollTime >= weaponScrollCooldown)
        {
            Vector2 scroll = mouse.scroll.ReadValue();
            if (scroll.y > 0.01f)
            {
                CycleWeapon(1);
                lastWeaponScrollTime = Time.unscaledTime;
            }
            else if (scroll.y < -0.01f)
            {
                CycleWeapon(-1);
                lastWeaponScrollTime = Time.unscaledTime;
            }
        }

        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
        }
    }

    void CycleWeapon(int dir)
    {
        int cur = (int)weaponMode;
        int next = (cur + dir + 3) % 3;
        SetWeapon((WeaponMode)next);
    }

    void SetWeapon(WeaponMode mode)
    {
        if (weaponMode == mode) return;

        weaponMode = mode;
        attackHeld = false;
        bowActive = false;
        bowLooping = false;
        pendingLightAttack = false;
        attackLocked = false;
        attackQueued = false;
        chainTimer = 0f;
        comboStep = -1;
        lastAttackTime = -999f;
        ResetHeavy();
        ClearMeleeFx();
        ResetCast();
        if (castFx != null) castFx.StopAll();
        if (playerAnimation != null) playerAnimation.FreezeFacing = false;

        if (playerAnimation != null) playerAnimation.ClearDebugClip();
    }

    void OnAttackPressed(InputAction.CallbackContext context)
    {
        if (!enableDemo || playerAnimation == null) return;

        PlayerAnimationSet set = GetAnimationSet();
        if (set == null) return;

        if (weaponMode == WeaponMode.Bow)
        {
            if (!bowActive) StartBow(set);
            return;
        }

        if (weaponMode == WeaponMode.Cast)
        {
            StartCast(set);
            return;
        }

        if (weaponMode == WeaponMode.Sword)
        {
            if (playerMovement != null && !playerMovement.IsGrounded)
            {
                PlaySwordJumpAttack(set);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool wHeld = keyboard.wKey.isPressed;
                bool sHeld = keyboard.sKey.isPressed;
                if (wHeld && !sHeld)
                {
                    PlaySwordUpAttack(set);
                    return;
                }
                if (sHeld && !wHeld)
                {
                    PlaySwordDownAttack(set);
                    return;
                }
                if (sHeld && wHeld)
                {
                    PlaySwordDownAttack(set);
                    return;
                }
            }
        }

        attackHeld = true;
        attackHoldTime = 0f;
        heavyCharging = false;
        heavyLooping = false;
        heavyWindupTimer = 0f;
        pendingLightAttack = true;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
    }

    void OnAttackReleased(InputAction.CallbackContext context)
    {
        if (!enableDemo) return;

        if (weaponMode == WeaponMode.Bow)
        {
            StopBow();
            return;
        }

        if (weaponMode == WeaponMode.Cast)
        {
            ReleaseCast();
            return;
        }

        if (weaponMode != WeaponMode.Sword) return;

        attackHeld = false;

        if (heavyCharging)
        {
            pendingLightAttack = false;
            PlayHeavyRelease();
            heavyCharging = false;
            heavyLooping = false;
            heavyWindupTimer = 0f;
            comboStep = -1;
            lastAttackTime = -999f;
            return;
        }

        if (pendingLightAttack)
        {
            pendingLightAttack = false;
            PlayerAnimationSet set = GetAnimationSet();
            if (set != null) RequestSwordAttack(set);
        }
        else if (playerAnimation != null && !attackLocked)
        {
            playerAnimation.FreezeFacing = false;
        }
    }

    void RequestSwordAttack(PlayerAnimationSet set)
    {
        if (attackLocked)
        {
            attackQueued = true;
            return;
        }

        PlaySwordCombo(set, false);
    }

    void StartBow(PlayerAnimationSet set)
    {
        PlayerAnimationClip bow = set.bow;
        if (bow == null || bow.frames == null || bow.frames.Length < 11)
        {
            Debug.LogWarning("[CombatDemo] bow clip needs at least 11 frames");
            return;
        }

        ClearMeleeFx();
        bowActive = true;
        bowLooping = false;
        attackLocked = true;
        Sprite[] frames = new Sprite[11];
        Array.Copy(bow.frames, 0, frames, 0, 11);

        PlayerAnimationClip playback = new PlayerAnimationClip
        {
            id = "bowCombat",
            frames = frames,
            frameInterval = bow.frameInterval,
            loop = false
        };

        playerAnimation.PlayDebugClip(playback);
    }

    void PlayBowLoop(PlayerAnimationSet set)
    {
        if (set.bow == null || set.bow.frames == null || set.bow.frames.Length <= BowLoopEndFrame) return;

        bowLooping = true;
        PlayRange(set.bow, BowLoopStartFrame, BowLoopEndFrame, true, "bowLoop");
    }

    void StopBow()
    {
        bowActive = false;
        bowLooping = false;
        attackLocked = false;
        if (playerAnimation != null)
        {
            playerAnimation.FreezeFacing = false;
            playerAnimation.ClearDebugClip();
        }
    }

    void OnDebugClipFrame(int frame)
    {
        if (bowActive && weaponMode == WeaponMode.Bow)
        {
            if (!bowLooping && frame == BowFirstFireFrame) FireBow();
            if (bowLooping && frame == BowRepeatFireFrame - BowLoopStartFrame) FireBow();
        }

        if (castActive && weaponMode == WeaponMode.Cast && frame == 2 && !castGatherStarted && castFx != null)
        {
            castGatherStarted = true;
            castFx.PlayGather();
        }

        if (hasPendingCastShot && frame == 2)
        {
            hasPendingCastShot = false;
            FireCast(castPendingShot);
        }

        if (meleeFx == null || frame != meleeFirstFrame) return;

        meleeVfx.PlaySequence(
            meleeFx,
            meleeLastFrame - meleeFirstFrame + 1,
            0.05f,
            playerAnimation != null ? playerAnimation.BodyRenderer : null,
            meleeApplyVfxSpeed,
            meleeSortingOffset);

        if (!meleeHitDone && meleeHitFx != null)
        {
            meleeHitDone = true;
            meleeVfx.PlayHit(meleeHitFx, meleeHitRadius, playerAnimation != null ? playerAnimation.BodyRenderer : null);
        }
    }

    void OnDebugClipFinished(PlayerAnimationClip clip)
    {
        if (clip == null) return;

        switch (clip.id)
        {
            case "bowCombat":
                if (bowActive)
                {
                    PlayerAnimationSet set = GetAnimationSet();
                    if (set != null) PlayBowLoop(set);
                }
                return;

            case "heavyRelease":
                attackLocked = false;
                attackQueued = false;
                chainTimer = 0f;
                if (playerAnimation != null) playerAnimation.FreezeFacing = false;
                return;

            case "attack1":
            case "attack2":
            case "attack3":
            case "jumpAttack":
            case "upAttack":
            case "downAttack":
                attackLocked = false;
                lastAttackTime = Time.time;
                if (attackQueued)
                {
                    chainTimer = comboChainDelay;
                }
                else if (playerAnimation != null)
                {
                    playerAnimation.FreezeFacing = false;
                }
                return;

            case "castRelease":
                attackLocked = false;
                if (playerAnimation != null) playerAnimation.FreezeFacing = false;
                return;
        }
    }

    void FireBow()
    {
        PlayerAnimationSet set = GetAnimationSet();
        if (set == null || playerAnimation == null) return;

        Vector2 direction = playerAnimation.GetCurrentAimDirectionVector();
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

        Vector3 tip = PlayerBowShot.GetBowTip(
            playerAnimation.BodyRenderer,
            direction,
            playerAnimation.GetMuzzleWorldPosition());

        PlayerBowShot.Fire(set, tip, direction, gameObject, playerAnimation.BodyRenderer);
    }

    void StartCast(PlayerAnimationSet set)
    {
        PlayerAnimationClip cast = set.cast;
        if (cast == null || cast.frames == null || cast.frames.Length < 11)
        {
            Debug.LogWarning("[CombatDemo] cast clip needs at least 11 frames");
            return;
        }

        ClearMeleeFx();
        ResetCast();
        castActive = true;
        castHoldTime = 0f;
        attackLocked = true;
        if (castFx != null) castFx.StopAll();
        if (castFx != null) castFx.ShowAura();
        PlayRange(cast, 0, 2, true, "castWindup");
    }

    void UpdateCast()
    {
        if (!castActive) return;

        castHoldTime += Time.deltaTime;
        if (!castCharging && castHoldTime >= castHoldThreshold)
        {
            castCharging = true;
            PlayerAnimationSet set = GetAnimationSet();
            if (set != null && set.cast != null)
                PlayRange(set.cast, 3, 4, true, "castCharge");

            if (castMode == CastMode.Charge)
            {
                castBigCharging = true;
                if (castFx != null)
                {
                    castFx.PlayBigLoop();
                }
            }
            else
            {
                castRapidFiring = true;
                if (stateController != null) stateController.MovementLocked = true;
                FireCast(CastShotType.Rapid);
                castNextRapidTime = castHoldTime + rapidShotInterval;
            }
        }

        if (castRapidFiring && castHoldTime >= castNextRapidTime)
        {
            FireCast(CastShotType.Rapid);
            castNextRapidTime = castHoldTime + rapidShotInterval;
        }
    }

    void ReleaseCast()
    {
        if (!castActive) return;

        bool charged = castCharging;
        CastMode mode = castMode;
        castActive = false;
        castCharging = false;
        castBigCharging = false;
        castRapidFiring = false;
        if (stateController != null) stateController.MovementLocked = false;
        if (castFx != null) castFx.HideAura();
        if (castFx != null) castFx.StopAll();

        if (!charged)
        {
            hasPendingCastShot = true;
            castPendingShot = CastShotType.Basic;
        }
        else if (mode == CastMode.Charge)
        {
            hasPendingCastShot = true;
            castPendingShot = CastShotType.Big;
        }
        else
        {
            hasPendingCastShot = false;
        }

        PlayerAnimationSet set = GetAnimationSet();
        if (set != null && set.cast != null)
            PlayRange(set.cast, 5, 10, false, "castRelease");
        else
            UnlockAttack();
    }

    void FireCast(CastShotType type)
    {
        PlayerAnimationSet set = GetAnimationSet();
        if (set == null || playerAnimation == null) return;

        float facing = transform.localScale.x < 0f ? -1f : 1f;
        Vector2 direction = new Vector2(facing, 0f);
        Vector3 origin = playerAnimation.GetMuzzleWorldPosition();
        origin.y -= castMuzzleYOffset;

        PlayerCastShot.Fire(
            set,
            type,
            origin,
            direction,
            gameObject,
            playerAnimation.BodyRenderer);
    }

    void ToggleCastMode()
    {
        if (castActive) return;
        castMode = castMode == CastMode.Charge ? CastMode.Rapid : CastMode.Charge;
    }

    void ResetCast()
    {
        castActive = false;
        castHoldTime = 0f;
        castCharging = false;
        castBigCharging = false;
        castRapidFiring = false;
        if (stateController != null) stateController.MovementLocked = false;
        castNextRapidTime = 0f;
        hasPendingCastShot = false;
        castGatherStarted = false;
        if (castFx != null) castFx.HideAura();
    }

    void PlaySwordCombo(PlayerAnimationSet set, bool chained)
    {
        if (!chained && (Time.time - lastAttackTime > comboResetWindow || comboStep < 0))
        {
            comboStep = 0;
        }
        else
        {
            comboStep = (comboStep + 1) % 3;
        }

        lastAttackTime = Time.time;
        attackLocked = true;
        attackQueued = false;
        chainTimer = 0f;
        pendingLightAttack = false;
        bowActive = false;
        bowLooping = false;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;

        if (meleeVfx != null) meleeVfx.SetSlashRotation(0f);
        meleeFx = comboStep == 0 ? set.attack1Fx : comboStep == 1 ? set.attack2Fx : set.attack3Fx;
        meleeFirstFrame = comboStep == 0 ? 3 : comboStep == 1 ? 1 : 5;
        meleeLastFrame = meleeFirstFrame + 3;
        meleeApplyVfxSpeed = true;
        meleeHitFx = set.swordHitFx != null ? set.swordHitFx : set.castRelease;
        meleeHitRadius = 1.1f;
        meleeHitDone = false;
        if (meleeVfx != null) meleeVfx.BeginSwing();

        Vector2 slashOffset = comboStep == 0 ? new Vector2(1.2f, 1.0f)
            : comboStep == 1 ? new Vector2(1.4f, 1.0f)
            : new Vector2(1.6f, 1.0f);
        if (meleeVfx != null) meleeVfx.SetAnchorOffset(slashOffset);

        PlayerAnimationClip clip = comboStep == 0 ? set.attack1 : comboStep == 1 ? set.attack2 : set.attack3;
        PlayClip(clip, false, "attack" + (comboStep + 1));
    }

    void PlaySwordUpAttack(PlayerAnimationSet set)
    {
        ClearMeleeFx();
        attackHeld = false;
        ResetHeavy();
        bowActive = false;
        bowLooping = false;
        pendingLightAttack = false;
        attackLocked = true;
        attackQueued = false;
        chainTimer = 0f;
        comboStep = -1;
        lastAttackTime = -999f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;

        meleeFx = set.attack1Fx;
        meleeFirstFrame = 0;
        meleeLastFrame = 3;
        meleeApplyVfxSpeed = true;
        meleeHitFx = set.swordHitFx != null ? set.swordHitFx : set.castRelease;
        meleeHitRadius = 1.1f;
        meleeHitDone = false;
        if (meleeVfx != null)
        {
            meleeVfx.SetSlashRotation(90f);
            meleeVfx.SetAnchorOffset(new Vector2(0.35f, 2.6f));
            meleeVfx.BeginSwing();
        }

        PlayerAnimationClip clip = set.upAttack != null ? set.upAttack : set.attack1;
        PlayRange(clip, 3, 6, false, "upAttack");
    }

    void PlaySwordDownAttack(PlayerAnimationSet set)
    {
        ClearMeleeFx();
        attackHeld = false;
        ResetHeavy();
        bowActive = false;
        bowLooping = false;
        pendingLightAttack = false;
        attackLocked = true;
        attackQueued = false;
        chainTimer = 0f;
        comboStep = -1;
        lastAttackTime = -999f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;

        meleeFx = set.attack1Fx;
        meleeFirstFrame = 0;
        meleeLastFrame = 3;
        meleeApplyVfxSpeed = true;
        meleeHitFx = set.swordHitFx != null ? set.swordHitFx : set.castRelease;
        meleeHitRadius = 1.1f;
        meleeHitDone = false;
        if (meleeVfx != null)
        {
            meleeVfx.SetSlashRotation(-90f);
            meleeVfx.BeginSwing();
        }

        PlayerAnimationClip clip = set.downAttack != null ? set.downAttack : set.attack1;
        PlayRange(clip, 3, 6, false, "downAttack");
    }

    void PlaySwordJumpAttack(PlayerAnimationSet set)
    {
        ClearMeleeFx();
        attackHeld = false;
        ResetHeavy();
        bowActive = false;
        bowLooping = false;
        pendingLightAttack = false;
        attackLocked = true;
        attackQueued = false;
        chainTimer = 0f;
        comboStep = -1;
        lastAttackTime = -999f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;

        meleeFx = set.attack1Fx;
        meleeFirstFrame = 2;
        meleeLastFrame = 5;
        meleeApplyVfxSpeed = true;
        meleeHitFx = set.swordHitFx != null ? set.swordHitFx : set.castRelease;
        meleeHitRadius = 1.2f;
        meleeHitDone = false;
        if (meleeVfx != null)
        {
            meleeVfx.SetSlashRotation(0f);
            meleeVfx.SetAnchorOffset(new Vector2(1.2f, 0.4f));
            meleeVfx.BeginSwing();
        }

        PlayerAnimationClip clip = set.jumpAttack != null ? set.jumpAttack : set.attack1;
        PlayClip(clip, false, "jumpAttack");
    }

    void StartHeavy()
    {
        PlayerAnimationSet set = GetAnimationSet();
        if (set == null) return;

        PlayerAnimationClip heavy = set.heavyAttack;
        if (heavy == null || heavy.frames == null || heavy.frames.Length < 4)
        {
            Debug.LogWarning("[CombatDemo] heavyAttack has too few frames");
            return;
        }

        ClearMeleeFx();
        heavyCharging = true;
        heavyLooping = false;
        heavyWindupTimer = 0f;
        heavyWindupDuration = Mathf.Max(0.01f, 4f * heavy.frameInterval /
            (playerAnimation != null ? playerAnimation.PlaybackSpeed : 1f));
        pendingLightAttack = false;
        attackLocked = true;
        attackQueued = false;
        chainTimer = 0f;
        if (playerAnimation != null) playerAnimation.FreezeFacing = true;
        PlayRange(heavy, 0, 3, false, "heavyWindup");
    }

    void PlayHeavyLoop()
    {
        PlayerAnimationSet set = GetAnimationSet();
        if (set == null) return;

        PlayerAnimationClip heavy = set.heavyAttack;
        if (heavy == null || heavy.frames == null || heavy.frames.Length < 21)
        {
            Debug.LogWarning("[CombatDemo] heavyAttack has too few frames for loop");
            return;
        }

        ClearMeleeFx();
        PlayRange(heavy, 4, 20, true, "heavyLoop");
    }

    void PlayHeavyRelease()
    {
        PlayerAnimationSet set = GetAnimationSet();
        if (set == null) return;

        PlayerAnimationClip heavy = set.heavyAttack;
        if (heavy == null || heavy.frames == null || heavy.frames.Length < 27)
        {
            Debug.LogWarning("[CombatDemo] heavyAttack has too few frames for release");
            UnlockAttack();
            return;
        }

        meleeFx = set.heavyFx;
        meleeFirstFrame = 0;
        meleeLastFrame = 5;
        meleeApplyVfxSpeed = false;
        meleeSortingOffset = -1;
        meleeHitFx = set.heavyHitFx != null ? set.heavyHitFx : set.castBigRelease;
        meleeHitRadius = 1.6f;
        meleeHitDone = false;
        if (meleeVfx != null)
        {
            meleeVfx.SetSlashRotation(0f);
            meleeVfx.SetAnchorOffset(new Vector2(1.5f, 0.05f));
            meleeVfx.BeginSwing();
        }
        if (playerMovement != null)
        {
            float facing = transform.localScale.x < 0f ? -1f : 1f;
            playerMovement.ApplyLunge(new Vector2(facing, 0f), heavyLungeDistance, heavyLungeDuration);
        }
        PlayRange(heavy, 21, 26, false, "heavyRelease");
    }

    void ClearMeleeFx()
    {
        meleeFx = null;
        meleeHitFx = null;
        meleeFirstFrame = 0;
        meleeLastFrame = -1;
        meleeApplyVfxSpeed = true;
        meleeSortingOffset = 1;
        meleeHitRadius = 1.1f;
        meleeHitDone = false;
        if (meleeVfx != null)
        {
            meleeVfx.SetSlashRotation(0f);
            meleeVfx.SetAnchorOffset(Vector2.zero);
            meleeVfx.Hide();
            meleeVfx.BeginSwing();
        }
    }

    void UnlockAttack()
    {
        attackLocked = false;
        attackQueued = false;
        chainTimer = 0f;
        pendingLightAttack = false;
        if (playerAnimation != null) playerAnimation.FreezeFacing = false;
    }

    void ResetHeavy()
    {
        heavyCharging = false;
        heavyLooping = false;
        heavyWindupTimer = 0f;
        heavyWindupDuration = 0f;
    }

    PlayerAnimationSet GetAnimationSet()
    {
        if (playerAnimation == null) return null;

        PlayerAnimationSet set = playerAnimation.AnimationSet;
        if (set == null) Debug.LogWarning("[CombatDemo] PlayerAnimationSet is not assigned");
        return set;
    }

    void PlayClip(PlayerAnimationClip clip, bool loop, string label)
    {
        if (clip == null || clip.frames == null || clip.frames.Length == 0)
        {
            Debug.LogWarning("[CombatDemo] clip has no frames: " + label);
            UnlockAttack();
            return;
        }

        PlayerAnimationClip playback = new PlayerAnimationClip
        {
            id = label,
            frames = clip.frames,
            frameInterval = clip.frameInterval,
            loop = loop
        };

        playerAnimation.PlayDebugClip(playback);
        Debug.Log("[CombatDemo] " + label + " frames=" + playback.frames.Length +
                  " interval=" + playback.frameInterval + " loop=" + playback.loop);
    }

    void PlayRange(PlayerAnimationClip source, int start, int end, bool loop, string label)
    {
        int count = end - start + 1;
        if (count <= 0 || source.frames == null || start < 0 || end >= source.frames.Length)
        {
            Debug.LogWarning("[CombatDemo] invalid range " + start + ".." + end + " for " + label);
            UnlockAttack();
            return;
        }

        Sprite[] frames = new Sprite[count];
        Array.Copy(source.frames, start, frames, 0, count);

        PlayerAnimationClip playback = new PlayerAnimationClip
        {
            id = label,
            frames = frames,
            frameInterval = source.frameInterval,
            loop = loop
        };

        playerAnimation.PlayDebugClip(playback);
        Debug.Log("[CombatDemo] " + label + " frames=" + start + ".." + end +
                  " count=" + count + " loop=" + loop);
    }

    void OnGUI()
    {
        if (!enableDemo) return;

        string castState = !castActive ? "idle"
            : castBigCharging ? "big charging"
            : castRapidFiring ? "rapid firing"
            : castCharging ? "charging"
            : "windup";

        GUI.Box(new Rect(10f, 10f, 760f, 96f), string.Empty);
        GUI.Label(new Rect(20f, 16f, 740f, 84f),
            "Weapon: " + weaponMode + "   [1][2][3]/Scroll   [E] MagicMode: " + castMode + " (" + castState + ")   [Q] Dash   [RMB] Grapple\n" +
            "Sword: LMB combo/Hold heavy / W+LMB Up / S+LMB Down / Air+LMB Jump   Bow: Hold 0-4->5-10 fire 9 / repeat   Cast: hold>=0.5s gather, Rapid repeat, Charge big");
    }
}
