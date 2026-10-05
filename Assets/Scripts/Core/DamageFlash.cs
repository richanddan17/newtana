using UnityEngine;

/// <summary>
/// 피격 깜빡임/넉백 시각 효과.
/// 계획서 health-damage-system.md [4], hitbox-hurtbox-system.md [8] 확장.
/// Health와 별도 컴포넌트로 분리 (애니메이션/렌더러 독립적 관리).
/// </summary>
[DisallowMultipleComponent]
public class DamageFlash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] SpriteRenderer[] spriteRenderers; // 자동 수집 안 됨: 수동 할당
    [SerializeField] Health health;

    [Header("Flash Settings")]
    [SerializeField] Color flashColor = Color.white;
    [SerializeField] float flashDuration = 0.1f;
    [SerializeField] int flashCount = 3;
    [SerializeField] bool useMaterialProperty = false; // Material property block 사용
    [SerializeField] string colorPropertyName = "_Color";

    [Header("Knockback Visual")]
    [SerializeField] bool showKnockbackDirection = false;
    [SerializeField] GameObject knockbackIndicatorPrefab;

    MaterialPropertyBlock propertyBlock;
    bool isFlashing = false;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (spriteRenderers == null || spriteRenderers.Length == 0)
        {
            spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        if (useMaterialProperty)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        // Health 이벤트 구독
        if (health != null)
        {
            health.OnDamaged += OnDamaged;
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDamaged -= OnDamaged;
        }
    }

    void OnDamaged(DamageInfo info)
    {
        if (isFlashing) return; // 이미 깜빡이는 중이면 무시 (또는 큐잉)

        StartCoroutine(FlashCoroutine());
        
        // 넉백 방향 표시 (옵션)
        if (showKnockbackDirection && knockbackIndicatorPrefab != null)
        {
            ShowKnockbackIndicator(info.HitDirection);
        }
    }

    System.Collections.IEnumerator FlashCoroutine()
    {
        isFlashing = true;

        Color[] originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                originalColors[i] = spriteRenderers[i].color;
        }

        float interval = flashDuration / flashCount;

        for (int f = 0; f < flashCount; f++)
        {
            // 흰색/플래시 색으로
            SetAllColor(flashColor);
            yield return new WaitForSeconds(interval * 0.5f);

            // 원본 색으로
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    SetColor(i, originalColors[i]);
            }
            yield return new WaitForSeconds(interval * 0.5f);
        }

        // 확실하게 원본 복원
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                SetColor(i, originalColors[i]);
        }

        isFlashing = false;
    }

    void SetAllColor(Color color)
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
            SetColor(i, color);
    }

    void SetColor(int index, Color color)
    {
        if (index >= spriteRenderers.Length || spriteRenderers[index] == null) return;

        if (useMaterialProperty)
        {
            spriteRenderers[index].GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(colorPropertyName, color);
            spriteRenderers[index].SetPropertyBlock(propertyBlock);
        }
        else
        {
            spriteRenderers[index].color = color;
        }
    }

    void ShowKnockbackIndicator(Vector2 direction)
    {
        var indicator = Instantiate(knockbackIndicatorPrefab, transform.position, Quaternion.identity);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        indicator.transform.rotation = Quaternion.Euler(0, 0, angle);
        Destroy(indicator, 0.5f);
    }

    /// <summary>외부에서 강제 플래시 (상태 이상 등)</summary>
    public void TriggerFlash()
    {
        if (!isFlashing)
            StartCoroutine(FlashCoroutine());
    }
}