using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;

/// <summary>
/// 원클릭 셋업 (Newtana Custom Tool).
/// CinemachineBrain + Follow/Dialogue vcam + CameraDirector를 배치·와이어링하고,
/// 플레이어 오브젝트(태그/피직스/게임플레이 컴포넌트/교차 참조)를 보장한다.
/// 모든 단계가 idempotent — 재실행해도 기존 오브젝트를 재사용한다.
/// </summary>
public static class CameraSystemSetup
{
    const string UndoLabel = "카메라 시스템 셋업";
    const string DirectorObjectName = "CameraDirector";
    const string FollowVcamName = "CM Follow Camera";
    const string DialogueVcamName = "CM Dialogue Camera";

    public static void Setup()
    {
        ProjectTagsLayersSetup.EnsureAllQuiet();

        Camera cam = FindOrCreateCamera();
        CinemachineBrain brain = EnsureBrain(cam);

        EnsurePlayer(); // 플레이어를 먼저 보장 — 뒤의 WireDirector가 PlayerAnimation을 찾아 연결한다

        CameraDirector director = UnityEngine.Object.FindAnyObjectByType<CameraDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            var directorGo = new GameObject(DirectorObjectName);
            Undo.RegisterCreatedObjectUndo(directorGo, UndoLabel);
            director = Undo.AddComponent<CameraDirector>(directorGo);
        }

        CinemachineVirtualCamera followVcam = GetOrCreateVcam(director.transform, FollowVcamName);
        CinemachineVirtualCamera dialogueVcam = GetOrCreateVcam(director.transform, DialogueVcamName);

        ConfigureFollowVcam(followVcam, cam, director);
        ConfigureDialogueVcam(dialogueVcam, cam, director);
        WireDirector(director, brain, followVcam, dialogueVcam);

