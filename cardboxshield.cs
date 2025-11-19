using System;
using System.Collections.Generic;
using UnityEngine;

namespace cardboxshield
{
    /// <summary>
    /// 경화 모드 컨트롤러.
    ///  - PageDown 키로 경화 모드 ON/OFF
    ///  - 이동거리 + 시간에 따라 경화 게이지 감소
    ///  - 0 되면 자동으로 경화 해제
    ///  - 화면 상단 중앙에 텍스트 + 게이지 바 (Y 좌표 고정)
    /// </summary>
    internal class CardboxShieldController : MonoBehaviour
    {
        private static CardboxShieldController _instance;
        public static CardboxShieldController Instance
        {
            get { return _instance; }
        }

        private CharacterMainControl _player;

        private bool _isHardenMode;
        private float _gauge = MaxGauge;
        private const float MaxGauge = 100f;

        private Vector3 _lastPos;

        // 튜닝 포인트: 1m 움직일 때 게이지 몇 깎일지
        private const float MoveDrainPerMeter = 5f;

        // 튜닝 포인트: 가만히 있어도 초당 얼마나 깎일지
        private const float TimeDrainPerSecond = 0.5f;

        // 토글 키
        private const KeyCode ToggleKey = KeyCode.PageDown;

        // HUD용
        private GUIStyle _mainLabelStyle;
        private GUIStyle _subLabelStyle;
        private Texture2D _whiteTexture;

        // 짧게 뜨는 멘트용
        private float _funMessageTimer;
        private string _funMessage;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _player = FindLocalPlayer();
            if (_player != null)
            {
                _lastPos = _player.transform.position;
            }
        }

        private void Update()
        {
            // PageDown으로 토글
            if (Input.GetKeyDown(ToggleKey))
            {
                if (_isHardenMode)
                {
                    ExitHardenMode("경화 모드 해제");
                }
                else
                {
                    TryEnterHardenMode();
                }
            }

            if (!_isHardenMode)
            {
                if (_funMessageTimer > 0f)
                {
                    _funMessageTimer -= Time.deltaTime;
                    if (_funMessageTimer < 0f) _funMessageTimer = 0f;
                }
                return;
            }

            if (_player == null)
            {
                _player = FindLocalPlayer();
                if (_player != null)
                    _lastPos = _player.transform.position;

                return;
            }

            UpdateGauge();
        }

        private void UpdateGauge()
        {
            // 이동량에 따라 게이지 감소
            Vector3 currentPos = _player.transform.position;
            float dist = Vector3.Distance(currentPos, _lastPos);

            if (dist > 0.01f)
            {
                _gauge -= dist * MoveDrainPerMeter;
                _lastPos = currentPos;
            }

            // 시간 경과에 따라도 조금씩 감소
            _gauge -= Time.deltaTime * TimeDrainPerSecond;

            if (_gauge < 0f) _gauge = 0f;

            // 완전 소진
            if (_gauge <= 0f)
            {
                BreakHarden();
            }

            // 멘트 타이머 감소
            if (_funMessageTimer > 0f)
            {
                _funMessageTimer -= Time.deltaTime;
                if (_funMessageTimer < 0f) _funMessageTimer = 0f;
            }
        }

        /// <summary>
        /// 외부(인벤 쪽)에서 경화 아이템 소비 직후 호출하고 싶으면
        /// CardboxShieldController.Instance?.TryEnterHardenMode(); 이렇게 쓰면 됨.
        /// </summary>
        public void TryEnterHardenMode()
        {
            if (_player == null)
            {
                _player = FindLocalPlayer();
                if (_player == null)
                {
                    Debug.Log("[cardboxshield] 로컬 플레이어를 찾지 못해 경화 모드 진입 실패");
                    ShowFunMessage("플레이어를 못 찾았어…");
                    return;
                }
            }

            if (_gauge <= 0f)
            {
                Debug.Log("[cardboxshield] 게이지 0, 경화 모드 진입 불가");
                ShowFunMessage("이미 경화가 풀렸어!");
                return;
            }

            _isHardenMode = true;
            _lastPos = _player.transform.position;

            ShowFunMessage("경화 모드 ON!");
            Debug.Log("[cardboxshield] EnterHarden - 경화 모드 ON. 게이지 = " + _gauge);
        }

        private void ExitHardenMode(string reason)
        {
            _isHardenMode = false;

            ShowFunMessage(reason);
            Debug.Log("[cardboxshield] ExitHarden - 경화 모드 OFF. 남은 게이지 = " + _gauge);
        }

        private void BreakHarden()
        {
            if (!_isHardenMode) return;

            _gauge = 0f;
            ExitHardenMode("경화가 완전히 풀렸다!!");
        }

