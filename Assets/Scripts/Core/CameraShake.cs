using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 카메라 흔들림 시스템.
/// 계획서 Phase 6, room-camera-system.md [3] 확장.
/// - 임펄스 기반 (방향/강도/감쇠)
/// - 여러 소스 합산 (발사, 피격, 폭발, 사망)
/// - Cinemachine 호환 (ImpulseSource 연동 가능)
/// - 언스케일드 타임으로 히트스톱 중에도 동작
/// </summary>
[DisallowMultipleComponent]
public class CameraShake : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform cameraTransform; // 흔들릴 카메라 (보통 Main Camera)
    [SerializeField] bool useCinemachine = false; // Cinemachine ImpulseSource 사용 시

    [Header("Default Settings")]
    [SerializeField] float defaultDuration = 0.2f;
    [SerializeField] float defaultFrequency = 25f;
    [SerializeField] float defaultAmplitude = 0.3f;
    [SerializeField] float defaultDecay = 3f;

    [Header("Trauma Settings (대안: Perlin 기반)")]
    [SerializeField] bool useTrauma = false;
    [SerializeField] float maxTrauma = 1f;
    [SerializeField] float traumaDecay = 1f;

    // 임펄스 기반 상태
    class ShakeImpulse
    {
        public Vector2 Direction;
        public float Amplitude;
        public float Frequency;
        public float Duration;
        public float Decay;
        public float StartTime;
        public bool IsActive;
    }

    readonly List<ShakeImpulse> impulses = new();
    Vector3 originalPosition;
    Quaternion originalRotation;
    float trauma = 0f;

    // Cinemachine
    Cinemachine.CinemachineImpulseSource impulseSource;

    public static CameraShake Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (useCinemachine)
        {
            impulseSource = GetComponent<Cinemachine.CinemachineImpulseSource>();
            if (impulseSource == null)
                impulseSource = gameObject.AddComponent<Cinemachine.CinemachineImpulseSource>();
        }

        if (cameraTransform != null)
        {
            originalPosition = cameraTransform.localPosition;
            originalRotation = cameraTransform.localRotation;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;

        if (useTrauma)
        {
            UpdateTraumaShake();
        }
        else
        {
            UpdateImpulseShake();
        }
    }

    #region Impulse-based Shake (기본)

    /// <summary>방향성 흔들림 추가 (발사 반동, 피격 넉백 방향 등)</summary>
    /// <param name="direction">흔들림 방향 (월드 스페이스, 단위 벡터)</param>
    /// <param name="amplitude">최대 흔들림 거리</param>
    /// <param name="duration">지속 시간</param>
    /// <param name="frequency">진동 주파수 (Hz)</param>
    /// <param name="decay">감쇠 속도</param>
    public static void Shake(Vector2 direction, float amplitude = 0.3f, float duration = 0.2f, float frequency = 25f, float decay = 3f)
    {
        if (Instance == null) return;
        Instance.AddImpulse(direction.normalized, amplitude, duration, frequency, decay);
    }

    /// <summary>방향 없는 흔들림 (폭발, 사망 등)</summary>
    public static void Shake(float amplitude = 0.3f, float duration = 0.2f, float frequency = 25f, float decay = 3f)
    {
        if (Instance == null) return;
        // 랜덤 방향
        Vector2 dir = Random.insideUnitCircle.normalized;
        Instance.AddImpulse(dir, amplitude, duration, frequency, decay);
    }

    /// <summary>프리셋 이름으로 흔들림 (데이터 드리븐)</summary>
    public static void ShakePreset(string presetName)
    {
        if (Instance == null) return;
        Instance.ApplyPreset(presetName);
    }

    void AddImpulse(Vector2 direction, float amplitude, float duration, float frequency, float decay)
    {
        impulses.Add(new ShakeImpulse
        {
            Direction = direction,
            Amplitude = amplitude,
            Frequency = frequency,
            Duration = duration,
            Decay = decay,
            StartTime = Time.unscaledTime,
            IsActive = true
        });

        // Cinemachine Impulse 동시 발생
        if (useCinemachine && impulseSource != null)
        {
            impulseSource.GenerateImpulse(direction * amplitude);
        }
    }

    void UpdateImpulseShake()
    {
        if (impulses.Count == 0) return;

        Vector3 offset = Vector3.zero;
        float now = Time.unscaledTime;

        for (int i = impulses.Count - 1; i >= 0; i--)
        {
            var impulse = impulses[i];
            float elapsed = now - impulse.StartTime;

            if (elapsed >= impulse.Duration)
            {
                impulses.RemoveAt(i);
                continue;
            }

            // 감쇠된 진폭
            float t = elapsed / impulse.Duration;
            float currentAmplitude = impulse.Amplitude * Mathf.Exp(-impulse.Decay * t);

            // 사인파 진동
            float oscillation = Mathf.Sin(elapsed * impulse.Frequency * Mathf.PI * 2f);
            Vector2 shakeOffset = impulse.Direction * oscillation * currentAmplitude;

            offset += new Vector3(shakeOffset.x, shakeOffset.y, 0f);
        }

        // 적용 (로컬 포지션에 덧셈)
        cameraTransform.localPosition = originalPosition + offset;
    }

    #endregion

    #region Trauma-based Shake (대안: Perlin Noise)

    /// <summary>Trauma 기반 흔들림 추가 (0~1)</summary>
    public static void AddTrauma(float amount)
    {
        if (Instance == null) return;
        Instance.trauma = Mathf.Min(Instance.maxTrauma, Instance.trauma + amount);
    }

    void UpdateTraumaShake()
    {
        // Trauma 감소
        trauma = Mathf.Max(0f, trauma - traumaDecay * Time.unscaledDeltaTime);

        if (trauma <= 0f && impulses.Count == 0)
        {
            // 원위치 복구
            cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, originalPosition, Time.unscaledDeltaTime * 10f);
            cameraTransform.localRotation = Quaternion.Lerp(cameraTransform.localRotation, originalRotation, Time.unscaledDeltaTime * 10f);
            return;
        }

        // Perlin noise 기반 흔들림
        float shakeAmount = trauma * trauma; // 제곱으로 자연스러운 감소
        float x = Mathf.PerlinNoise(Time.unscaledTime * 10f, 0f) * 2f - 1f;
        float y = Mathf.PerlinNoise(0f, Time.unscaledTime * 10f) * 2f - 1f;
        float z = Mathf.PerlinNoise(Time.unscaledTime * 10f, Time.unscaledTime * 10f) * 2f - 1f;

        Vector3 shakeOffset = new Vector3(x, y, z) * shakeAmount * defaultAmplitude;
        cameraTransform.localPosition = originalPosition + shakeOffset;

        // 회전도 약간 흔들림
        float rotZ = Mathf.PerlinNoise(Time.unscaledTime * 5f, 5f) * 2f - 1f;
        cameraTransform.localRotation = originalRotation * Quaternion.Euler(0, 0, rotZ * shakeAmount * 5f);
    }

    #endregion

    #region Presets

    [System.Serializable]
    public class ShakePreset
    {
        public string Name;
        public float Amplitude = 0.3f;
        public float Duration = 0.2f;
        public float Frequency = 25f;
        public float Decay = 3f;
        public bool Directional = false;
    }

    [SerializeField] List<ShakePreset> presets = new()
    {
        new ShakePreset { Name = "LightHit", Amplitude = 0.15f, Duration = 0.1f, Frequency = 30f },
        new ShakePreset { Name = "HeavyHit", Amplitude = 0.4f, Duration = 0.3f, Frequency = 20f },
        new ShakePreset { Name = "Explosion", Amplitude = 0.6f, Duration = 0.5f, Frequency = 15f },
        new ShakePreset { Name = "PlayerShoot", Amplitude = 0.1f, Duration = 0.05f, Frequency = 40f, Directional = true },
        new ShakePreset { Name = "EnemyShoot", Amplitude = 0.08f, Duration = 0.05f, Frequency = 35f },
        new ShakePreset { Name = "Dash", Amplitude = 0.2f, Duration = 0.15f, Frequency = 25f, Directional = true },
        new ShakePreset { Name = "Death", Amplitude = 0.5f, Duration = 0.4f, Frequency = 18f },
    };

    void ApplyPreset(string name)
    {
        var preset = presets.Find(p => p.Name == name);
        if (preset == null) return;

        Vector2 dir = preset.Directional ? (transform.right * transform.localScale.x) : Random.insideUnitCircle.normalized;
        AddImpulse(dir, preset.Amplitude, preset.Duration, preset.Frequency, preset.Decay);
    }

    #endregion

    #region Convenience Methods (게임 이벤트용)

    /// <summary>플레이어 발사 반동</summary>
    public static void OnPlayerShoot(Vector2 shootDirection)
    {
        ShakePreset("PlayerShoot");
    }

    /// <summary>적 발사 (작은 흔들림)</summary>
    public static void OnEnemyShoot()
    {
        ShakePreset("EnemyShoot");
    }

    /// <summary>플레이어 피격</summary>
    public static void OnPlayerHit(Vector2 hitDirection)
    {
        Shake(hitDirection, 0.3f, 0.2f, 25f);
        HitStop.Request(0.05f); // 짧은 히트스톱 동반
    }

    /// <summary>적 피격</summary>
    public static void OnEnemyHit()
    {
        ShakePreset("LightHit");
        HitStop.Request(0.03f);
    }

    /// <summary>폭발</summary>
    public static void OnExplosion(Vector2 epicenter, float radius)
    {
        // 거리 기반 감쇠
        if (Instance != null && Instance.cameraTransform != null)
        {
            float dist = Vector2.Distance(Instance.cameraTransform.position, epicenter);
            float intensity = Mathf.Clamp01(1f - dist / radius);
            if (intensity > 0.1f)
            {
                Shake(intensity * 0.6f, 0.4f, 15f);
                HitStop.Request(0.1f * intensity);
            }
        }
    }

    /// <summary>플레이어 사망</summary>
    public static void OnPlayerDeath()
    {
        ShakePreset("Death");
        HitStop.Request(0.15f);
    }

    /// <summary>대시</summary>
    public static void OnDash(Vector2 dashDirection)
    {
        ShakePreset("Dash");
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        if (cameraTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(cameraTransform.position, 0.2f);
        }
    }
}