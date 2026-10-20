using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// 대화 관리자 - 상태 머신(Idle/Active), 라인 순차 진행, 입력 차단, 카메라 컷줄 호출.
/// 계획서 room-camera-system.md [7] Dialogue 모드(컷 줌 + 입력 차단) 반영.
/// 씬에 배치가 없어도 Instance 접근 시 자동 생성되도록 Lazy Singleton (ProjectilePool 패턴 + FindAnyObjectByType 폴백).
/// </summary>
[DisallowMultipleComponent]
public class DialogueManager : MonoBehaviour
{
    public enum DialogueState
    {
        Idle,    // 대화 없음
        Active,  // 대화 진행중
    }

    [Header("Advance Input")]
    [Tooltip("라인 시작 직후 이 시간만큼은 진행 입력 무시 (시작 키가 첫 라인을 즉시 넘기는 것 방지)")]
    [SerializeField] float advanceInputCooldown = 0.2f;
    [Tooltip("PlayerInputHandler 미배치 환경용 Keyboard 폴백 키 (E/Space/Enter 고정, 액션 에셋의 Interact=E와 동일)")]
    [SerializeField] bool allowKeyboardFallback = true;

    [Header("Debug")]
    [SerializeField] bool logDialogueEvents = false;

    static DialogueManager instance;

