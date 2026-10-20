using UnityEngine;

/// <summary>
/// 2D 위치 패럴랙스 레이어 (background-parallax-system.md [3]).
/// 배경 위치 = 방 배경 기준점 + (카메라 위치 - 방 배경 기준점) × (1 - factor)
/// - factor X/Y 축별 독립. 0에 가까울수록 멀리, 1이면 카메라와 동일
/// - Transform 위치만 변경 (flipX/스케일/회전/스프라이트 교체 없음)
/// - 카메라를 못 찾으면 조용히 자기 위치 유지 (Camera.main 캐시 + 파괴 시 재탐색)
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // RoomCamera(기본 0)보다 뒤에 실행되어 1프레임 지연 방지
public class ParallaxLayer : MonoBehaviour
{
    [Header("속도 계수 (0에 가까울수록 멀리, 1이면 카메라와 동일)")]
    [SerializeField] float parallaxX = 0.7f;
    [SerializeField] float parallaxY = 0.5f;

    [Header("Reference (기준점)")]
    [SerializeField] Transform anchor; // 미지정 시 Awake에서 자기 초기 위치 캐시 (Room 자식 배경 = 방 배경 기준점)

    // 캐시
    Camera cachedCamera;
    Vector3 cachedAnchorPos;

    void Awake()
    {
        // 방 자식 배경의 초기 위치 = 방 배경 기준점 (플랜 [3]).
        // anchor를 지정했다가 나중에 파괴된 경우에도 기준점이 영구 소실되지 않도록 항상 캐시.
        cachedAnchorPos = transform.position;
    }

    void LateUpdate()
    {
        // 카메라 캐시: 미생성/파괴 시 재탐색. 못 찾으면 조용히 유지 (에러 로그 폭탄 금지)
        if (cachedCamera == null)
            cachedCamera = Camera.main;
        if (cachedCamera == null) return;

        Vector3 basePos = anchor != null ? anchor.position : cachedAnchorPos;
        Vector3 camPos = cachedCamera.transform.position;

        // 배경 위치 = 방 배경 기준점 + (카메라 위치 - 방 배경 기준점) × (1 - factor)
        // 카메라가 방 배경 기준점 위에 있으면 배경도 거기, 가장자리로 갈수록 factor 비율만큼만 따라옴
        Vector3 pos;
        pos.x = basePos.x + (camPos.x - basePos.x) * (1f - parallaxX);
        pos.y = basePos.y + (camPos.y - basePos.y) * (1f - parallaxY);
        pos.z = transform.position.z; // z는 그대로 유지 (변경 금지 — Sorting Layer로 앞뒤 결정)

        transform.position = pos;
    }

    void OnDrawGizmosSelected()
    {
        // 기준점(노랑)과 현재 위치(시안) 표시
        Vector3 basePos = anchor != null ? anchor.position
            : Application.isPlaying ? cachedAnchorPos : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(basePos, 0.05f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(basePos, transform.position);
    }
}
