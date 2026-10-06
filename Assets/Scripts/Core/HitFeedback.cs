using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 피격 피드백 통합 관리 (이펙트, 사운드, 파티클).
/// 계획서 Phase 6, weapon-projectile-system.md [7], hitbox-hurtbox-system.md [8] 확장.
/// - 오브젝트 풀링으로 이펙트 재사용
/// - HitType별 다른 이펙트/사운드
/// - 월드 공간 위치에서 즉시 재생
/// </summary>
public static class HitFeedback
{
    // 풀링용 프리팹 레지스트리
    static readonly System.Collections.Generic.Dictionary<HitType, GameObject> hitEffectPrefabs = new();
    static readonly System.Collections.Generic.Dictionary<HitType, ObjectPool<GameObject>> hitEffectPools = new();
    static readonly System.Collections.Generic.Dictionary<HitType, AudioClip> hitSounds = new();

    static GameObject effectsContainer;
    static AudioSource globalAudioSource;

    /// <summary>초기화 (게임 시작 시 한 번 호출)</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        effectsContainer = new GameObject("[HitFeedbackEffects]");
        effectsContainer.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(effectsContainer);

        // 전역 오디오 소스 (3D 사운드용)
        globalAudioSource = effectsContainer.AddComponent<AudioSource>();
        globalAudioSource.spatialBlend = 1f; // 3D
        globalAudioSource.rolloffMode = AudioRolloffMode.Linear;
        globalAudioSource.maxDistance = 30f;
    }

    /// <summary>HitType별 이펙트 프리팹 등록</summary>
    public static void RegisterEffect(HitType hitType, GameObject effectPrefab, AudioClip soundClip = null)
    {
        if (effectPrefab == null) return;

        hitEffectPrefabs[hitType] = effectPrefab;
        hitSounds[hitType] = soundClip;

        // 풀 생성
        if (!hitEffectPools.ContainsKey(hitType))
        {
            hitEffectPools[hitType] = new ObjectPool<GameObject>(
                createFunc: () => Object.Instantiate(effectPrefab, effectsContainer.transform),
                actionOnGet: obj => obj.SetActive(true),
                actionOnRelease: obj => obj.SetActive(false),
                actionOnDestroy: obj => Object.Destroy(obj),
                collectionCheck: false,
                defaultCapacity: 10,
                maxSize: 50
            );
        }
    }

    /// <summary>피격 이펙트/사운드 재생</summary>
    /// <param name="hitType">피격 타입</param>
    /// <param name="position">월드 위치</param>
    /// <param name="normal">피격 법선 (이펙트 회전용)</param>
    /// <param name="attacker">공격자 (팀별 색상 변경용)</param>
    public static void Play(HitType hitType, Vector3 position, Vector3 normal = default, GameObject attacker = null)
    {
        Initialize();

        // 이펙트 재생
        if (hitEffectPools.TryGetValue(hitType, out var pool))
        {
            var effect = pool.Get();
            effect.transform.position = position;
            
            // 법선 방향 회전
            if (normal != default)
                effect.transform.rotation = Quaternion.LookRotation(Vector3.forward, normal);
            else
                effect.transform.rotation = Quaternion.identity;

            // 팀별 색상 변경 (옵션)
            if (attacker != null)
            {
                var teamComp = attacker.GetComponent<TeamComponent>();
                if (teamComp != null)
                    ApplyTeamColor(effect, teamComp.Team);
            }

            // 자동 반환 (파티클 시스템 길이 기반)
            var ps = effect.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                float lifetime = ps.main.duration + ps.main.startLifetimeMultiplier;
                ReturnToPoolDelayed(pool, effect, lifetime);
            }
            else
            {
                // 파티클 없으면 1초 후 반환
                ReturnToPoolDelayed(pool, effect, 1f);
            }
        }

        // 사운드 재생
        if (hitSounds.TryGetValue(hitType, out var clip) && clip != null)
        {
            // 3D 사운드는 AudioSource.PlayClipAtPoint 사용
            AudioSource.PlayClipAtPoint(clip, position, 1f);
        }
    }

    static async void ReturnToPoolDelayed(ObjectPool<GameObject> pool, GameObject effect, float delay)
    {
        await System.Threading.Tasks.Task.Delay((int)(delay * 1000f));
        if (effect != null && effect.activeInHierarchy)
            pool.Release(effect);
    }

    static void ApplyTeamColor(GameObject effect, Team team)
    {
        var ps = effect.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            Color color = team switch
            {
                Team.Player => Color.cyan,
                Team.Enemy => Color.red,
                Team.Neutral => Color.yellow,
                _ => Color.white
            };
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }

        // SpriteRenderer도 색상 변경
        var srs = effect.GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in srs)
        {
            Color color = team switch
            {
                Team.Player => Color.cyan,
                Team.Enemy => Color.red,
                Team.Neutral => Color.yellow,
                _ => Color.white
            };
            sr.color = color;
        }
    }

    /// <summary>환경 데미지용 (함정, 가시 등)</summary>
    public static void PlayEnvironmental(Vector3 position, Vector3 normal, HitType type = HitType.Trap)
    {
        Play(type, position, normal, null);
    }

    /// <summary>즉사/낙사용</summary>
    public static void PlayInstantKill(Vector3 position)
    {
        Play(HitType.InstantKill, position, Vector3.up, null);
    }
}

/// <summary>
/// MonoBehaviour 헬퍼: 컴포넌트에서 쉽게 호출용
/// </summary>
public class HitFeedbackPlayer : MonoBehaviour
{
    [Header("Auto-play on collision (Hurtbox 등에서 사용)")]
    [SerializeField] bool autoPlayOnTrigger = false;
    [SerializeField] HitType hitType = HitType.Bullet;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!autoPlayOnTrigger) return;
        
        var projectile = other.GetComponent<ProjectileData>();
        if (projectile != null)
        {
            HitFeedback.Play(projectile.HitType, transform.position, projectile.transform.right, projectile.Owner);
        }
    }

    /// <summary>외부에서 호출</summary>
    public void PlayFeedback(HitType type, Vector3 position, Vector3 normal, GameObject attacker = null)
    {
        HitFeedback.Play(type, position, normal, attacker);
    }
}