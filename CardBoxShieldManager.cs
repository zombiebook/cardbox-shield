using System;
using System.Reflection;
using UnityEngine;

namespace cardboxshield
{
    // Duckov 로더 엔트리 포인트
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        protected override void OnAfterSetup()
        {
            try
            {
                GameObject go = new GameObject("CardBoxShieldRoot");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<CardBoxShieldManager>();
                Debug.Log("[cardboxshield] ModBehaviour.OnAfterSetup - manager created");
            }
            catch (Exception ex)
            {
                Debug.Log("[cardboxshield] OnAfterSetup 예외: " + ex);
            }
        }
    }

    internal class CardBoxShieldManager : MonoBehaviour
    {
        private const KeyCode ToggleKey = KeyCode.PageDown;

        // 상자 내구도
        private const float MaxDurability = 100f;

        // 쿨타임(초) – 필요하면 여기 숫자만 바꿔
        private const float CooldownSeconds = 30f;

        // 플레이어 / 숨김 관련
        private CharacterMainControl _playerMain;
        private Transform _hudAnchor;
        private MonoBehaviour _duckovHider;
        private FieldInfo _hiddenField;
        private FieldInfo _targetHideField;
        private FieldInfo _teamField;

        // HP 추적
        private object _healthObj;
        private FieldInfo _healthValueField;
        private float _lastHealthValue;
        private bool _healthFieldReady;

        // 렌더러 캐시
        private Renderer[] _renderers;
        private bool[] _rendererOriginalEnabled;

        // 상태
        private bool _isBoxMode;
        private float _durability = MaxDurability;

        // 쿨타임 상태
        private bool _onCooldown;
        private float _cooldownEndTime;

        // GUI
        private bool _styleReady;
        private GUIStyle _barBgStyle;
        private GUIStyle _barFillStyle;
        private float _loadedMsgEndTime;

        private void Awake()
        {
            _loadedMsgEndTime = Time.time + 10f;
            Debug.Log("[cardboxshield] CardBoxShieldManager.Awake");
        }

        private void Start()
        {
            TryFindPlayer();
        }

        private void Update()
        {
            // 플레이어 사라졌으면 재탐색
            if (_playerMain == null)
            {
                TryFindPlayer();
            }

            // 쿨타임 끝났는지 체크
            if (_onCooldown && Time.time >= _cooldownEndTime)
            {
                _onCooldown = false;
                Debug.Log("[cardboxshield] Cooldown 종료, 다시 사용 가능");
            }

            // 토글 키 입력
            if (Input.GetKeyDown(ToggleKey))
            {
                if (!_isBoxMode)
                {
                    // 켜려고 하는데 쿨타임이면 무시
                    if (_onCooldown)
                    {
                        float remain = _cooldownEndTime - Time.time;
                        if (remain < 0f) remain = 0f;
                        Debug.Log("[cardboxshield] 쿨타임 중, 남은 시간 " + remain.ToString("F1") + "s");
                    }
                    else
                    {
                        Debug.Log("[cardboxshield] PageDown 눌림 - EnterBox 시도");
                        EnterBox("manual key");
                    }
                }
                else
                {
                    Debug.Log("[cardboxshield] PageDown 눌림 - ExitBox 시도");
                    ExitBox("manual key");
                }
            }

            // 상자 모드 중 HP 변화 감지 → 내구도 소모
            if (_isBoxMode && _healthFieldReady)
            {
                float curHp = ReadHealthValue();

                // 데미지(HP 감소) 감지
                if (curHp < _lastHealthValue - 0.01f)
                {
                    float diff = _lastHealthValue - curHp;
                    if (diff < 0f) diff = 0f;

                    // 플레이어 HP 복구 → 상자가 대신 맞음
                    WriteHealthValue(_lastHealthValue);

                    // 내구도 감소
                    _durability -= diff;
                    if (_durability < 0f) _durability = 0f;

                    Debug.Log("[cardboxshield] Damage detected: -" + diff +
                              " HP -> durability=" + _durability);

                    if (_durability <= 0f)
                    {
                        ExitBox("durability empty");
                    }
                }
                else
                {
                    // 회복/변화 없으면 기준값만 갱신
                    _lastHealthValue = curHp;
                }
            }
        }

        // ===================== Player 찾기 =====================

private bool TryFindPlayer()
{
    try
    {
        var all = GameObject.FindObjectsOfType<CharacterMainControl>();
        if (all == null || all.Length == 0)
        {
            // 플레이어가 아직 안 생긴 상태(메뉴/로딩 등)에서는
            // 콘솔에 로그 찍지 않고 조용히 실패만 리턴
            return false;
        }

        CharacterMainControl chosen = null;

        // 1차: team 문자열 + 입력 컴포넌트 여부로 추정
        foreach (var c in all)
        {
            string teamName = GetTeamName(c);
            bool isPlayerByTeam = !string.IsNullOrEmpty(teamName) &&
                                  teamName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasInputComp = HasInputLikeComponent(c);

            // 이 디버그는 필요하면 남겨두고, 너무 시끄러우면 이것도 지워도 됨
            Debug.Log("[cardboxshield] 후보 Character=" + c.name +
                      " team=" + teamName +
                      " hasInputLike=" + hasInputComp);

            if (isPlayerByTeam || hasInputComp)
            {
                chosen = c;
                break;
            }
        }

        if (chosen == null)
        {
            chosen = all[0];
            Debug.Log("[cardboxshield] team/input 기준 실패, 첫 번째 Character 사용: " + chosen.name);
        }

        _playerMain = chosen;

        // HUD 앵커
        _hudAnchor = _playerMain.transform;
        var cm = GetCharacterModel(_playerMain);
        if (cm != null)
        {
            var rootField = cm.GetType().GetField(
                "modelRoot",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rootField != null)
            {
                var v = rootField.GetValue(cm) as Transform;
                if (v != null) _hudAnchor = v;
            }
        }

        // DuckovHider
        _duckovHider = _playerMain.GetComponent("DuckovHider") as MonoBehaviour;

        Debug.Log("[cardboxshield] 최종 Player 선택: " + _playerMain.name +
                  " hudAnchor=" + (_hudAnchor != null ? _hudAnchor.name : "null") +
                  " hider=" + (_duckovHider != null ? _duckovHider.GetType().Name : "null"));

        EnsureHideFieldsCached();
        CacheRenderers();
        CacheHealthField();

        return true;
    }
    catch (Exception ex)
    {
        Debug.Log("[cardboxshield] TryFindPlayer 예외: " + ex);
        return false;
    }
}


        private string GetTeamName(CharacterMainControl c)
        {
            try
            {
                if (_teamField == null)
                {
                    _teamField = typeof(CharacterMainControl).GetField(
                        "team", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_teamField == null) return null;

                object v = _teamField.GetValue(c);
                return v != null ? v.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private bool HasInputLikeComponent(CharacterMainControl c)
        {
            try
            {
                var comps = c.GetComponents<Component>();
                if (comps == null) return false;

                for (int i = 0; i < comps.Length; i++)
                {
                    var comp = comps[i];
                    if (comp == null) continue;

                    string lower = comp.GetType().Name.ToLowerInvariant();
                    if (lower.Contains("input") ||
                        lower.Contains("controller") ||
                        lower.Contains("local"))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private object GetCharacterModel(CharacterMainControl c)
        {
            try
            {
                var f = typeof(CharacterMainControl).GetField(
                    "characterModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return null;
                return f.GetValue(c);
            }
            catch
            {
                return null;
            }
        }

        // ===================== Health / HP 리플렉션 =====================

        private void CacheHealthField()
        {
            _healthObj = null;
            _healthValueField = null;
            _healthFieldReady = false;
            _lastHealthValue = 0f;

            if (_playerMain == null) return;

            try
            {
                var hf = typeof(CharacterMainControl).GetField(
                    "health", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (hf == null)
                {
                    Debug.Log("[cardboxshield] CharacterMainControl.health 필드를 찾지 못함");
                    return;
                }

                _healthObj = hf.GetValue(_playerMain);
                if (_healthObj == null)
                {
                    Debug.Log("[cardboxshield] health 인스턴스가 null");
                    return;
                }

                var type = _healthObj.GetType();
                var fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                FieldInfo best = null;
                int bestScore = 0;

                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    var ft = f.FieldType;

                    if (ft != typeof(int) && ft != typeof(float) && ft != typeof(double))
                        continue;

                    string n = f.Name.ToLowerInvariant();

                    if (n.Contains("hash") || n.Contains("timer") ||
                        n.Contains("cool") || n.Contains("regen"))
                        continue;

                    int score = 0;
                    if (n.Contains("health") || n.Contains("hp")) score += 2;
                    if (n.Contains("current") || n.Contains("cur")) score += 1;
                    if (n.Contains("max")) score -= 2;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = f;
                    }
                }

                if (best == null || bestScore <= 0)
                {
                    Debug.Log("[cardboxshield] Health 내 HP 후보 필드를 찾지 못함");
                    return;
                }

                _healthValueField = best;
                _healthFieldReady = true;
                _lastHealthValue = ReadHealthValue();

                Debug.Log("[cardboxshield] Health HP 필드 선택: " +
                          best.Name + " 초기값=" + _lastHealthValue);
            }
            catch (Exception ex)
            {
                Debug.Log("[cardboxshield] CacheHealthField 예외: " + ex);
            }
        }

        private float ReadHealthValue()
        {
            if (!_healthFieldReady || _healthObj == null || _healthValueField == null)
                return 0f;

            try
            {
                object v = _healthValueField.GetValue(_healthObj);
                if (v == null) return 0f;

                if (v is int) return (int)v;
                if (v is float) return (float)v;
                if (v is double) return (float)(double)v;
            }
            catch
            {
            }

            return 0f;
        }

        private void WriteHealthValue(float value)
        {
            if (!_healthFieldReady || _healthObj == null || _healthValueField == null)
                return;

            try
            {
                if (_healthValueField.FieldType == typeof(int))
                {
                    _healthValueField.SetValue(_healthObj, (int)value);
                }
                else if (_healthValueField.FieldType == typeof(float))
                {
                    _healthValueField.SetValue(_healthObj, value);
                }
                else if (_healthValueField.FieldType == typeof(double))
                {
                    _healthValueField.SetValue(_healthObj, (double)value);
                }
            }
            catch
            {
            }
        }

        // ===================== 숨김 / 렌더러 =====================

        private void EnsureHideFieldsCached()
        {
            if (_playerMain == null) return;

            if (_hiddenField == null)
            {
                _hiddenField = typeof(CharacterMainControl).GetField(
                    "hidden", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_hiddenField != null)
                {
                    Debug.Log("[cardboxshield] CharacterMainControl.hidden 필드 캐시 완료");
                }
            }

            if (_duckovHider != null && _targetHideField == null)
            {
                _targetHideField = _duckovHider.GetType().GetField(
                    "targetHide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_targetHideField != null)
                {
                    Debug.Log("[cardboxshield] DuckovHider.targetHide 필드 캐시 완료");
                }
            }
        }

        private void CacheRenderers()
        {
            if (_playerMain == null) return;

            _renderers = _playerMain.GetComponentsInChildren<Renderer>(true);
            if (_renderers == null) _renderers = new Renderer[0];

            _rendererOriginalEnabled = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _rendererOriginalEnabled[i] = _renderers[i].enabled;
            }

            Debug.Log("[cardboxshield] 렌더러 캐시 완료, count=" + _renderers.Length);
        }

        private void TrySetHidden(bool hidden)
        {
            if (_playerMain == null) return;

            EnsureHideFieldsCached();

            if (_hiddenField != null)
            {
                _hiddenField.SetValue(_playerMain, hidden);
                bool cur = (bool)_hiddenField.GetValue(_playerMain);
                Debug.Log("[cardboxshield] CharacterMainControl.hidden = " + cur);
            }

            if (_duckovHider != null && _targetHideField != null)
            {
                _targetHideField.SetValue(_duckovHider, hidden);
                bool cur2 = (bool)_targetHideField.GetValue(_duckovHider);
                Debug.Log("[cardboxshield] DuckovHider.targetHide = " + cur2);
            }

            ApplyRendererHidden(hidden);
        }

        private void ApplyRendererHidden(bool hidden)
        {
            if (_renderers == null || _rendererOriginalEnabled == null) return;

            for (int i = 0; i < _renderers.Length && i < _rendererOriginalEnabled.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                bool baseEnabled = _rendererOriginalEnabled[i];
                r.enabled = hidden ? false : baseEnabled;
            }

            Debug.Log("[cardboxshield] ApplyRendererHidden(" + hidden + ")");
        }

        // ===================== 상자 온/오프 & 쿨타임 =====================

        private void EnterBox(string reason)
        {
            if (_playerMain == null)
            {
                if (!TryFindPlayer()) return;
            }

            _isBoxMode = true;
            _durability = MaxDurability;

            EnsureHideFieldsCached();
            CacheRenderers();
            CacheHealthField();

            if (_healthFieldReady)
            {
                _lastHealthValue = ReadHealthValue();
            }

            TrySetHidden(true);

            Debug.Log("[cardboxshield] EnterBox (" + reason + ") - 상자 모드 ON. 내구도 = " + _durability);
        }

        private void ExitBox(string reason)
        {
            if (!_isBoxMode) return;

            _isBoxMode = false;
            TrySetHidden(false);

            StartCooldown(reason);

            Debug.Log("[cardboxshield] ExitBox (" + reason + ") - 상자 모드 OFF.");
        }

        private void StartCooldown(string reason)
        {
            _onCooldown = true;
            _cooldownEndTime = Time.time + CooldownSeconds;
            Debug.Log("[cardboxshield] Cooldown 시작 (" + reason + ") " +
                      CooldownSeconds + "초");
        }

        // ===================== GUI =====================

        private void SetupStyles()
        {
            if (_styleReady) return;

            _barBgStyle = new GUIStyle(GUI.skin.box);
            _barBgStyle.normal.background = MakeTex(1, 1,
                new Color(0f, 0f, 0f, 0.6f));

            _barFillStyle = new GUIStyle(GUI.skin.box);
            _barFillStyle.normal.background = MakeTex(1, 1,
                new Color(0.2f, 0.9f, 0.2f, 0.9f));

            _styleReady = true;
            Debug.Log("[cardboxshield] SetupStyles 완료");
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;

            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            // 로드 확인 메시지 (조금 아래)
            if (Time.time <= _loadedMsgEndTime)
            {
                GUI.Label(
                    new Rect(10f, 40f, 400f, 25f),
                    "cardboxshield: Loaded (Press PageDown)");
            }

            // 쿨타임 남은 시간 표시
            if (_onCooldown)
            {
                float remain = _cooldownEndTime - Time.time;
                if (remain < 0f) remain = 0f;

                GUI.Label(
                    new Rect(10f, 85f, 400f, 25f),
                    "cardboxshield: Cooldown " + remain.ToString("0.0") + "s");
            }

            if (!_isBoxMode || _hudAnchor == null) return;

            SetupStyles();

            // 머리 위 바
            Vector3 worldPos = _hudAnchor.position + new Vector3(0f, 1.8f, 0f);
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0f) return;

            float barWidth = 80f;
            float barHeight = 10f;

            float x = screenPos.x - (barWidth * 0.5f);
            float y = Screen.height - screenPos.y - 40f;

            Rect bg = new Rect(x, y, barWidth, barHeight);
            GUI.Box(bg, GUIContent.none, _barBgStyle);

            float ratio = Mathf.Clamp01(_durability / MaxDurability);
            Rect fill = new Rect(x + 1f, y + 1f,
                (barWidth - 2f) * ratio, barHeight - 2f);
            GUI.Box(fill, GUIContent.none, _barFillStyle);
        }
    }
}
