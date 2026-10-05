using UnityEngine;
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
    InputAction crouchAction;    // 미사용 (향후 확장)

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

        // 콜백 등록
        if (moveAction != null) moveAction.performed += ctx => OnMoveInput?.Invoke(ctx.ReadValue<Vector2>());
        if (moveAction != null) moveAction.canceled += ctx => OnMoveInput?.Invoke(Vector2.zero);

        if (lookAction != null) lookAction.performed += ctx => OnAimInput?.Invoke(ctx.ReadValue<Vector2>());
        if (lookAction != null) lookAction.canceled += ctx => OnAimInput?.Invoke(Vector2.zero);

        if (jumpAction != null)
        {
            jumpAction.started += ctx => OnJumpPressed?.Invoke(ctx);
            jumpAction.canceled += ctx => OnJumpReleased?.Invoke(ctx);
        }

        if (dashAction != null)
        {
            dashAction.started += ctx => OnDashPressed?.Invoke(ctx);
        }

        if (attackAction != null)
        {
            attackAction.started += ctx => OnShootPressed?.Invoke(ctx);
            attackAction.canceled += ctx => OnShootReleased?.Invoke(ctx);
        }

        if (interactAction != null)
        {
            interactAction.started += ctx => 
            {
                OnReloadPressed?.Invoke(ctx);
                OnInteractPressed?.Invoke(ctx);
            };
        }

        // 액션 맵 활성화
        playerMap.Enable();
    }

    void OnDestroy()
    {
        // 정리
        if (moveAction != null) { moveAction.performed -= _ => { }; moveAction.canceled -= _ => { }; }
        if (lookAction != null) { lookAction.performed -= _ => { }; lookAction.canceled -= _ => { }; }
        if (jumpAction != null) { jumpAction.started -= _ => { }; jumpAction.canceled -= _ => { }; }
        if (dashAction != null) dashAction.started -= _ => { };
        if (attackAction != null) { attackAction.started -= _ => { }; attackAction.canceled -= _ => { }; }
        if (interactAction != null) interactAction.started -= _ => { };
    }

    void OnEnable()
    {
        inputActionsAsset?.FindActionMap("Player")?.Enable();
    }

    void OnDisable()
    {
        inputActionsAsset?.FindActionMap("Player")?.Disable();
    }

    /// <summary>8방향 조준 입력 정규화 (아날로그 스틱/마우스 → 8방향 스냅)</summary>
    public static Vector2 QuantizeTo8Directions(Vector2 input)
    {
        if (input.sqrMagnitude < 0.01f) return Vector2.zero;
        
        float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
        // 45도 단위 스냅 (-180 ~ 180)
        float snappedAngle = Mathf.Round(angle / 45f) * 45f;
        float rad = snappedAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
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