    public static DialogueManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<DialogueManager>();
                if (instance == null)
                {
                    var go = new GameObject("[DialogueManager]");
                    instance = go.AddComponent<DialogueManager>();
                }
            }
            return instance;
        }
    }

    public DialogueState State { get; private set; } = DialogueState.Idle;
    public bool IsActive => State == DialogueState.Active;
    public DialogueData CurrentDialogue { get; private set; }
    public int CurrentLineIndex { get; private set; }

    public event Action OnDialogueStarted;
    public event Action OnDialogueEnded;

    DialogueData data;
    PlayerStateController playerState;
    PlayerInputHandler inputHandler;
    Action<InputAction.CallbackContext> advanceHandler;
    DialogueUI ui;
    float nextAdvanceAllowedTime;
    bool inputBlockedByDialogue; // SetInputBlocked 소유권 표시 (RespawnSystem 등과 우연히 겹쳐도 자기 차단만 해제)

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void OnDestroy()
    {
        // 플레이어가 대화중 씬을 떠나는 등 비정상 종료 → 입력 차단 해제 보장 (finally 역할)
        if (IsActive) ForceEndDialogue();

        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!IsActive) return;
        if (!allowKeyboardFallback) return;
        if (inputHandler != null) return; // Input System 액션 구독 중 → 폴백 비활성 (중복 진행 방지)

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[Key.E].wasPressedThisFrame || kb[Key.Space].wasPressedThisFrame || kb[Key.Enter].wasPressedThisFrame)
        {
            TryAdvance();
        }
    }

    /// <summary>대화 시작. 이미 진행중이거나 데이터가 비어있으면 false.</summary>
    /// <param name="playerTransform">플레이어 (입력 차단 + 카메라 타겟 A)</param>
    /// <param name="otherTarget">상대 (NPC/오브젝트, 카메라 타겟 B. null 가능)</param>
    public bool StartDialogue(DialogueData dialogueData, Transform playerTransform, Transform otherTarget)
    {
        if (dialogueData == null || dialogueData.LineCount == 0)
        {
            Debug.LogWarning("[DialogueManager] Empty dialogue data.");
            return false;
        }
        if (IsActive)
        {
            if (logDialogueEvents) Debug.Log("[DialogueManager] Already in dialogue, start ignored.");
            return false;
        }

        data = dialogueData;
        CurrentDialogue = dialogueData;
        CurrentLineIndex = 0;
        State = DialogueState.Active;
        nextAdvanceAllowedTime = Time.unscaledTime + advanceInputCooldown;

        // 카메라 컷줄 (계약: 병렬 구현. 널이면 카메라 없이 대화만 진행)
        CameraDirector.Instance?.EnterDialogue(playerTransform, otherTarget);

        // 입력 차단 (이동/점프/사격/앉기 전부 버퍼에서 무시됨 - RespawnSystem과 동일 경로)
        playerState = playerTransform != null
            ? playerTransform.GetComponent<PlayerStateController>()
            : FindAnyObjectByType<PlayerStateController>();
        if (playerState != null)
        {
            playerState.SetInputBlocked(true);
            inputBlockedByDialogue = true;
        }
        else
        {
            Debug.LogWarning("[DialogueManager] PlayerStateController not found - input not blocked.");
        }

        // 대화 진행 키: 기존 Interact 액션(PlayerInputHandler) 구독, 없으면 Update의 Keyboard 폴백
        inputHandler = playerTransform != null
            ? playerTransform.GetComponent<PlayerInputHandler>()
            : FindAnyObjectByType<PlayerInputHandler>();
        if (inputHandler != null)
        {
            advanceHandler = _ => TryAdvance();
            inputHandler.OnInteractPressed += advanceHandler;
        }

        ShowCurrentLine();

        if (logDialogueEvents) Debug.Log($"[DialogueManager] Dialogue started: {dialogueData.name} ({dialogueData.LineCount} lines)");
        OnDialogueStarted?.Invoke();
        return true;
    }

    /// <summary>다음 라인으로 진행. 마지막 라인 이후 호출 시 대화 종료.</summary>
    public void TryAdvance()
    {
        if (!IsActive) return;
        if (Time.unscaledTime < nextAdvanceAllowedTime) return;

        CurrentLineIndex++;
        if (data == null || CurrentLineIndex >= data.LineCount)
        {
            EndDialogue();
            return;
        }

        nextAdvanceAllowedTime = Time.unscaledTime + advanceInputCooldown;
        ShowCurrentLine();
    }

    /// <summary>외부에서 대화 강제 종료 (죽음 연출 등). 정상 경로 포함 모든 정리 수행.</summary>
    public void EndDialogue()
    {
        if (!IsActive) return;
        ForceEndDialogue();

        if (logDialogueEvents) Debug.Log("[DialogueManager] Dialogue ended.");
        OnDialogueEnded?.Invoke();
    }

    void ForceEndDialogue()
    {
        State = DialogueState.Idle;

        // 정리 순서 가역성: 어떤 항목이 예외를 던지든 나머지가 모두 수행되도록 각각 독립 try
        try { CameraDirector.Instance?.ExitDialogue(); }
        catch (Exception e) { Debug.LogException(e); }

        try
        {
            if (inputBlockedByDialogue && playerState != null)
            {
                playerState.SetInputBlocked(false); // 원래 조작으로 복귀 보장
            }
        }
        catch (Exception e) { Debug.LogException(e); }

        try
        {
            if (inputHandler != null && advanceHandler != null)
            {
                inputHandler.OnInteractPressed -= advanceHandler;
            }
        }
        catch (Exception e) { Debug.LogException(e); }

        try { ui?.Hide(); }
        catch (Exception e) { Debug.LogException(e); }

        inputBlockedByDialogue = false;
        inputHandler = null;
        advanceHandler = null;
        playerState = null;
        data = null;
        CurrentDialogue = null;
        CurrentLineIndex = 0;
    }

    void ShowCurrentLine()
    {
        var line = data.GetLine(CurrentLineIndex);
        if (line == null)
        {
            EndDialogue();
            return;
        }

        bool hasNext = CurrentLineIndex + 1 < data.LineCount;

        if (ui == null) ui = new DialogueUI();
        ui.Show(line.speakerName, line.text, hasNext);

        if (logDialogueEvents)
            Debug.Log($"[DialogueManager] Line {CurrentLineIndex + 1}/{data.LineCount}: [{line.speakerName}] {line.text}");
    }
}