        Debug.Log("[카메라 시스템 셋업] 완료 — Brain, Follow/Dialogue vcam, CameraDirector 배치. 재실행 가능합니다.");
    }

    static Camera FindOrCreateCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            cam = UnityEngine.Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam != null) return cam;

        var go = new GameObject("Main Camera");
        Undo.RegisterCreatedObjectUndo(go, "Main Camera 생성");
        go.tag = "MainCamera";
        cam = Undo.AddComponent<Camera>(go);
        go.transform.position = new Vector3(0f, 0f, -10f); // 2D 투영 깊이
        Undo.AddComponent<AudioListener>(go);
        return cam;
    }

    static CinemachineBrain EnsureBrain(Camera cam)
    {
        var brain = cam.GetComponent<CinemachineBrain>();
        if (brain == null)
            brain = Undo.AddComponent<CinemachineBrain>(cam.gameObject);
        Undo.RecordObject(brain, UndoLabel);
        // 방 전환/대화 복귀의 기본 블렌드 (계획서 v5 [7]: 부드러운 복귀 옵션)
        brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut, 0.5f);
        return brain;
    }

    static CinemachineVirtualCamera GetOrCreateVcam(Transform parent, string vcamName)
    {
        CinemachineVirtualCamera vcam = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<CinemachineVirtualCamera>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.name == vcamName)
            {
                vcam = candidate;
                break;
            }
        }
        if (vcam != null) return vcam; // 기존 배치를 그대로 채택 (위치 무관)

        var go = new GameObject(vcamName);
        Undo.RegisterCreatedObjectUndo(go, vcamName + " 생성");
        go.transform.SetParent(parent, false);
        return Undo.AddComponent<CinemachineVirtualCamera>(go);
    }

    static void ConfigureFollowVcam(CinemachineVirtualCamera vcam, Camera cam, CameraDirector director)
    {
        var framing = vcam.GetCinemachineComponent<CinemachineFramingTransposer>();
        if (framing == null)
            framing = vcam.AddCinemachineComponent<CinemachineFramingTransposer>();

        Undo.RecordObject(framing, "Follow vcam 추적값");
        // 계획서 [3] 목표값: 가로 감쇠 0.5~0.8 / 세로 0.8~1.0, 데드존 가로 10%·세로 5%, Soft존 80%
        // Soft zone = 하드 리밋 (m_UnlimitedSoftZone=false 필수), Follow는 플레이어 직접 추적
        framing.m_XDamping = 0.6f;
        framing.m_YDamping = 0.9f;
        framing.m_DeadZoneWidth = 0.1f;
        framing.m_DeadZoneHeight = 0.05f;
        framing.m_SoftZoneWidth = 0.8f;
        framing.m_SoftZoneHeight = 0.8f;
        framing.m_TrackedObjectOffset = Vector3.zero;
        framing.m_BiasX = 0f;
        framing.m_BiasY = 0f;
        framing.m_LookaheadTime = 0f; // 조준 선행은 AimLeadExtension이 담당 — 이중 계산 방지
        framing.m_CameraDistance = 10f;
        var framingSO = new SerializedObject(framing);
        var unlimitedProp = framingSO.FindProperty("m_UnlimitedSoftZone");
        if (unlimitedProp != null) unlimitedProp.boolValue = false;
        framingSO.ApplyModifiedProperties();

        var confiner = vcam.GetComponent<CinemachineConfiner2D>();
        if (confiner == null)
            confiner = Undo.AddComponent<CinemachineConfiner2D>(vcam.gameObject);
        Undo.RecordObject(confiner, "Confiner2D 감쇠");
        confiner.m_Damping = ReadFloat(director, "boundaryDamping", 0.5f);

        EnsureImpulseListener(vcam);
        EnsureAimLeadExtension(vcam);
        ApplyLens(vcam, cam, 1f);
        vcam.Priority = ReadInt(director, "followPriority", 10);
    }

    static void ConfigureDialogueVcam(CinemachineVirtualCamera vcam, Camera cam, CameraDirector director)
    {
        if (vcam.GetCinemachineComponent<CinemachineFramingTransposer>() == null)
            vcam.AddCinemachineComponent<CinemachineFramingTransposer>();
        EnsureImpulseListener(vcam);
        ApplyLens(vcam, cam, ReadFloat(director, "dialogueZoomDivisor", 3.5f));
        vcam.Priority = ReadInt(director, "dialogueIdlePriority", 0);
    }

    static void EnsureImpulseListener(CinemachineVirtualCamera vcam)
    {
        var listener = vcam.GetComponent<CinemachineImpulseListener>();
        if (listener == null)
            listener = Undo.AddComponent<CinemachineImpulseListener>(vcam.gameObject);
        Undo.RecordObject(listener, "ImpulseListener 채널");
        // 채널 1 = CameraShake ImpulseSource 채널 (Reset 기본값과 동일, 재실행 시에도 유지)
        listener.m_ChannelMask = 1;
        listener.m_Gain = 1f;
    }

    static void EnsureAimLeadExtension(CinemachineVirtualCamera vcam)
    {
        var ext = vcam.GetComponent<AimLeadExtension>();
        if (ext == null)
            ext = Undo.AddComponent<AimLeadExtension>(vcam.gameObject);
        Undo.RecordObject(ext, "AimLeadExtension 설정");
        ext.m_LeadDistance = 2.5f;
        ext.m_LeadSmoothing = 0.18f;
        ext.m_SafeScreenFraction = 0.8f;
    }

    static void ApplyLens(CinemachineVirtualCamera vcam, Camera cam, float zoomDivisor)
    {
        Undo.RecordObject(vcam, "vcam 렌즈");
        var lens = vcam.m_Lens;
        lens.OrthographicSize = Mathf.Max(0.01f, cam.orthographicSize) / Mathf.Max(1f, zoomDivisor);
        lens.NearClipPlane = cam.nearClipPlane;
        lens.FarClipPlane = cam.farClipPlane;
        vcam.m_Lens = lens;
    }

    static void WireDirector(CameraDirector director, CinemachineBrain brain,
        CinemachineVirtualCamera followVcam, CinemachineVirtualCamera dialogueVcam)
    {
        var so = new SerializedObject(director);
        SetRef(so, "brain", brain);
        SetRef(so, "followVcam", followVcam);
        SetRef(so, "dialogueVcam", dialogueVcam);

        var roomManagerProp = so.FindProperty("roomManager");
        if (roomManagerProp != null && roomManagerProp.objectReferenceValue == null)
            roomManagerProp.objectReferenceValue =
                UnityEngine.Object.FindAnyObjectByType<RoomManager>(FindObjectsInactive.Include);

        var playerAnimProp = so.FindProperty("playerAnimation");
        if (playerAnimProp != null && playerAnimProp.objectReferenceValue == null)
            playerAnimProp.objectReferenceValue =
                UnityEngine.Object.FindAnyObjectByType<PlayerAnimation>(FindObjectsInactive.Include);

        so.ApplyModifiedProperties();
    }

    static void SetRef(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.objectReferenceValue = value;
    }

    static float ReadFloat(CameraDirector director, string propertyName, float fallback)
    {
        var prop = new SerializedObject(director).FindProperty(propertyName);
        return prop != null ? prop.floatValue : fallback;
    }

    static int ReadInt(CameraDirector director, string propertyName, int fallback)
    {
        var prop = new SerializedObject(director).FindProperty(propertyName);
        return prop != null ? prop.intValue : fallback;
    }

    // === 플레이어 셋업 ===
    const string PlayerTagName = "Player"; // CameraDirector/EnemyAI/RoomManager가 태그로 캐시하는 내장 태그
    const string GroundCheckPointName = "GroundCheckPoint";
    const string MuzzleName = "Muzzle";
    const string MuzzleFxName = "MuzzleFX";
    const string CastPointName = "CastPoint";
    const string BodyName = "Body";
    const string CastFxName = "CastFX";
    const string AuraFxName = "AuraFX";
    const string HurtboxName = "Hurtbox";
    const int PlayerLayerIndex = 6;            // Team.Player.LayerIndex() (Phase4 가이드 Layer 표)
    const int GroundLayerMaskValue = 1 << 3;   // Ground 레이어 (GroundCheck/PlayerMovement 기본값과 동일)
    const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    const string AnimationSetPath = "Assets/PlayerAnimationSet.asset"; // Newtana Custom Tool > Import Sheets From Manifest 가 생성

    /// <summary>
    /// 플레이어 오브젝트 보장 (idempotent): 태그 "Player" 또는 PlayerStateController로 기존 플레이어를 찾아
    /// (없으면 생성), 런타임 코드가 기대하는 컴포넌트/자식 구조와 [SerializeField] 교차 참조를 빠진 것만 채운다.
    /// 재실행해도 사용자가 수정한 값은 덮어쓰지 않는다.
    /// </summary>
    static void EnsurePlayer()
    {
        GameObject playerGo = FindExistingPlayer();
        bool created = false;
        if (playerGo == null)
        {
            playerGo = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(playerGo, UndoLabel);
            playerGo.transform.position = Vector3.zero; // 2D 원점 — 카메라 Follow 대상
            created = true;
        }

        // 태그 계약: CameraDirector.CachePlayer 등이 FindGameObjectWithTag("Player")로 캐시
        if (!playerGo.CompareTag(PlayerTagName))
        {
            Undo.RecordObject(playerGo, UndoLabel);
            playerGo.tag = PlayerTagName;
        }
        // 팀 레이어: TeamComponent.OnValidate와 같은 값 (LayerMask/충돌 판정 계약)
        if (playerGo.layer != PlayerLayerIndex)
        {
            Undo.RecordObject(playerGo, UndoLabel);
            playerGo.layer = PlayerLayerIndex;
        }

        // Rigidbody2D: PlayerMovement.Awake가 강제하는 값과 동일하게 미리 설정.
        // gravityScale·AddForce(넉백)를 쓰므로 Dynamic 필수 — Kinematic은 중력/힘이 무시된다.
        var rb = EnsureComponent<Rigidbody2D>(playerGo);
        ConfigureRigidbody(rb);

        // 이동용 Collider: sprite-asset-spec [7]의 고정 판정 크기 0.22×0.24 유닛(PPU 100).
        // 신규 추가일 때만 적용 — 기존 콜라이더 크기는 사용자 조작값으로 간주해 유지.
        var bodyCollider = playerGo.GetComponent<BoxCollider2D>();
        if (bodyCollider == null)
        {
            bodyCollider = Undo.AddComponent<BoxCollider2D>(playerGo);
            bodyCollider.size = new Vector2(0.22f, 0.24f);
            bodyCollider.offset = Vector2.zero;
        }

        // 루트 컴포넌트 (Phase4 가이드 [2] Player 구성)
        var team = EnsureComponent<TeamComponent>(playerGo);
        var inputHandler = EnsureComponent<PlayerInputHandler>(playerGo);
        var stateController = EnsureComponent<PlayerStateController>(playerGo);
        var movement = EnsureComponent<PlayerMovement>(playerGo);
        var anim = EnsureComponent<PlayerAnimation>(playerGo);
        var weapon = EnsureComponent<WeaponController>(playerGo);
        var health = EnsureComponent<Health>(playerGo);

        // 입력 애셋 / 투사체 풀 (에셋·씬 오브젝트는 스크립트가 만들지 않음 — 있으면 연결만)
        var inputActionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        var projectilePool = UnityEngine.Object.FindAnyObjectByType<ProjectilePool>(FindObjectsInactive.Include);
        var animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);

        // --- 자식 구조 ---
        var animSo = new SerializedObject(anim);

        // v5: 몸통은 Body 자식의 SpriteRenderer 가 담당한다. 기존 루트 렌더러가 있으면 자식으로 이전.
        SpriteRenderer bodyRenderer = EnsureBodyRenderer(playerGo);
        SpriteRenderer castFxRenderer = EnsureRendererChild(playerGo.transform, CastFxName, 5);
        SpriteRenderer auraFxRenderer = EnsureRendererChild(playerGo.transform, AuraFxName, -1);

        // Muzzle: 미연결이면 생성 (PlayerAnimation이 muzzleOffset 으로 localPosition을 주기적으로 갱신)
        var muzzleProp = animSo.FindProperty("muzzleTransform");
        Transform muzzle = muzzleProp != null ? muzzleProp.objectReferenceValue as Transform : null;
        if (muzzle == null)
            muzzle = GetOrCreateChild(playerGo.transform, MuzzleName, new Vector3(0.08f, 0.04f, 0f)); // 정면 조준 총구 실측값(스펙 [4])

        GetOrCreateChild(muzzle, CastPointName, Vector3.zero);

        // MuzzleFX: Playback이 이름으로 탐색하는 SpriteRenderer — 없으면 연출만 생략되므로 미리 생성
        var fxProp = animSo.FindProperty("muzzleFxRenderer");
        SpriteRenderer muzzleFxRenderer = fxProp != null ? fxProp.objectReferenceValue as SpriteRenderer : null;
        Transform muzzleFx = muzzleFxRenderer != null ? muzzleFxRenderer.transform : null;
        if (muzzleFx == null) muzzleFx = muzzle.Find(MuzzleFxName);
        if (muzzleFx == null) muzzleFx = playerGo.transform.Find(MuzzleFxName);
        if (muzzleFx == null) muzzleFx = GetOrCreateChild(muzzle, MuzzleFxName, Vector3.zero);
        if (muzzleFxRenderer == null)
        {
            muzzleFxRenderer = muzzleFx.GetComponent<SpriteRenderer>();
            if (muzzleFxRenderer == null)
            {
                muzzleFxRenderer = Undo.AddComponent<SpriteRenderer>(muzzleFx.gameObject);
                muzzleFxRenderer.sortingOrder = 10; // Body(0)보다 위에 총구 플래시
            }
        }

        // GroundCheck: 기존 배치(루트/자식 어디든)를 채택하고, 없으면 발끝 자식을 생성.
        // 위치는 콜라이더 하단에서 BoxCast가 시작되도록 boxSize의 절반 위 —
        // 시작 위치가 지형과 겹치면 BoxCast가 무시하므로 발바닥에 정확히 붙으면 착지 판정이 누락된다.
        GroundCheck groundCheck = playerGo.GetComponent<GroundCheck>();
        if (groundCheck == null) groundCheck = playerGo.GetComponentInChildren<GroundCheck>(true);
        Transform groundCheckPoint;
        if (groundCheck != null)
        {
            groundCheckPoint = groundCheck.transform; // 기존 배치 채택
        }
        else
        {
            groundCheckPoint = playerGo.transform.Find(GroundCheckPointName);
            bool createdPoint = groundCheckPoint == null;
            if (createdPoint)
                groundCheckPoint = GetOrCreateChild(playerGo.transform, GroundCheckPointName, Vector3.zero);
            groundCheck = EnsureComponent<GroundCheck>(groundCheckPoint.gameObject);

            if (createdPoint)
            {
                var groundSo = new SerializedObject(groundCheck);
                var boxSizeProp = groundSo.FindProperty("boxSize");
                Vector2 boxSize = boxSizeProp != null ? boxSizeProp.vector2Value : new Vector2(0.6f, 0.1f);
                groundCheckPoint.localPosition = new Vector3(
                    bodyCollider.offset.x,
                    bodyCollider.offset.y - bodyCollider.size.y * 0.5f + boxSize.y * 0.5f, // 콜라이더 하단 + box 절반
                    0f);
            }
        }

        // Hurtbox: 이동용 Collider와 분리 (Hurtbox.cs — Trigger 전용, Awake가 같은 오브젝트 Collider를 강제 Trigger화).
        Hurtbox hurtbox = playerGo.GetComponent<Hurtbox>();
        if (hurtbox == null)
        {
            Transform hurtboxT = GetOrCreateChild(playerGo.transform, HurtboxName, Vector3.zero);
            if (hurtboxT.gameObject.layer != PlayerLayerIndex)
            {
                Undo.RecordObject(hurtboxT.gameObject, UndoLabel);
                hurtboxT.gameObject.layer = PlayerLayerIndex; // Hurtbox.Awake가 런타임에 맞추는 레이어를 미리 동기화
            }
            var hurtboxCollider = hurtboxT.GetComponent<CircleCollider2D>();
            if (hurtboxCollider == null)
            {
                hurtboxCollider = Undo.AddComponent<CircleCollider2D>(hurtboxT.gameObject);
                hurtboxCollider.radius = bodyCollider.size.x * 0.5f; // 이동 콜라이더 폭과 동일 (스펙 [7])
                hurtboxCollider.isTrigger = true; // Hurtbox.Awake/OnValidate가 강제하는 값
            }
            hurtbox = EnsureComponent<Hurtbox>(hurtboxT.gameObject);
        }

        // --- 교차 참조 와이어링 (필드가 비어 있을 때만) ---
        var inputSo = new SerializedObject(inputHandler);
        SetRefIfNull(inputSo, "inputActionsAsset", inputActionsAsset);
        inputSo.ApplyModifiedProperties();

        var stateSo = new SerializedObject(stateController);
        SetRefIfNull(stateSo, "inputHandler", inputHandler);    // Awake 폴백 없음 — 필수
        SetRefIfNull(stateSo, "rb", rb);
        SetRefIfNull(stateSo, "health", health);
        SetRefIfNull(stateSo, "groundCheckPoint", groundCheckPoint); // 없으면 IsGrounded 상실 — 필수
        SetMaskIfEmpty(stateSo, "groundLayerMask", GroundLayerMaskValue); // 기본 0이면 지상 판정 불가 — 필수
        stateSo.ApplyModifiedProperties();

        var moveSo = new SerializedObject(movement);
        SetRefIfNull(moveSo, "stateController", stateController);
        SetRefIfNull(moveSo, "groundCheck", groundCheck); // Awake는 루트에서만 찾음 — 와이어 필수
        SetRefIfNull(moveSo, "rb", rb);
        moveSo.ApplyModifiedProperties();

        SetRefIfNull(animSo, "stateController", stateController);
        SetRefIfNull(animSo, "movement", movement);
        SetRefIfNull(animSo, "spriteRenderer", bodyRenderer);
        SetRefIfNull(animSo, "castFxRenderer", castFxRenderer);
        SetRefIfNull(animSo, "auraFxRenderer", auraFxRenderer);
        SetRefIfNull(animSo, "animationSet", animationSet);
        SetRefIfNull(animSo, "weapon", weapon);
        SetRefIfNull(animSo, "muzzleTransform", muzzle);
        SetRefIfNull(animSo, "muzzleFxRenderer", muzzleFxRenderer);
        animSo.ApplyModifiedProperties();

        var castFx = EnsureComponent<PlayerCastFx>(playerGo);
        var castFxSo = new SerializedObject(castFx);
        SetRefIfNull(castFxSo, "animationSet", animationSet);
        SetRefIfNull(castFxSo, "castFxRenderer", castFxRenderer);
        SetRefIfNull(castFxSo, "auraFxRenderer", auraFxRenderer);
        SetRefIfNull(castFxSo, "castPoint", muzzle.Find(CastPointName));
        castFxSo.ApplyModifiedProperties();

        var weaponSo = new SerializedObject(weapon);
        SetRefIfNull(weaponSo, "playerAnimation", anim);
        SetRefIfNull(weaponSo, "stateController", stateController);
        SetRefIfNull(weaponSo, "projectilePool", projectilePool); // null이면 건너뜀(런타임 Find 폴백)
        weaponSo.ApplyModifiedProperties();

        var healthSo = new SerializedObject(health);
        SetRefIfNull(healthSo, "teamComponent", team);
        SetRefIfNull(healthSo, "spriteRenderer", bodyRenderer);
        SetRefIfNull(healthSo, "movement", movement);
        SetRefIfNull(healthSo, "stateController", stateController);
        healthSo.ApplyModifiedProperties();

        var hurtboxSo = new SerializedObject(hurtbox);
        SetRefIfNull(hurtboxSo, "damageable", health); // 인터페이스 필드는 직렬화 안 될 수 있음 → Awake의 부모탐색 폴백 존재
        SetRefIfNull(hurtboxSo, "teamComponent", team);
        hurtboxSo.ApplyModifiedProperties();

        var teamSo = new SerializedObject(team);
        var teamProp = teamSo.FindProperty("team");
        if (teamProp != null && teamProp.intValue == (int)Team.None)
            teamProp.intValue = (int)Team.Player; // OnValidate가 레이어 6으로 동기화
        teamSo.ApplyModifiedProperties();

        var gcMaskSo = new SerializedObject(groundCheck);
        SetMaskIfEmpty(gcMaskSo, "groundLayerMask", GroundLayerMaskValue); // 기본 1<<3 — 이미 채워져 있으면 유지
        gcMaskSo.ApplyModifiedProperties();

        // v5: 모든 프레임은 PlayerAnimationSet 에셋이 담는다 (구 시트 로더는 v5에서 제거됨).
        if (animationSet == null)
        {
            Debug.LogWarning("[플레이어 셋업] PlayerAnimationSet 에셋이 없습니다 — " +
                            "'Newtana Custom Tool > Import Sheets From Manifest'를 먼저 실행하세요.");
        }
        else if (bodyRenderer.sprite == null && animationSet.idle != null && animationSet.idle.frames != null && animationSet.idle.frames.Length > 0)
        {
            Undo.RecordObject(bodyRenderer, UndoLabel);
            bodyRenderer.sprite = animationSet.idle.frames[0];
        }

        // --- 수동 작업 경고 (에셋/추가 씬 오브젝트는 셋업 스크립트가 생성하지 않음) ---
        if (inputActionsAsset == null)
            Debug.LogWarning($"[플레이어 셋업] 입력 애셋 로드 실패({InputActionsPath}) — PlayerInputHandler의 Input Actions Asset 필드에 수동 할당하세요.");

        if (weapon.CurrentWeapon == null)
            Debug.LogWarning("[플레이어 셋업] WeaponData 미지정 — 무기 발사 불가. " +
                "메뉴 Create > RunGun > Weapon Data로 무기 에셋을 만들고(투사체 프리팹 포함, " +
                "Phase4_TestSetupGuide.md §4~§5) WeaponController의 Current Weapon Data·Available Weapons에 등록하세요.");

        if (projectilePool == null)
            Debug.LogWarning("[플레이어 셋업] 씬에 ProjectilePool 없음 — WeaponController의 Projectile Pool 미연결. " +
                "빈 오브젝트를 만들고 ProjectilePool 컴포넌트를 붙여 재실행하세요. (Phase4_TestSetupGuide.md §6)");

        Debug.Log($"[플레이어 셋업] 완료 — {(created ? "Player 생성" : "기존 Player 보완")}: 태그/레이어, Rigidbody2D, 이동·Hurtbox 콜라이더, " +
            "GroundCheckPoint·Muzzle·MuzzleFX·Hurtbox 자식, 게임플레이 컴포넌트 와이어링. 재실행 가능합니다.");
    }

    static GameObject FindExistingPlayer()
    {
        // 태그 "Player" 우선 (FindGameObjectWithTag는 활성만 찾으므로 수동 순회로 비활성 포함)
        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.CompareTag(PlayerTagName)) return go;
        }

        // 태그가 없어도 PlayerStateController가 붙어 있으면 플레이어로 간주
        var stateController = UnityEngine.Object.FindAnyObjectByType<PlayerStateController>(FindObjectsInactive.Include);
        return stateController != null ? stateController.gameObject : null;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        if (component == null)
            component = Undo.AddComponent<T>(go);
        return component;
    }

    static Transform GetOrCreateChild(Transform parent, string childName, Vector3 localPosition)
    {
        var existing = parent.Find(childName);
        if (existing != null) return existing; // 기존 배치 채택 (위치 무관)

        var go = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(go, childName + " 생성");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    static void ConfigureRigidbody(Rigidbody2D rb)
    {
        Undo.RecordObject(rb, UndoLabel);
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.freezeRotation = true;
        rb.gravityScale = 1f; // PlayerMovement.Awake와 동일한 값
    }

    static void SetRefIfNull(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop != null && value != null && prop.objectReferenceValue == null)
            prop.objectReferenceValue = value;
    }

    static void SetMaskIfEmpty(SerializedObject so, string propertyName, int mask)
    {
        var prop = so.FindProperty(propertyName);
        if (prop != null && prop.intValue == 0)
            prop.intValue = mask;
    }

    /// <summary>v5: 몸통 렌더러는 Body 자식에 둔다. 기존 루트 렌더러가 있으면 스프라이트를 옮기고 컴포넌트를 제거한다.</summary>
    static SpriteRenderer EnsureBodyRenderer(GameObject playerGo)
    {
        var bodyTransform = GetOrCreateChild(playerGo.transform, BodyName, Vector3.zero);
        var bodyRenderer = bodyTransform.GetComponent<SpriteRenderer>();
        if (bodyRenderer == null) bodyRenderer = Undo.AddComponent<SpriteRenderer>(bodyTransform.gameObject);
        if (bodyRenderer.sortingOrder != 0)
        {
            Undo.RecordObject(bodyRenderer, UndoLabel);
            bodyRenderer.sortingOrder = 0;
        }

        var rootRenderer = playerGo.GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
        {
            if (bodyRenderer.sprite == null && rootRenderer.sprite != null) bodyRenderer.sprite = rootRenderer.sprite;
            Undo.DestroyObjectImmediate(rootRenderer);
        }
        return bodyRenderer;
    }

    static SpriteRenderer EnsureRendererChild(Transform parent, string childName, int sortingOrder)
    {
        var child = GetOrCreateChild(parent, childName, Vector3.zero);
        var renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(child.gameObject);
        if (renderer.sortingOrder != sortingOrder)
        {
            Undo.RecordObject(renderer, UndoLabel);
            renderer.sortingOrder = sortingOrder;
        }
        return renderer;
    }
}
