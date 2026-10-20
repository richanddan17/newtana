using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// 대화 UI - 런타임에 Canvas + 패널 + 화자명 + 대사 + 진행 안내를 코드로 생성.
/// 계획서 room-camera-system.md [7] Dialogue 모드.
/// 씬에 prefab이 없으므로 Show 시점에 Lazy 생성, 대화 종료 후에도 파괴하지 않고 숨김 유지(재사용).
/// 폰트: TMP Settings(기본 폰트 에셋)가 있으면 TextMeshPro, 없으면 uGUI Text 폴백.
/// (manifest의 TMP 패키지 유무와 별개로, TMP Essentials 미가져오면 기본 폰트 에셋이 없어 TMP 사용 불가)
/// </summary>
public class DialogueUI
{
    // 레이아웃 상수 (Inspector 미노출 - 런타임 생성 UI라 하드코딩 허용, 계획서 좌표 규약은 월드 좌표 대상)
    const float PanelHeight = 220f;
    const float PanelMargin = 24f;
    const float TextPadding = 20f;
    const int SpeakerFontSize = 30;
    const int BodyFontSize = 36;
    const int HintFontSize = 28;

    GameObject root;                // DialogueUI (Canvas + ... )
    GameObject panel;
    GameObject continueHint;
    Text speakerLegacy;
    Text bodyLegacy;
    TextMeshProUGUI speakerTmp;
    TextMeshProUGUI bodyTmp;
    bool built;
    bool useTmp;

    /// <summary>대사 표시. hasNextLine=false면 진행 안내(▼) 숨김.</summary>
    public void Show(string speakerName, string text, bool hasNextLine)
    {
        EnsureBuilt();

        panel.SetActive(true);

        // 화자명: 비어있으면 줄 자체를 숨김
        bool hasSpeaker = !string.IsNullOrEmpty(speakerName);
        if (speakerTmp != null) speakerTmp.gameObject.SetActive(hasSpeaker);
        if (speakerLegacy != null) speakerLegacy.gameObject.SetActive(hasSpeaker);
        if (hasSpeaker) SetSpeakerText(speakerName);

        SetBodyText(text);
        continueHint.SetActive(hasNextLine);
    }

    /// <summary>패널 숨김 (캔버스 자체는 유지 - 다음 대화에서 재사용)</summary>
    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>UI 오브젝트 완전 파괴 (씬 언로드 등)</summary>
    public void Destroy()
    {
        if (root != null)
        {
            Object.Destroy(root);
            root = null;
            panel = null;
            continueHint = null;
            speakerLegacy = null;
            bodyLegacy = null;
            speakerTmp = null;
            bodyTmp = null;
            built = false;
        }
    }

    void EnsureBuilt()
    {
        if (built && root != null) return;

        useTmp = DetectTmp();

        // === Canvas ===
        root = new GameObject("DialogueUI");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // HUD 위에 표시
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // === EventSystem (없을 때만 - 신규 Input System용 InputSystemUIInputModule 사용) ===
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>(); // StandaloneInputModule은 Input System 전용 설정과 충돌
        }

        // === 대사 패널 (화면 하단 가로 stretch) ===
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(root.transform, false);
        panel = panelGo;

