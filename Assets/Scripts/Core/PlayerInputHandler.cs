using UnityEngine;
using System;
using UnityEngine.InputSystem;

/// <summary>
/// Unity Input System → PlayerInputBuffer 연결 어댑터.
/// 계획서 [5]: 입력 장치/키 설정을 코드에 하드코딩하지 않음.
/// InputSystem_Actions.asset의 Player 맵 사용.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Input Asset (Project에 있는 InputSystem_Actions.asset)")]
    [SerializeField] InputActionAsset inputActionsAsset;

    // 액션 참조
    InputAction moveAction;
    InputAction lookAction;      // 조준 (마우스/우측 스틱)
    InputAction attackAction;    // 사격
    InputAction jumpAction;
    InputAction dashAction;      // Sprint 키로 매핑 (Shift/LeftStickPress)
    InputAction interactAction;  // 재장전/상호작용
    InputAction crouchAction;

    // 콜백 델리게이트 (OnDestroy에서 정확히 해제하기 위해 필드로 보관)
    Action<InputAction.CallbackContext> onMovePerformed;
    Action<InputAction.CallbackContext> onMoveCanceled;
    Action<InputAction.CallbackContext> onLookPerformed;
    Action<InputAction.CallbackContext> onLookCanceled;
    Action<InputAction.CallbackContext> onJumpStarted;
    Action<InputAction.CallbackContext> onJumpCanceled;
    Action<InputAction.CallbackContext> onDashStarted;
    Action<InputAction.CallbackContext> onShootStarted;
    Action<InputAction.CallbackContext> onShootCanceled;
    Action<InputAction.CallbackContext> onInteractStarted;
    Action<InputAction.CallbackContext> onCrouchStarted;
    Action<InputAction.CallbackContext> onCrouchCanceled;

    // 이벤트 (버퍼가 구독)
    public event Action<Vector2> OnMoveInput;
    public event Action<Vector2> OnAimInput;
    public event Action<InputAction.CallbackContext> OnJumpPressed;
    public event Action<InputAction.CallbackContext> OnJumpReleased;
    public event Action<InputAction.CallbackContext> OnDashPressed;
    public event Action<InputAction.CallbackContext> OnShootPressed;
    public event Action<InputAction.CallbackContext> OnShootReleased;
    public event Action<InputAction.CallbackContext> OnReloadPressed;
    public event Action<InputAction.CallbackContext> OnInteractPressed;
    public event Action<bool> OnCrouchPressed; // true=눌림(hold 시작), false=뗌

    /// <summary>현재 원시 조준 입력 (스냅 전)</summary>
    public Vector2 AimInput { get; private set; }

    void Awake()
    {
        if (inputActionsAsset == null)
        {
            Debug.LogError("[PlayerInputHandler] InputActionAsset not assigned!");
            return;
        }

        // Player 맵 찾기
        var playerMap = inputActionsAsset.FindActionMap("Player");
        if (playerMap == null)
        {
            Debug.LogError("[PlayerInputHandler] 'Player' action map not found in asset!");
            return;
        }

        // 액션 바인딩
        moveAction = playerMap.FindAction("Move");
        lookAction = playerMap.FindAction("Look");
        attackAction = playerMap.FindAction("Attack");
        jumpAction = playerMap.FindAction("Jump");
        dashAction = playerMap.FindAction("Sprint"); // 대시 = Sprint 키
        interactAction = playerMap.FindAction("Interact");
        crouchAction = playerMap.FindAction("Crouch");

        // 콜백 델리게이트 생성 (필드 보관 → OnDestroy에서 해제 가능)
        onMovePerformed = ctx => OnMoveInput?.Invoke(ctx.ReadValue<Vector2>());
        onMoveCanceled = ctx => OnMoveInput?.Invoke(Vector2.zero);
        onLookPerformed = ctx =>
        {
            AimInput = ctx.ReadValue<Vector2>();
            OnAimInput?.Invoke(AimInput);
        };
        onLookCanceled = ctx =>
        {
            AimInput = Vector2.zero;
            OnAimInput?.Invoke(Vector2.zero);
        };
        onJumpStarted = ctx => OnJumpPressed?.Invoke(ctx);
        onJumpCanceled = ctx => OnJumpReleased?.Invoke(ctx);
        onDashStarted = ctx => OnDashPressed?.Invoke(ctx);
        onShootStarted = ctx => OnShootPressed?.Invoke(ctx);
        onShootCanceled = ctx => OnShootReleased?.Invoke(ctx);
        onInteractStarted = ctx =>
        {
            OnReloadPressed?.Invoke(ctx);
            OnInteractPressed?.Invoke(ctx);
        };
        onCrouchStarted = ctx => OnCrouchPressed?.Invoke(true);
        onCrouchCanceled = ctx => OnCrouchPressed?.Invoke(false);

        // 콜백 등록
        if (moveAction != null) { moveAction.performed += onMovePerformed; moveAction.canceled += onMoveCanceled; }
        if (lookAction != null) { lookAction.performed += onLookPerformed; lookAction.canceled += onLookCanceled; }
        if (jumpAction != null) { jumpAction.started += onJumpStarted; jumpAction.canceled += onJumpCanceled; }
        if (dashAction != null) { dashAction.started += onDashStarted; }
        if (attackAction != null) { attackAction.started += onShootStarted; attackAction.canceled += onShootCanceled; }
        if (interactAction != null) { interactAction.started += onInteractStarted; }
        if (crouchAction != null) { crouchAction.started += onCrouchStarted; crouchAction.canceled += onCrouchCanceled; }

        // 액션 맵 활성화
        playerMap.Enable();
    }

    void OnDestroy()
    {
        if (moveAction != null) { moveAction.performed -= onMovePerformed; moveAction.canceled -= onMoveCanceled; }
        if (lookAction != null) { lookAction.performed -= onLookPerformed; lookAction.canceled -= onLookCanceled; }
        if (jumpAction != null) { jumpAction.started -= onJumpStarted; jumpAction.canceled -= onJumpCanceled; }
        if (dashAction != null) { dashAction.started -= onDashStarted; }
        if (attackAction != null) { attackAction.started -= onShootStarted; attackAction.canceled -= onShootCanceled; }
        if (interactAction != null) { interactAction.started -= onInteractStarted; }
        if (crouchAction != null) { crouchAction.started -= onCrouchStarted; crouchAction.canceled -= onCrouchCanceled; }
    }

    void OnEnable()
    {
        inputActionsAsset?.FindActionMap("Player")?.Enable();
    }

    void OnDisable()
    {
        inputActionsAsset?.FindActionMap("Player")?.Disable();
    }

    /// <summary>조준 입력 5방향 스냅 (정면/위45/위90/아래45/아래90) - 정면 ±22.5°, 대각 구간 45° 간격</summary>
    /// <param name="raw">원시 조준 입력</param>
    /// <param name="isGrounded">지상인지 (아래45/90은 지상에서 정면 계열로 스냅)</param>
    /// <returns>스냅된 방향 벡터 (좌우 부호는 raw.x 기준, 좌우 반전은 루트 scale.x가 담당)</returns>
    public static Vector2 SnapTo5Directions(Vector2 raw, bool isGrounded)
    {
        if (raw.sqrMagnitude < 0.01f) return Forward(raw);

        float angle = Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;

        // 대각 스냅 벡터 (√2/2) - 좌우 부호는 raw.x 기준 (Forward와 동일한 패턴)
        Vector2 up45 = new Vector2(1f, 1f).normalized;    // 우측 기준 위45
        Vector2 down45 = new Vector2(1f, -1f).normalized; // 우측 기준 아래45
        if (raw.x < 0f) { up45.x = -up45.x; down45.x = -down45.x; }

        // 정면: ±22.5 (좌측 대칭 157.5~202.5)
        if (angle >= 337.5f || angle < 22.5f) return Forward(raw);
        if (angle >= 157.5f && angle < 202.5f) return Forward(raw);
        // 위45: 22.5~67.5 (좌측 대칭 112.5~157.5)
        if (angle < 67.5f) return up45;
        if (angle >= 112.5f && angle < 157.5f) return up45;
        // 위90: 67.5~112.5
        if (angle < 112.5f) return Vector2.up;

        // 아래 구간(202.5~337.5): 아래는 공중에서만 허용, 지상이면 정면 계열로 스냅
        if (isGrounded) return Forward(raw);
        // 아래45: 202.5~247.5 (우측 대칭 292.5~337.5)
        if (angle < 247.5f) return down45;
        if (angle >= 292.5f) return down45;
        // 아래90: 247.5~292.5
        return Vector2.down;
    }

    static Vector2 Forward(Vector2 raw) => raw.x < 0f ? Vector2.left : Vector2.right;

    /// <summary>스냅 인덱스를 월드 방향 벡터로 변환 (0=정면, 1=위45, 2=위90, 3=아래45, 4=아래90)</summary>
    public static Vector3 AimIndexToVector(int index)
    {
        const float kDiagonal = 0.7071068f; // √2/2
        return index switch
        {
            PlayerStateController.AimDirUp45 => new Vector3(kDiagonal, kDiagonal, 0f),    // 위45
            PlayerStateController.AimDirUp90 => Vector3.up,                               // 위90
            PlayerStateController.AimDirDown45 => new Vector3(kDiagonal, -kDiagonal, 0f), // 아래45
            PlayerStateController.AimDirDown90 => Vector3.down,                           // 아래90
            _ => Vector3.right,                                                           // 정면
        };
    }

    /// <summary>스냅 인덱스를 월드 방향 벡터로 변환 (하위 호환 오버로드, facingRight > 0 = 오른쪽)</summary>
    public static Vector2 AimIndexToVector(int aimIndex, int facingRight)
    {
        const float kDiagonal = 0.7071068f; // √2/2
        float facing = facingRight > 0 ? 1f : -1f;
        return aimIndex switch
        {
            PlayerStateController.AimDirUp45 => new Vector2(kDiagonal * facing, kDiagonal),    // 위45
            PlayerStateController.AimDirUp90 => Vector2.up,                                   // 위90
            PlayerStateController.AimDirDown45 => new Vector2(kDiagonal * facing, -kDiagonal), // 아래45
            PlayerStateController.AimDirDown90 => Vector2.down,                               // 아래90
            _ => facingRight > 0 ? Vector2.right : Vector2.left                               // 정면
        };
    }

    /// <summary>마우스 월드 위치로 조준 방향 계산 (카메라 필요)</summary>
    public static Vector2 GetMouseAimDirection(Camera cam, Transform player)
    {
        if (cam == null || player == null) return player ? player.right : Vector2.right;
        Vector3 mouseWorld = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorld.z = 0f;
        return (mouseWorld - player.position).normalized;
    }
}
