using UnityEngine;
using Cinemachine;

/// <summary>
/// 조준 방향으로 카메라를 선행(lead)시키되, 위치 이동을 클램프해
/// 플레이어가 안전 영역(FramingTransposer soft zone) 밖으로 밀려나지 않게 한다.
///
/// Cinemachine 2.10에서 FramingTransposer의 "하드 리밋"은 곧 m_SoftZoneWidth/Height이며,
/// 그 클램프는 Follow 대상(플레이어)을 보호한다. 따라서 조준 선행을 Follow 대상에 얹으면
/// (프록시/ m_TrackedObjectOffset) 보호 대상이 어긋나 플레이어가 화면 밖으로 나간다.
/// 이 확장은 Finalize 단계(트랜스포저·Confiner2D 이후)에서 카메라 공간 보정을 더하므로,
/// 선행을 주면서도 플레이어의 화면 내 보장을 깨지 않는다.
/// </summary>
[SaveDuringPlay]
[AddComponentMenu("")]
public class AimLeadExtension : CinemachineExtension
{
    [Tooltip("조준 방향으로 선행할 거리 (월드 단위)")]
    public float m_LeadDistance = 2.5f;

    [Tooltip("선행이 목표에 수렴하는 시간 상수 (초). 클수록 부드럽다")]
    public float m_LeadSmoothing = 0.18f;

    [Tooltip("안전 영역 비율 — FramingTransposer의 m_SoftZoneWidth/Height와 동일하게 맞출 것")]
    [Range(0.5f, 1f)] public float m_SafeScreenFraction = 0.8f;

    [HideInInspector] public Transform m_Target;                    // 보호 대상(플레이어)
    [HideInInspector] public Vector2 m_AimDirection = Vector2.zero; // 월드 공간 단위벡터

    class ExtraState
    {
        public Vector2 Lead;
        public bool Initialized;
    }

    public void ResetLead()
    {
        if (VirtualCamera == null) return;
        var es = GetExtraState<ExtraState>(VirtualCamera);
        es.Lead = Vector2.zero;
        es.Initialized = false;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (m_Target == null || m_LeadDistance <= 0f) return;

        var es = GetExtraState<ExtraState>(vcam);

        Vector2 desired = m_AimDirection * m_LeadDistance;
        if (!es.Initialized || deltaTime <= 0f)
        {
            es.Lead = desired;
            es.Initialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, m_LeadSmoothing));
            es.Lead = Vector2.Lerp(es.Lead, desired, k);
        }

        Vector3 camPos = state.FinalPosition;      // Confiner2D 보정 포함 위치
        float halfH = state.Lens.OrthographicSize;
        float halfW = halfH * state.Lens.Aspect;
        Vector3 rel = m_Target.position - camPos;  // 카메라 기준 플레이어 위치

        // 카메라 이동을 클램프 — 이동 후에도 플레이어가 안전 영역 안에 머물게 한다
        Vector3 shift = new Vector3(es.Lead.x, es.Lead.y, 0f);
        float limX = halfW * m_SafeScreenFraction;
        float limY = halfH * m_SafeScreenFraction;
        shift.x = Mathf.Clamp(shift.x, rel.x - limX, rel.x + limX);
        shift.y = Mathf.Clamp(shift.y, rel.y - limY, rel.y + limY);
        state.PositionCorrection += shift;         // Brain이 FinalPosition으로 적용
    }
}