        private void ShowFunMessage(string msg)
        {
            _funMessage = msg;
            _funMessageTimer = 2.0f;
        }

        /// <summary>
        /// 카메라와 가장 가까운 CharacterMainControl을 로컬 플레이어로 간주
        /// </summary>
        private CharacterMainControl FindLocalPlayer()
        {
            CharacterMainControl[] allChars = GameObject.FindObjectsOfType<CharacterMainControl>();
            if (allChars == null || allChars.Length == 0)
                return null;

            Camera cam = Camera.main;
            CharacterMainControl best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < allChars.Length; i++)
            {
                CharacterMainControl c = allChars[i];
                if (c == null) continue;

                try
                {
                    float d;
                    if (cam != null)
                    {
                        d = Vector3.Distance(cam.transform.position, c.transform.position);
                    }
                    else
                    {
                        d = c.transform.position.sqrMagnitude;
                    }

                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
                catch
                {
                    // 무시
                }
            }

            return best;
        }

        private void EnsureGuiResources()
        {
            if (_whiteTexture == null)
            {
                _whiteTexture = new Texture2D(1, 1);
                _whiteTexture.SetPixel(0, 0, Color.white);
                _whiteTexture.Apply();
            }

            if (_mainLabelStyle == null)
            {
                _mainLabelStyle = new GUIStyle(GUI.skin.label);
                _mainLabelStyle.alignment = TextAnchor.MiddleCenter;
                _mainLabelStyle.fontSize = 18;
                _mainLabelStyle.fontStyle = FontStyle.Bold;
                _mainLabelStyle.normal.textColor = Color.white;
            }

            if (_subLabelStyle == null)
            {
                _subLabelStyle = new GUIStyle(GUI.skin.label);
                _subLabelStyle.alignment = TextAnchor.MiddleCenter;
                _subLabelStyle.fontSize = 14;
                _subLabelStyle.normal.textColor = Color.yellow;
            }
        }

private void OnGUI()
{
    // 경화 모드도 아니고 멘트도 없으면 그리지 않음
    if (!_isHardenMode && _funMessageTimer <= 0f) return;

    EnsureGuiResources();

    float width = 220f;
    float x = (Screen.width - width) * 0.5f;

    // 🔽 위치 조정
    float barY   = 145f;              // 하얀 바를 살짝 더 아래로
    float textY  = barY + 22f;        // 바 바로 아래 경화 모드 텍스트
    float msgY   = textY + 20f;       // 그 아래 한 줄 멘트

    // 배경 바
    Rect bgRect = new Rect(x, barY, width, 16f);

    // 채워지는 바
    float ratio = MaxGauge > 0f ? (_gauge / MaxGauge) : 0f;
    if (ratio < 0f) ratio = 0f;
    if (ratio > 1f) ratio = 1f;
    Rect fillRect = new Rect(x + 2f, barY + 2f, (width - 4f) * ratio, 12f);

    Color oldColor = GUI.color;

    // 반투명 검정 배경
    GUI.color = new Color(0f, 0f, 0f, 0.6f);
    GUI.DrawTexture(bgRect, _whiteTexture);

    // 흰색 게이지 바
    GUI.color = Color.white;
    GUI.DrawTexture(fillRect, _whiteTexture);

    // 🔽 경화 상태 텍스트 (바 아래)
    string mainText = string.Format(
        "경화 모드 {0}% ({1})",
        Mathf.RoundToInt(_gauge),
        _gauge <= 0f ? "해제" : (_isHardenMode ? "발동 중" : "대기 중")
    );

    Rect textRect = new Rect(x, textY, width, 24f);
    GUI.Label(textRect, mainText, _mainLabelStyle);

    // 🔽 재밌는 한 줄 멘트 (그 아래)
    if (_funMessageTimer > 0f && !string.IsNullOrEmpty(_funMessage))
    {
        Rect subRect = new Rect(x, msgY, width, 20f);
        GUI.Label(subRect, _funMessage, _subLabelStyle);
    }

    GUI.color = oldColor;
}
    }

    /// <summary>
    /// 워크샵 로더가 요구하는 공식 엔트리포인트.
    /// cardboxshield.ModBehaviour 가 있어야 하고,
    /// Duckov.Modding.ModBehaviour 를 상속해야 함.
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        protected override void OnAfterSetup()
        {
            try
            {
                GameObject go = new GameObject("HardenModeRoot");
                UnityEngine.Object.DontDestroyOnLoad(go);

                go.AddComponent<CardboxShieldController>();

                Debug.Log("[cardboxshield] ModBehaviour.OnAfterSetup - 경화 모드 초기화 완료");
            }
            catch (Exception ex)
            {
                Debug.Log("[cardboxshield] 초기화 예외: " + ex);
            }
        }
    }
}
