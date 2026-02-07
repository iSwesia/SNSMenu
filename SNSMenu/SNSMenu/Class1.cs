using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using Il2CppInterop.Runtime.Injection;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SaikoMenu
{
    [BepInPlugin("com.saiko.menu", "SNS Menu", SaikoMod.MOD_VERSION)]
    public class SaikoMod : BepInEx.Unity.IL2CPP.BasePlugin
    {
        public const string MOD_VERSION = "1.0.1";

        public override void Load()
        {
            ClassInjector.RegisterTypeInIl2Cpp<MasterHandler>();

            GameObject go = new GameObject("SNSMenu");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MasterHandler>();
            UnityEngine.Object.DontDestroyOnLoad(go);

            // highlight
            Log.LogError("============================================");
            Log.LogWarning("                  SNS MENU                  ");
            Log.LogMessage("        SNS Menu Successfully Loaded        ");
            Log.LogWarning("                Version: " + MOD_VERSION);
            Log.LogError("                 by Swesia                  ");
            Log.LogError("============================================");
        }
    }

    public class MasterHandler : MonoBehaviour
    {
        public MasterHandler(IntPtr ptr) : base(ptr) { }

        private GameObject _cachedPlayer;
        private Component _cachedInteractManager;
        private GameObject _mouseLookObj;
        private GameObject _cachedEyeVignette;
        private Component _cachedBloodFX;
        private Light _cachedDirLight;

        private bool showStartupMsg = true;
        private bool forceUnlock = false;
        private bool disableVolumetrics = false;
        private bool disableMirrors = false;
        private bool wasVolumetricsDisabled = false;

        private bool noDarkness = false;
        private bool hasStoredDefaults = false;
        private Color defLightColor;
        private float defLightTemp;
        private float defCookieSize;
        private int defCullingMask;
        private float defIntensity;
        private bool defFogEnabled;
        private Color defAmbientLight;
        private float defFogDensity;
        private Color defCamColor;
        private CameraClearFlags defClearFlags;

        private GameObject currentTarget = null;
        private Component currentDynamicComp = null;
        private float maxInteractDistance = 4.0f;

        private bool showGUI = false;
        private int currentTab = 0;
        private string[] tabs = { "Player", "ESP", "Misc", "Fun", "Info", "Settings" };
        private Rect windowRect = new Rect(50, 50, 360, 380);

        private bool espBox = false, espName = false, espDist = false;
        private bool espKeys = false, espPages = false, espDiaries = false;

        private bool speedEnabled = false, wasSpeedEnabled = false;
        private float speedMultiplier = 1.0f;
        private const float DEFAULT_WALK_SPEED = 4.0f;
        private bool noHold = false;
        private float interactCooldown = 0f;
        private bool godMode = false;
        private bool shoesWorn = false;

        private bool rearView = false;
        private KeyCode rearKey = KeyCode.V;
        private bool isRebindingRear = false;
        private bool freezeSaiko = false, wasFrozen = false;
        private bool removeVignette = false, removeBlood = false;

        private KeyCode menuKey = KeyCode.F1;
        private bool isRebinding = false;
        private int selectedLang = 1;
        private string notificationMsg = "";
        private float notificationTimer = 0f;
        private const float maxNotifyTime = 5.0f;
        private string currentSafeCode = "xxxx";
        private string currentEnemyState = "Unknown";

        private List<GameObject> targets = new List<GameObject>();
        private List<GameObject> keyTargets = new List<GameObject>();
        private List<GameObject> pageTargets = new List<GameObject>();
        private List<GameObject> diaryTargets = new List<GameObject>();
        private List<GameObject> volumetricObjects = new List<GameObject>();
        private List<GameObject> mirrorObjects = new List<GameObject>();

        private readonly List<string> validSaikoNames = new List<string> { "yandere 2", "sane saiko", "bunny_saiko", "nightmare" };
        private int interactLayerIndex = -1;

        private float slowUpdateTimer = 0f;
        private const float SLOW_UPDATE_RATE = 2.5f;

        private Color colorBrown = new Color(0.6f, 0.3f, 0f, 1f);

        private Camera _rearCamera;
        private RenderTexture _rearTexture;
        private GameObject _rearCamObj;
        private const int MIRROR_WIDTH = 480;
        private const int MIRROR_HEIGHT = 270;
        private Texture2D _whiteTexture;
        private Texture2D WhiteTexture
        {
            get
            {
                if (_whiteTexture == null) _whiteTexture = Texture2D.whiteTexture;
                return _whiteTexture;
            }
        }

        void Start()
        {
            if (Application.systemLanguage == SystemLanguage.Turkish) selectedLang = 0; else selectedLang = 1;
            SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>)OnSceneLoaded);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResetCache();
            interactLayerIndex = LayerMask.NameToLayer("Interact");

            if (scene.name == "LevelNew")
            {
                RefreshTargets();
                currentSafeCode = "xxxx";
                currentEnemyState = "Unknown";
            }
        }

        void ResetCache()
        {
            _cachedPlayer = null; _cachedInteractManager = null; _mouseLookObj = null; _whiteTexture = null;
            _cachedEyeVignette = null; _cachedBloodFX = null; _cachedDirLight = null;
            currentTarget = null; currentDynamicComp = null;
            hasStoredDefaults = false;
            targets.Clear(); keyTargets.Clear(); pageTargets.Clear(); diaryTargets.Clear();
            volumetricObjects.Clear(); mirrorObjects.Clear();
            DestroyRearCamera();
            shoesWorn = false; freezeSaiko = false; wasFrozen = false;
            removeVignette = false; removeBlood = false;
            speedEnabled = false; wasSpeedEnabled = false;
            rearView = false; isRebindingRear = false;
            noHold = false; interactCooldown = 0f;
        }

        void Update()
        {
            if (showStartupMsg) return;

            if (Input.GetKeyDown(menuKey) && !isRebinding && !isRebindingRear) showGUI = !showGUI;
            if (!isRebinding && !isRebindingRear && Input.GetKeyDown(rearKey)) rearView = !rearView;

            if (notificationTimer > 0) notificationTimer -= Time.deltaTime;
            if (interactCooldown > 0) interactCooldown -= Time.deltaTime;

            if (SceneManager.GetActiveScene().name == "LevelNew")
            {
                slowUpdateTimer += Time.deltaTime;
                if (slowUpdateTimer >= SLOW_UPDATE_RATE)
                {
                    if (espBox || espName || espDist || espKeys || espPages || espDiaries || freezeSaiko || rearView)
                    {
                        RefreshTargets();
                    }
                    slowUpdateTimer = 0f;
                }

                ApplyHacks();
                UpdateTargetLogic();
                ApplyNoHold();
                UpdateSafeCode();
                UpdateEnemyState();
                HandleFreezeLogic();
                HandleVignette();
                HandleBloodFX();
                HandleRearView();
                HandleNoDarkness();
                HandlePerformance();
                HandleForceUnlock();
            }
        }

        void RefreshTargets()
        {
            targets.Clear();
            keyTargets.Clear();
            pageTargets.Clear();
            diaryTargets.Clear();
            volumetricObjects.Clear();
            mirrorObjects.Clear();

            var agents = GameObject.FindObjectsOfType<NavMeshAgent>();
            foreach (var agent in agents)
            {
                GameObject obj = agent.gameObject;
                if (validSaikoNames.Contains(obj.name))
                {
                    if (!targets.Contains(obj)) targets.Add(obj);
                }
            }

            var allColliders = GameObject.FindObjectsOfType<Collider>();

            foreach (var col in allColliders)
            {
                GameObject obj = col.gameObject;

                if (obj.layer != interactLayerIndex &&
                    !obj.name.ToLower().Contains("journal") &&
                    !obj.name.StartsWith("Mirror") &&
                    !obj.name.StartsWith("SHW"))
                    continue;

                if (!obj.activeInHierarchy) continue;

                CheckItem(obj);

                if (disableVolumetrics && obj.name.StartsWith("SHW_Add_effect"))
                {
                    if (!volumetricObjects.Contains(obj)) volumetricObjects.Add(obj);
                }

                if (disableMirrors && obj.name.StartsWith("Mirror"))
                {
                    if (!mirrorObjects.Contains(obj)) mirrorObjects.Add(obj);
                }
            }
        }

        void CheckItem(GameObject obj)
        {
            if (keyTargets.Contains(obj) || pageTargets.Contains(obj) || diaryTargets.Contains(obj)) return;

            string n = obj.name;
            string nLower = n.ToLower();

            if (espKeys)
            {
                bool isKey = (n.StartsWith("Door_Key") || n == "StorageRoomKey" || n == "InfirmaryKey" || n == "ExitDoorKey" || (n.StartsWith("Drop_") && n.EndsWith("_Key")));
                if (isKey) { keyTargets.Add(obj); return; }
            }

            if (espPages && n == "Page(Clone)") { pageTargets.Add(obj); return; }

            if (espDiaries)
            {
                if (nLower.Contains("journal") || nLower.Contains("diary"))
                {
                    diaryTargets.Add(obj);
                    return;
                }
            }
        }

        void UnlockNightmareDifficulty()
        {
            GameObject btnObj = GameObject.Find("MainMenu/Canvas/ChooseDifficulty/Practice (1)");
            if (btnObj != null)
            {
                var btn = btnObj.GetComponent<Button>();
                if (btn != null) btn.interactable = true;

                var txt = btnObj.GetComponentInChildren<Text>();
                if (txt != null) txt.text = (selectedLang == 0) ? "OYNA" : "PLAY";

                notificationMsg = (selectedLang == 0) ? "Kabus Modu Açıldı!" : "Nightmare Unlocked!";
                notificationTimer = maxNotifyTime;
            }
            else
            {
                notificationMsg = (selectedLang == 0) ? "Buton Bulunamadı!" : "Button not found!";
                notificationTimer = 2.0f;
            }
        }

        void HandleForceUnlock()
        {
            if (!forceUnlock) return;
            if (currentDynamicComp == null) return;
            bool isLocked = GetBoolValue(currentDynamicComp, "isLocked");
            if (isLocked)
            {
                SetBoolValue(currentDynamicComp, "isLocked", false);
                SetIntValue(currentDynamicComp, "keyID", 0);
                SetBoolValue(currentDynamicComp, "FoundLocked", false);
            }
        }

        void HandlePerformance()
        {
            if (disableMirrors)
            {
                foreach (var mirror in mirrorObjects) { if (mirror != null && mirror.activeSelf) mirror.SetActive(false); }
            }

            if (disableVolumetrics)
            {
                foreach (var vol in volumetricObjects) { if (vol != null && vol.activeSelf) vol.SetActive(false); }
                wasVolumetricsDisabled = true;
            }
            else if (wasVolumetricsDisabled)
            {
                foreach (var vol in volumetricObjects) { if (vol != null && !vol.activeSelf) vol.SetActive(true); }
                wasVolumetricsDisabled = false;
            }
        }

        void HandleNoDarkness()
        {
            if (_cachedDirLight == null)
            {
                GameObject lightObj = GameObject.Find("Directional Light");
                if (lightObj != null) _cachedDirLight = lightObj.GetComponent<Light>();
            }
            if (_cachedDirLight == null) return;

            if (noDarkness)
            {
                if (!hasStoredDefaults)
                {
                    if (_cachedDirLight != null)
                    {
                        defLightColor = _cachedDirLight.color;
                        defLightTemp = _cachedDirLight.colorTemperature;
                        defCookieSize = _cachedDirLight.cookieSize;
                        defCullingMask = _cachedDirLight.cullingMask;
                        defIntensity = _cachedDirLight.intensity;
                    }
                    defFogEnabled = RenderSettings.fog;
                    defFogDensity = RenderSettings.fogDensity;
                    defAmbientLight = RenderSettings.ambientLight;
                    if (Camera.main != null)
                    {
                        defCamColor = Camera.main.backgroundColor;
                        defClearFlags = Camera.main.clearFlags;
                    }
                    hasStoredDefaults = true;
                }
                if (_cachedDirLight != null)
                {
                    _cachedDirLight.color = Color.white;
                    _cachedDirLight.colorTemperature = 1f;
                    _cachedDirLight.cookieSize = 1f;
                    _cachedDirLight.cullingMask = -1;
                    _cachedDirLight.intensity = 1f;
                }
                RenderSettings.fog = false;
                RenderSettings.ambientLight = Color.white;
                if (Camera.main != null)
                {
                    Camera.main.backgroundColor = new Color(0.95f, 0.92f, 0.75f, 1f);
                    Camera.main.clearFlags = CameraClearFlags.SolidColor;
                }
            }
            else if (hasStoredDefaults)
            {
                if (_cachedDirLight != null)
                {
                    _cachedDirLight.color = defLightColor;
                    _cachedDirLight.colorTemperature = defLightTemp;
                    _cachedDirLight.cookieSize = defCookieSize;
                    _cachedDirLight.cullingMask = defCullingMask;
                    _cachedDirLight.intensity = defIntensity;
                }
                RenderSettings.fog = defFogEnabled;
                RenderSettings.fogDensity = defFogDensity;
                RenderSettings.ambientLight = defAmbientLight;
                if (Camera.main != null)
                {
                    Camera.main.backgroundColor = defCamColor;
                    Camera.main.clearFlags = defClearFlags;
                }
                hasStoredDefaults = false;
            }
        }

        void UpdateTargetLogic()
        {
            currentTarget = null; currentDynamicComp = null;
            if (_cachedInteractManager == null)
            {
                if (_mouseLookObj == null) _mouseLookObj = GameObject.Find("MouseLook");
                if (_mouseLookObj != null) _cachedInteractManager = _mouseLookObj.GetComponent("InteractManager");
                if (_cachedInteractManager == null) return;
            }
            GameObject found = null;
            var rayObj = GetValue(_cachedInteractManager, "RaycastObject") as Il2CppSystem.Object;
            if (rayObj != null) { var go = rayObj.TryCast<GameObject>(); if (go != null && IsInRange(go)) found = go; }
            if (found == null && Camera.main != null)
            {
                Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
                RaycastHit hit;
                int mask = (interactLayerIndex != -1) ? (1 << interactLayerIndex) : -5;

                if (Physics.Raycast(ray, out hit, maxInteractDistance, mask))
                {
                    GameObject hitObj = hit.collider.gameObject;
                    if (hitObj.tag == "Interact" || hitObj.tag == "DynamicObject" || hitObj.layer == interactLayerIndex) found = hitObj;
                    else { var dyn = hitObj.GetComponent("DynamicObject"); if (dyn != null) found = hitObj; }
                }
            }
            if (found != null)
            {
                var dynObj = GetValue(_cachedInteractManager, "dynamic") as Il2CppSystem.Object;
                if (dynObj != null) { var d = dynObj.TryCast<Component>(); if (d != null && d.gameObject == found) currentDynamicComp = d; }
                if (currentDynamicComp == null)
                {
                    currentDynamicComp = found.GetComponent("DynamicObject");
                    if (currentDynamicComp == null && found.transform.parent != null)
                        currentDynamicComp = found.transform.parent.GetComponent("DynamicObject");
                }
                currentTarget = found;
            }
        }

        bool IsInRange(GameObject target)
        {
            if (target == null || Camera.main == null) return false;
            return Vector3.Distance(Camera.main.transform.position, target.transform.position) <= maxInteractDistance;
        }

        void ApplyNoHold()
        {
            if (!noHold) return; if (interactCooldown > 0) return; if (_cachedInteractManager == null) return;
            bool ePressed = Input.GetKey(KeyCode.E); bool qPressed = Input.GetKey(KeyCode.Q);
            if (ePressed)
            {
                if (currentTarget != null && IsInRange(currentTarget))
                {
                    SetFieldValue(_cachedInteractManager, "loadValue", 10.0f);
                    CallMethodAggressive(_cachedInteractManager, "Interact", currentTarget);
                    if (currentDynamicComp != null) CallMethodNoParam(currentDynamicComp, "Use");
                    interactCooldown = 0.15f;
                }
            }
            if (qPressed)
            {
                bool canKick = GetBoolValue(_cachedInteractManager, "canKickDoor");
                if (canKick) return;
                else
                {
                    if (currentTarget != null && IsInRange(currentTarget))
                    {
                        SetFieldValue(_cachedInteractManager, "loadValue", 10.0f);
                        CallMethodAggressive(_cachedInteractManager, "LockUnlockDoor", currentTarget);
                        interactCooldown = 0.15f;
                    }
                }
            }
        }

        void CallMethodAggressive(Component c, string methodName, GameObject param)
        {
            try
            {
                var methods = c.GetIl2CppType().GetMethods((Il2CppSystem.Reflection.BindingFlags)0xFFFF);
                foreach (var method in methods)
                {
                    if (method.Name == methodName && method.GetParameters().Length == 1)
                    {
                        var args = new Il2CppReferenceArray<Il2CppSystem.Object>(1);
                        args[0] = param;
                        method.Invoke(c, args);
                        return;
                    }
                }
            }
            catch { }
        }

        void CallMethodNoParam(Component c, string methodName)
        {
            try
            {
                var method = c.GetIl2CppType().GetMethod(methodName, (Il2CppSystem.Reflection.BindingFlags)0xFFFF);
                if (method != null && method.GetParameters().Length == 0) method.Invoke(c, null);
            }
            catch { }
        }

        bool GetBoolValue(Component c, string f)
        {
            try
            {
                var field = c.GetIl2CppType().GetField(f, (Il2CppSystem.Reflection.BindingFlags)62);
                if (field != null)
                {
                    var val = field.GetValue(c);
                    if (val != null) return val.Unbox<bool>();
                }
            }
            catch { }
            return false;
        }

        Il2CppSystem.Object GetValue(Component c, string f)
        {
            try
            {
                var field = c.GetIl2CppType().GetField(f, (Il2CppSystem.Reflection.BindingFlags)62);
                if (field != null) return field.GetValue(c);
            }
            catch { }
            return null;
        }

        void SetFieldValue(Component c, string f, float v)
        {
            try { c.GetIl2CppType().GetField(f, (Il2CppSystem.Reflection.BindingFlags)62).SetValue(c, new Il2CppSystem.Single { m_value = v }.BoxIl2CppObject()); } catch { }
        }
        void SetBoolValue(Component c, string f, bool v)
        {
            try { c.GetIl2CppType().GetField(f, (Il2CppSystem.Reflection.BindingFlags)62).SetValue(c, new Il2CppSystem.Boolean { m_value = v }.BoxIl2CppObject()); } catch { }
        }
        void SetIntValue(Component c, string f, int v)
        {
            try { c.GetIl2CppType().GetField(f, (Il2CppSystem.Reflection.BindingFlags)62).SetValue(c, new Il2CppSystem.Int32 { m_value = v }.BoxIl2CppObject()); } catch { }
        }

        void OnGUI()
        {
            GUI.skin.label.richText = true;

            if (showStartupMsg)
            {
                GUI.color = Color.white;
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
                float boxW = 400, boxH = 200;
                Rect msgRect = new Rect((Screen.width - boxW) / 2, (Screen.height - boxH) / 2, boxW, boxH);
                GUI.Window(99, msgRect, (Action<int>)DrawStartupModal, $"SNS Menu v{SaikoMod.MOD_VERSION}");
                return;
            }

            if (rearView && _rearTexture != null)
            {
                GUI.color = Color.white;
                float mx = (Screen.width - MIRROR_WIDTH) / 2;
                float my = 10;
                GUI.Box(new Rect(mx - 2, my - 2, MIRROR_WIDTH + 4, MIRROR_HEIGHT + 4), "");
                GUI.DrawTexture(new Rect(mx, my, MIRROR_WIDTH, MIRROR_HEIGHT), _rearTexture);
            }

            if (SceneManager.GetActiveScene().name == "LevelNew")
            {
                if (espBox || espName || espDist) { GUI.depth = 2; DrawESP(); }
                if (espKeys) { GUI.depth = 2; DrawKeyESP(); }
                if (espPages) { GUI.depth = 2; DrawPageESP(); }
                if (espDiaries) { GUI.depth = 2; DrawDiaryESP(); }
            }

            GUI.depth = 0;
            if (showGUI)
            {
                GUI.color = Color.white;
                windowRect = GUI.Window(0, windowRect, (Action<int>)DrawMenu, $"SNS Menu v{SaikoMod.MOD_VERSION}");
            }

            if (notificationTimer > 0 && !string.IsNullOrEmpty(notificationMsg))
            {
                float alpha = notificationTimer > 1.0f ? 1f : notificationTimer;
                GUI.skin.label.fontSize = 22;
                GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                Rect subRect = new Rect(0, Screen.height - 180, Screen.width, 100);
                GUI.color = new Color(0, 0, 0, alpha * 0.8f);
                GUI.Label(new Rect(subRect.x + 1.5f, subRect.y + 1.5f, subRect.width, subRect.height), "<b>" + notificationMsg + "</b>");
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.Label(subRect, "<b>" + notificationMsg + "</b>");
                GUI.color = Color.white;
            }
        }

        void DrawStartupModal(int id)
        {
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;
            GUI.skin.label.fontSize = 16;
            string msg = selectedLang == 0 ?
                $"\nSNS Menu v{SaikoMod.MOD_VERSION} Yüklendi!\n\nMenüyü aktif etmek için F1 tuşuna basın.\n(Tuşu menüden değiştirebilirsiniz)" :
                $"\nSNS Menu v{SaikoMod.MOD_VERSION} Loaded!\n\nPress F1 to open GUI.\n(You can change keybind in menu)";

            string btnText = selectedLang == 0 ? "TAMAM" : "OK";
            GUILayout.Label(msg);
            GUILayout.Space(20);
            if (GUILayout.Button(btnText, GUILayout.Height(30))) showStartupMsg = false;
        }

        void DrawMenu(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            GUILayout.BeginArea(new Rect(10, 25, windowRect.width - 20, windowRect.height - 55));
            GUI.skin.label.fontSize = 14;
            GUI.skin.label.alignment = TextAnchor.UpperLeft;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Toggle(currentTab == i, tabs[i], "Button")) currentTab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            bool isTR = selectedLang == 0;
            switch (currentTab)
            {
                case 0: // PLAYER
                    speedEnabled = GUILayout.Toggle(speedEnabled, isTR ? " Oyuncu Hızı" : " Player Speed");
                    if (speedEnabled || wasSpeedEnabled)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("-", GUILayout.Width(30)))
                        {
                            speedMultiplier -= 0.5f;
                            if (speedMultiplier < 1f) speedMultiplier = 1f;
                        }
                        GUILayout.Box(speedMultiplier.ToString("F1") + "x", GUILayout.Width(60));
                        if (GUILayout.Button("+", GUILayout.Width(30))) speedMultiplier += 0.5f;
                        GUILayout.EndHorizontal();
                    }
                    godMode = GUILayout.Toggle(godMode, isTR ? " Ölümsüzlük" : " God Mode");
                    noHold = GUILayout.Toggle(noHold, isTR ? " Basılı Tutma Yok" : " No KeyHold");
                    GUILayout.BeginHorizontal();
                    rearView = GUILayout.Toggle(rearView, isTR ? " Dikiz Aynası" : " Back View Cam");
                    if (rearView)
                    {
                        if (GUILayout.Button(isRebindingRear ? "..." : "[" + rearKey + "]", GUILayout.Width(80))) isRebindingRear = true;
                    }
                    GUILayout.EndHorizontal();
                    if (isRebindingRear)
                    {
                        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                        {
                            if (Input.GetKeyDown(k) && k != KeyCode.Mouse0) { rearKey = k; isRebindingRear = false; break; }
                        }
                    }
                    break;
                case 1: // ESP
                    GUILayout.Label("<b>Saiko / Yandere</b>");
                    espBox = GUILayout.Toggle(espBox, isTR ? " Kutu" : " Box ESP");
                    espName = GUILayout.Toggle(espName, isTR ? " İsim" : " Name ESP");
                    espDist = GUILayout.Toggle(espDist, isTR ? " Mesafe" : " Distance ESP");
                    GUILayout.Space(10);
                    GUILayout.Label("<b>" + (isTR ? "Eşyalar" : "Items") + "</b>");
                    espKeys = GUILayout.Toggle(espKeys, isTR ? " Anahtarları Göster" : " Keys ESP");
                    espPages = GUILayout.Toggle(espPages, isTR ? " Sayfaları Göster" : " Pages ESP");
                    espDiaries = GUILayout.Toggle(espDiaries, isTR ? " Günlüğü Göster" : " Diary ESP");
                    break;
                case 2: // MISC
                    freezeSaiko = GUILayout.Toggle(freezeSaiko, isTR ? " Saiko'yu Dondur" : " Freeze Saiko");
                    removeVignette = GUILayout.Toggle(removeVignette, isTR ? " Karartmayı Kaldır" : " Remove Vignette");
                    removeBlood = GUILayout.Toggle(removeBlood, isTR ? " Kan Efektini Kaldır" : " Remove BloodFX");
                    noDarkness = GUILayout.Toggle(noDarkness, isTR ? " Karanlığı Kapat" : " No Darkness");
                    forceUnlock = GUILayout.Toggle(forceUnlock, isTR ? " Kilidi Zorla" : " Force Unlock");
                    GUILayout.Space(5);
                    if (GUILayout.Button(isTR ? "Ayakkabıları Giy" : "Equip Shoes", GUILayout.Height(30))) UnlockShoes();
                    GUILayout.Space(5);
                    if (GUILayout.Button(isTR ? "Kabus Modunu Aç (Ana Menü)" : "Unlock Nightmare (Main Menu)", GUILayout.Height(30))) UnlockNightmareDifficulty();
                    break;
                case 3: // FUN
                    GUILayout.Space(100);
                    GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                    GUILayout.Label(isTR ? "<b>Çok yakında...</b>" : "<b>Soon...</b>");
                    GUI.skin.label.alignment = TextAnchor.UpperLeft;
                    break;
                case 4: // INFO
                    GUI.color = Color.yellow; GUILayout.Label("<b>" + (isTR ? "Kasa Şifresi" : "Safe Code") + ": " + currentSafeCode + "</b>");
                    GUI.color = Color.white; GUILayout.Label("----------------");
                    string stateTitle = isTR ? "Saiko / Yandere durumu" : "Saiko / Yandere state";
                    GUILayout.Label($"<b><color=red>{stateTitle}: </color><color=white>{currentEnemyState}</color></b>");
                    break;
                case 5: // SETTINGS
                    GUI.color = Color.white;
                    GUILayout.Label(isTR ? "Menü Tuşu:" : "Menu Key:");
                    if (GUILayout.Button(isRebinding ? "..." : menuKey.ToString())) isRebinding = true;
                    if (isRebinding)
                    {
                        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                        {
                            if (Input.GetKeyDown(k) && k != KeyCode.Mouse0) { menuKey = k; isRebinding = false; break; }
                        }
                    }
                    GUILayout.Space(10);
                    GUILayout.Label(isTR ? "Dil / Language:" : "Language / Dil:");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Toggle(selectedLang == 0, "TR", "Button")) selectedLang = 0;
                    if (GUILayout.Toggle(selectedLang == 1, "EN", "Button")) selectedLang = 1;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(15);
                    GUILayout.Label(isTR ? "<b>Performans Ayarları</b>" : "<b>Performance Settings</b>");
                    disableVolumetrics = GUILayout.Toggle(disableVolumetrics, isTR ? " Işık Süzmelerini Kaldır" : " Disable Volumetrics");
                    disableMirrors = GUILayout.Toggle(disableMirrors, isTR ? " Aynaları Kaldır" : " Disable Mirrors");
                    break;
            }
            GUILayout.EndArea();
            Rect footerRect = new Rect(0, windowRect.height - 30, windowRect.width, 30);
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            GUI.Label(footerRect, "Made by Swêsia");
            GUI.color = Color.white;
            GUI.skin.label.alignment = TextAnchor.UpperLeft;
        }

        void DrawBoxOutline(Rect rect, Color color, float thickness = 1f)
        {
            Texture2D tex = WhiteTexture;
            if (tex == null) return;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), tex);
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), tex);
        }

        void DrawESP()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (var t in targets)
            {
                if (t == null || !t.activeInHierarchy) continue;
                Vector3 w2s = cam.WorldToScreenPoint(t.transform.position);
                if (w2s.z > 0)
                {
                    float h = Mathf.Abs(w2s.y - cam.WorldToScreenPoint(t.transform.position + new Vector3(0, 2.3f, 0)).y);
                    if (espBox) DrawBoxOutline(new Rect(w2s.x - (h / 3), Screen.height - w2s.y - h, h / 1.5f, h), Color.red, 2f);
                    string info = (espName ? t.name : "") + (espDist ? " [" + (int)Vector3.Distance(cam.transform.position, t.transform.position) + "m]" : "");
                    if (!string.IsNullOrEmpty(info))
                    {
                        GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                        GUI.color = Color.red;
                        GUI.Label(new Rect(w2s.x - 100, Screen.height - w2s.y - h - 30, 200, 30), "<b>" + info + "</b>");
                    }
                }
            }
            GUI.color = Color.white;
        }

        void DrawKeyESP()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            for (int i = keyTargets.Count - 1; i >= 0; i--)
            {
                var k = keyTargets[i];
                if (k == null) { keyTargets.RemoveAt(i); continue; }
                if (!IsVisiblyActive(k)) continue;
                Vector3 w2s = cam.WorldToScreenPoint(k.transform.position);
                if (w2s.z > 0)
                {
                    string n = GetSmartName(k);
                    string d = " [" + (int)Vector3.Distance(cam.transform.position, k.transform.position) + "m]";
                    GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                    GUI.color = Color.cyan;
                    GUI.Label(new Rect(w2s.x - 50, Screen.height - w2s.y - 20, 100, 40), "<b>" + n + d + "</b>");
                }
            }
            GUI.color = Color.white;
        }

        void DrawPageESP()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            for (int i = pageTargets.Count - 1; i >= 0; i--)
            {
                var p = pageTargets[i];
                if (p == null) { pageTargets.RemoveAt(i); continue; }
                if (!IsVisiblyActive(p)) continue;
                Vector3 w2s = cam.WorldToScreenPoint(p.transform.position);
                if (w2s.z > 0)
                {
                    string d = " [" + (int)Vector3.Distance(cam.transform.position, p.transform.position) + "m]";
                    GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                    GUI.color = Color.green;
                    GUI.Label(new Rect(w2s.x - 50, Screen.height - w2s.y - 20, 100, 40), "<b>Page" + d + "</b>");
                }
            }
            GUI.color = Color.white;
        }

        void DrawDiaryESP()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            for (int i = diaryTargets.Count - 1; i >= 0; i--)
            {
                var d = diaryTargets[i];
                if (d == null) { diaryTargets.RemoveAt(i); continue; }
                Vector3 w2s = cam.WorldToScreenPoint(d.transform.position);
                if (w2s.z > 0)
                {
                    string n = GetSmartName(d);
                    string dist = " [" + (int)Vector3.Distance(cam.transform.position, d.transform.position) + "m]";
                    GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                    GUI.color = colorBrown;
                    GUI.Label(new Rect(w2s.x - 50, Screen.height - w2s.y - 20, 100, 40), "<b>" + n + dist + "</b>");
                }
            }
            GUI.color = Color.white;
        }

        string GetSmartName(GameObject obj)
        {
            try
            {
                foreach (var c in obj.GetComponentsInChildren<Component>(false))
                {
                    string typeName = c.GetIl2CppType().Name;
                    if (typeName.Contains("TextMeshPro") || typeName.Contains("TMP_Text"))
                    {
                        var prop = c.GetIl2CppType().GetProperty("text");
                        if (prop != null)
                        {
                            var val = prop.GetGetMethod().Invoke(c, null);
                            if (val != null) return val.ToString();
                        }
                        var field = c.GetIl2CppType().GetField("m_text", (Il2CppSystem.Reflection.BindingFlags)62);
                        if (field != null)
                        {
                            var val = field.GetValue(c);
                            if (val != null) return val.ToString();
                        }
                    }
                }
            }
            catch { }
            return obj.name.Replace("(Clone)", "").Replace("Door_", "").Replace("Drop_", "").Replace("Key", "").Replace("journal", "Diary").Trim();
        }

        bool IsVisiblyActive(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy) return false;
            foreach (var t in obj.GetComponentsInChildren<Transform>(false))
            {
                if (t.gameObject.activeSelf)
                {
                    var r = t.GetComponent<Renderer>();
                    if (r != null && r.enabled) return true;
                    try
                    {
                        var comps = t.GetComponents<Component>();
                        foreach (var c in comps)
                            if (c.GetIl2CppType().Name.Contains("TextMeshPro")) return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        void UpdateSafeCode()
        {
            if (!showGUI && currentSafeCode != "xxxx") return;
            try
            {
                var allMonos = GameObject.FindObjectsOfType<MonoBehaviour>();
                foreach (var mono in allMonos)
                {
                    if (mono.GetIl2CppType().Name == "Keypad")
                    {
                        var val = GetValue(mono, "AccessCode");
                        if (val != null) currentSafeCode = val.Unbox<int>().ToString();
                        break;
                    }
                }
            }
            catch { }
        }

        void UpdateEnemyState()
        {
            if (!showGUI) return;
            currentEnemyState = selectedLang == 0 ? "Bilinmiyor" : "Unknown";
            foreach (var t in targets)
            {
                if (t == null) continue;
                var ai = t.GetComponent("YandereAI");
                if (ai != null)
                {
                    try
                    {
                        var val = GetValue(ai, "currentState");
                        if (val != null) currentEnemyState = val.ToString();
                    }
                    catch { }
                    break;
                }
            }
        }

        void HandleRearView()
        {
            if (rearView)
            {
                if (Camera.main == null) return;
                if (_rearCamera == null)
                {
                    _rearCamObj = new GameObject("RearCamera");
                    _rearCamera = _rearCamObj.AddComponent<Camera>();
                    _rearCamera.CopyFrom(Camera.main);
                    _rearCamera.depth = -10;
                    _rearCamera.nearClipPlane = 0.3f;
                    _rearTexture = new RenderTexture(MIRROR_WIDTH, MIRROR_HEIGHT, 16, RenderTextureFormat.ARGB32);
                    _rearCamera.targetTexture = _rearTexture;
                }
                Transform mainT = Camera.main.transform;
                _rearCamObj.transform.rotation = Quaternion.Euler(0, mainT.eulerAngles.y + 180f, 0);
                _rearCamObj.transform.position = mainT.position;
                _rearCamObj.transform.Translate(0, 0.2f, -0.5f, Space.Self);
            }
            else
            {
                DestroyRearCamera();
            }
        }

        void DestroyRearCamera()
        {
            if (_rearCamera != null)
            {
                _rearCamera.targetTexture = null;
                UnityEngine.Object.Destroy(_rearCamObj);
                _rearCamera = null;
            }
            if (_rearTexture != null)
            {
                _rearTexture.Release();
                UnityEngine.Object.Destroy(_rearTexture);
                _rearTexture = null;
            }
        }

        void ApplyHacks()
        {
            if (_cachedPlayer == null) _cachedPlayer = GameObject.Find("FPSPLAYER");
            if (_cachedPlayer == null) return;
            var controller = _cachedPlayer.GetComponent("PlayerController");
            if (controller != null)
            {
                if (speedEnabled) SetFieldValue(controller, "walkSpeed", DEFAULT_WALK_SPEED * speedMultiplier);
                else if (wasSpeedEnabled) SetFieldValue(controller, "walkSpeed", DEFAULT_WALK_SPEED);
            }
            wasSpeedEnabled = speedEnabled;
            if (godMode)
            {
                var healthComp = _cachedPlayer.GetComponent("HealthManager");
                if (healthComp != null)
                {
                    SetFieldValue(healthComp, "Health", 100f);
                    SetBoolValue(healthComp, "isDead", false);
                }
            }
        }

        void HandleFreezeLogic()
        {
            bool stateChanged = (freezeSaiko != wasFrozen);
            if (stateChanged)
            {
                foreach (var t in targets)
                {
                    if (t == null) continue;
                    var ctrl = t.GetComponent("YandereController").TryCast<Behaviour>();
                    if (ctrl != null) ctrl.enabled = !freezeSaiko;
                    var ai = t.GetComponent("YandereAI").TryCast<Behaviour>();
                    if (ai != null) ai.enabled = !freezeSaiko;
                    var agent = t.GetComponent("UnityEngine.AI.NavMeshAgent");
                    if (agent == null) agent = t.GetComponent("NavMeshAgent");
                    if (agent != null)
                    {
                        var agentBeh = agent.TryCast<Behaviour>();
                        if (agentBeh != null) agentBeh.enabled = !freezeSaiko;
                    }
                }
            }
            wasFrozen = freezeSaiko;
        }

        void HandleBloodFX()
        {
            if (_cachedBloodFX == null && _cachedPlayer != null)
            {
                var camTrans = _cachedPlayer.transform.Find("CameraAnimations/MouseLook/Kickback/MainCamera");
                if (camTrans != null)
                    foreach (var c in camTrans.GetComponents<Component>())
                        if (c.GetIl2CppType().Name.Contains("CameraBloodEffect"))
                        {
                            _cachedBloodFX = c;
                            break;
                        }
            }
            if (_cachedBloodFX != null)
            {
                var beh = _cachedBloodFX.TryCast<Behaviour>();
                if (beh != null) beh.enabled = !removeBlood;
            }
        }

        void HandleVignette()
        {
            if (_cachedEyeVignette == null)
            {
                var gm = GameObject.Find("GAMEMANAGER");
                if (gm != null)
                {
                    var eyeTrans = gm.transform.Find("Canvas/UI/Eye");
                    if (eyeTrans != null) _cachedEyeVignette = eyeTrans.gameObject;
                }
            }
            if (_cachedEyeVignette != null) _cachedEyeVignette.SetActive(!removeVignette);
        }

        void UnlockShoes()
        {
            GameObject worldShoes = GameObject.Find("Shoes");
            if (worldShoes != null) worldShoes.SetActive(false);
            if (_cachedPlayer == null) _cachedPlayer = GameObject.Find("FPSPLAYER");
            if (_cachedPlayer != null)
            {
                Transform boy5 = _cachedPlayer.transform.Find("boy 5");
                if (boy5 != null)
                {
                    Transform ls = boy5.Find("left shoes");
                    if (ls != null) ls.gameObject.SetActive(true);
                    Transform rs = boy5.Find("right shoes");
                    if (rs != null) rs.gameObject.SetActive(true);
                }
                var controller = _cachedPlayer.GetComponent("PlayerController");
                if (controller != null) SetBoolValue(controller, "hasShoes", true);
            }
            if (!shoesWorn)
            {
                notificationMsg = selectedLang == 0 ? "Ayakkabılar giyildi." : "Shoes equipped.";
                notificationTimer = maxNotifyTime;
                shoesWorn = true;
            }
        }
    }
}