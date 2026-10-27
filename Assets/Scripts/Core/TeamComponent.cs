using UnityEngine;

/// <summary>
/// 팀/진영 정의. 계획서 [공통 규약 3] 준수.
/// Layer와 1:1 매핑되어 Collision Matrix로 필터링.
/// </summary>
public enum Team
{
    None = 0,
    Player = 1,
    Enemy = 2,
    Neutral = 3, // 상호작용 오브젝트, 함정 등
}

public static class TeamExtensions
{
    /// <summary>레이어 이름 반환 (Unity Layer 설정과 동일하게 맞출 것)</summary>
    public static string LayerName(this Team team) => team switch
    {
        Team.Player => "Player",
        Team.Enemy => "Enemy",
        Team.Neutral => "Neutral",
        _ => "Default"
    };

    /// <summary>레이어 인덱스 반환 (Project Settings > Tags and Layers에서 동일 번호 부여 필요)</summary>
    public static int LayerIndex(this Team team) => team switch
    {
        Team.Player => 6,   // User Layer 6
        Team.Enemy => 7,    // User Layer 7
        Team.Neutral => 8,  // User Layer 8
        _ => 0
    };

    /// <summary>LayerMask 반환</summary>
    public static LayerMask LayerMask(this Team team) => 1 << team.LayerIndex();

    /// <summary>적대 관계 판단: true면 서로 데미지 줄 수 있음</summary>
    public static bool IsHostileTo(this Team a, Team b)
    {
        if (a == Team.None || b == Team.None) return false;
        if (a == Team.Neutral || b == Team.Neutral) return false;
        return a != b; // Player <-> Enemy만 적대
    }

    /// <summary>GameObject의 Team 가져오기 (컴포넌트 또는 레이어로 추론)</summary>
    public static Team GetTeam(this GameObject obj)
    {
        var comp = obj.GetComponent<TeamComponent>();
        if (comp != null) return comp.Team;

        int layer = obj.layer;
        if (layer == Team.Player.LayerIndex()) return Team.Player;
        if (layer == Team.Enemy.LayerIndex()) return Team.Enemy;
        if (layer == Team.Neutral.LayerIndex()) return Team.Neutral;
        return Team.None;
    }
}

/// <summary>
/// GameObject에 부착해 팀을 명시적으로 지정.
/// 레이어 기반 추론 대신 이 컴포넌트를 권장.
/// </summary>
[DisallowMultipleComponent]
public class TeamComponent : MonoBehaviour
{
    [SerializeField] Team team = Team.None;
    public Team Team => team;

    void Reset()
    {
        // 레이어가 이미 설정돼 있으면 그에 맞춤
        if (gameObject.layer == Team.Player.LayerIndex()) team = Team.Player;
        else if (gameObject.layer == Team.Enemy.LayerIndex()) team = Team.Enemy;
        else if (gameObject.layer == Team.Neutral.LayerIndex()) team = Team.Neutral;
    }

    void OnValidate()
    {
        // 팀 변경 시 레이어 동기화 (에디터에서만)
#if UNITY_EDITOR
        if (!Application.isPlaying)
            gameObject.layer = team.LayerIndex();
#endif
    }
}