using UnityEngine;

/// <summary>
/// 대화 데이터 (ScriptableObject) - 단선(單線) 스크립트 라인 배열.
/// 계획서 room-camera-system.md [7] Dialogue 모드용 데이터.
/// 에셋 생성: Project 창 우클릭 → Create → RunGun → Dialogue Data.
/// </summary>
[CreateAssetMenu(fileName = "DialogueData", menuName = "RunGun/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    /// <summary>대사 한 줄 (화자 이름 + 텍스트)</summary>
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("화자 이름 (비워두면 이름 줄 자체를 숨김)")]
        public string speakerName = "";

        [TextArea(2, 5)]
        [Tooltip("대사 텍스트")]
        public string text = "";
    }

    [Header("Lines (인덱스 순서대로 진행)")]
    [SerializeField] DialogueLine[] lines = new DialogueLine[0];

    /// <summary>전체 라인 수</summary>
    public int LineCount => lines?.Length ?? 0;

    /// <summary>인덱스 라인 반환 (범위 밖이면 null)</summary>
    public DialogueLine GetLine(int index)
    {
        if (lines == null || index < 0 || index >= lines.Length) return null;
        return lines[index];
    }
}
