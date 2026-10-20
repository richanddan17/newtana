using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// 대화 트리거 - 2D Trigger 진입 또는 상호작용 키(Interact)로 대화 시작.
/// 계획서 room-camera-system.md [7] Dialogue 모드.
/// 재시작 규칙: playOnce=true면 한 번만, false면 트리거 재진입(또는 키 재입력) 시 반복.
/// 플레이어는 태그 "Player"로 찾는다 (RoomManager와 동일한 패턴).
/// </summary>
[DisallowMultipleComponent]
public class DialogueTrigger : MonoBehaviour
{
    [Header("Dialogue")]
    [SerializeField] DialogueData dialogue;

    [Header("Start Mode")]
    [Tooltip("체크: 트리거 진입 시 자동 시작 / 해제: 상호작용 키(E)로 시작")]
    [SerializeField] bool startOnEnter = true;

    [Header("Restart Rule")]
    [Tooltip("체크: 한 번만 재생 / 해제: 반복 (트리거 이탈 후 재진입 시 다시 시작)")]
    [SerializeField] bool playOnce = true;

    [Header("Debug")]
    [SerializeField] bool logTriggerEvents = false;

    bool hasPlayed;
    bool playerInRange;
    Transform playerInRangeTransform;
    PlayerInputHandler playerInputHandler;
    Action<InputAction.CallbackContext> interactHandler;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = true;
        playerInRangeTransform = other.transform;
        BindInteract(other.transform);

        if (startOnEnter)
        {
            TryStartDialogue();
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        playerInRangeTransform = null;
        UnbindInteract();

        // 반복 모드: 트리거 이탈으로 재시작 무장 (playOnce는 hasPlayed가 계속 막음)
    }

    void Update()
    {
        if (startOnEnter) return; // 자동 시작 모드는 키 불필요
        if (!playerInRange) return;
        if (playerInputHandler != null) return; // Input System 액션 구독 중 → 폴백 비활성

        var kb = Keyboard.current;
        if (kb != null && kb[Key.E].wasPressedThisFrame)
        {
            TryStartDialogue();
        }
    }

    void OnDestroy()
    {
        UnbindInteract();
    }

    void OnDisable()
    {
        UnbindInteract();
        playerInRange = false;
        playerInRangeTransform = null;
    }

    void TryStartDialogue()
    {
        if (dialogue == null)
        {
            Debug.LogWarning($"[DialogueTrigger] '{name}' has no DialogueData assigned.", this);
            return;
        }
        if (playOnce && hasPlayed) return;
        if (DialogueManager.Instance.IsActive) return;

        var target = playerInRangeTransform != null
            ? playerInRangeTransform
            : (GameObject.FindGameObjectWithTag("Player")?.transform);

        if (target == null)
        {
            Debug.LogWarning($"[DialogueTrigger] Player (tag 'Player') not found.", this);
            return;
        }

        // otherTarget: 트리거自身이 상대 (NPC/간판/오브젝트). 카메라 컷줄 기준점.
        if (DialogueManager.Instance.StartDialogue(dialogue, target, transform))
        {
            hasPlayed = true;
            if (logTriggerEvents)
                Debug.Log($"[DialogueTrigger] Dialogue started on '{name}': {dialogue.name}", this);
        }
    }

    /// <summary>상호작용 키 수신: PlayerInputHandler의 Interact 액션 사용 (없으면 Update 폴백)</summary>
    void BindInteract(Transform player)
    {
        UnbindInteract();

        playerInputHandler = player.GetComponent<PlayerInputHandler>();
        if (playerInputHandler == null) return;

        interactHandler = _ =>
        {
            // 대화 진행 중에는 이 트리거가 아닌 Manager가 진행을 담당
            if (!DialogueManager.Instance.IsActive)
            {
                TryStartDialogue();
            }
        };
        playerInputHandler.OnInteractPressed += interactHandler;
    }

    void UnbindInteract()
    {
        if (playerInputHandler != null && interactHandler != null)
        {
            playerInputHandler.OnInteractPressed -= interactHandler;
        }
        playerInputHandler = null;
        interactHandler = null;
    }
}