        var panelImg = panelGo.GetComponent<Image>();
        panelImg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f); // 검정 계열 반투명
        panelImg.raycastTarget = false; // 입력은 키보드로만 받음

        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(1f, 0f);
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.sizeDelta = new Vector2(-PanelMargin * 2f, PanelHeight);
        panelRt.anchoredPosition = new Vector2(0f, PanelMargin);

        // === 화자명 (상단 좌측, 패널 폭 stretch + 고정 높이) ===
        var speakerGo = CreateTextObject("Speaker", panelRt, useTmp, SpeakerFontSize,
            new Vector2(TextPadding, -TextPadding),
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(-TextPadding * 2f, SpeakerFontSize),
            out speakerTmp, out speakerLegacy);
        speakerGo.SetActive(false); // Show()에서 화자명 유무에 따라 활성화
        if (speakerTmp != null) speakerTmp.color = new Color(1f, 0.85f, 0.3f, 1f); // 노란색 계열
        if (speakerLegacy != null) speakerLegacy.color = new Color(1f, 0.85f, 0.3f, 1f);

        // === 대사 본문 (화자명 아래 → 하단, 좌우 stretch) ===
        var bodyGo = CreateTextObject("Body", panelRt, useTmp, BodyFontSize,
            Vector2.zero,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero,
            out bodyTmp, out bodyLegacy);
        var bodyRt = bodyGo.GetComponent<RectTransform>();
        bodyRt.offsetMin = new Vector2(TextPadding, TextPadding);
        bodyRt.offsetMax = new Vector2(-TextPadding, -(SpeakerFontSize + TextPadding * 2));
        if (bodyTmp != null)
        {
            bodyTmp.color = Color.white;
            bodyTmp.textWrappingMode = TextWrappingModes.Normal;
            bodyTmp.overflowMode = TextOverflowModes.Ellipsis;
            bodyTmp.alignment = TextAlignmentOptions.TopLeft;
        }
        if (bodyLegacy != null)
        {
            bodyLegacy.color = Color.white;
            bodyLegacy.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyLegacy.verticalOverflow = VerticalWrapMode.Truncate;
            bodyLegacy.alignment = TextAnchor.UpperLeft;
        }

        // === 다음 라인 안내 (우하단 ▼, 포인트 앵커 + 고정 크기) ===
        var hint = CreateTextObject("ContinueHint", panelRt, useTmp, HintFontSize,
            new Vector2(-TextPadding, TextPadding),
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(48f, 40f),
            out var hintTextTmp, out var hintTextLegacy);
        continueHint = hint;
        if (hintTextTmp != null)
        {
            hintTextTmp.text = "▼";
            hintTextTmp.color = new Color(1f, 1f, 1f, 0.7f);
            hintTextTmp.alignment = TextAlignmentOptions.BottomRight;
        }
        if (hintTextLegacy != null)
        {
            hintTextLegacy.text = "▼";
            hintTextLegacy.color = new Color(1f, 1f, 1f, 0.7f);
            hintTextLegacy.alignment = TextAnchor.LowerRight;
        }

        // 초기에는 숨김
        panel.SetActive(false);
        built = true;
    }

    /// <summary>TMP 기본 폰트 에셋 존재 여부로 TMP 사용 판별 (Essentials 미가져오면 null)</summary>
    static bool DetectTmp()
    {
        try
        {
            return TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
        }
        catch
        {
            return false; // TMP Settings 리소스 없음 → uGUI Text 폴백
        }
    }

    /// <summary>텍스트 오브젝트 생성 (TMP 또는 legacy Text). out 둘 중 하나만非null.</summary>
    static GameObject CreateTextObject(string name, RectTransform parent, bool tmp, int fontSize,
        Vector2 anchoredPos, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta,
        out TextMeshProUGUI tmpOut, out Text legacyOut)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;

        tmpOut = null;
        legacyOut = null;

        if (tmp)
        {
            tmpOut = go.AddComponent<TextMeshProUGUI>();
            tmpOut.fontSize = fontSize;
            tmpOut.raycastTarget = false;
            // fontAsset는 TMP Settings의 기본 폰트 자동 적용
        }
        else
        {
            legacyOut = go.AddComponent<Text>();
            legacyOut.fontSize = fontSize;
            legacyOut.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // Unity 6 내장 폰트 (OS 폴백으로 한글 렌더링)
            legacyOut.alignment = TextAnchor.UpperLeft;
            legacyOut.raycastTarget = false;
        }

        return go;
    }

    void SetSpeakerText(string value)
    {
        if (speakerTmp != null) speakerTmp.text = value;
        if (speakerLegacy != null) speakerLegacy.text = value;
    }

    void SetBodyText(string value)
    {
        if (bodyTmp != null) bodyTmp.text = value;
        if (bodyLegacy != null) bodyLegacy.text = value;
    }
}
