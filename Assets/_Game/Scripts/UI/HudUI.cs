using System.Collections.Generic;
using System.Text;
using Transity.Audio;
using Transity.Combat;
using Transity.Core;
using Transity.Creatures;
using Transity.Interaction;
using Transity.Inventory;
using Transity.Missions;
using Transity.Networking;
using Transity.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Transity.UI
{
    /// <summary>
    /// The in-game overlay. The few widgets the scaffold wires (prompt, message, session
    /// code, phase, crosshair) are kept; everything else is built here at runtime so a
    /// change to the HUD is a change to one file.
    ///
    /// Reads only replicated or owner-local state. Nothing on the HUD is an authority on
    /// anything: it shows what the server said and what the owner is pressing.
    /// </summary>
    public sealed class HudUI : MonoBehaviour
    {
        [SerializeField] Text promptLabel;
        [SerializeField] Text sessionLabel;
        [SerializeField] Text phaseLabel;
        [SerializeField] Text messageLabel;
        [SerializeField] Image crosshair;
        [SerializeField] Image promptPlate;
        [SerializeField] Image messagePlate;
        [SerializeField] float messageDuration = 3f;
        [SerializeField] float fadeSpeed = 12f;

        const int HotbarSlots = 5;

        static readonly Color Cream = new(0.93f, 0.9f, 0.82f);
        static readonly Color Dim = new(0.65f, 0.66f, 0.68f);
        static readonly Color Amber = new(0.95f, 0.78f, 0.45f);
        static readonly Color Blood = new(0.85f, 0.15f, 0.12f);
        static readonly Color Plate = new(0f, 0f, 0f, 0.5f);

        float m_PromptAlpha;
        float m_MessageAlpha;
        float m_MessageClearAt;

        // ---- bound player ----
        PlayerVitals m_Vitals;
        Interactor m_Interactor;
        PlayerFeedback m_Feedback;
        FirstPersonController m_Movement;
        InventoryComponent m_Inventory;
        PlayerEquipment m_Equipment;
        PlayerCharacter m_Character;
        SpectatorController m_Spectator;

        // ---- built widgets ----
        RectTransform m_Root;
        Image m_HealthFill;
        Image m_HealthBack;
        Image m_StaminaFill;
        Text m_BleedLabel;
        Text m_NameLabel;
        readonly List<HotbarSlot> m_Hotbar = new();
        Text m_HeldName;
        Text m_HeldAmmo;
        Text m_HeldHint;
        Image m_UseFill;
        RectTransform m_UseBar;
        Image m_Vignette;
        Image m_TintOverlay;
        readonly Image[] m_DamageArcs = new Image[8];
        readonly float[] m_DamageArcHeat = new float[8];
        readonly Image[] m_HitLines = new Image[4];
        float m_HitMarkerHeat;
        Color m_HitMarkerColor = Color.white;
        RectTransform m_CompassStrip;
        readonly List<CompassMarker> m_CompassMarkers = new();
        readonly List<Image> m_ThermalMarkers = new();
        RectTransform m_TeamList;
        readonly List<TeamRow> m_TeamRows = new();
        Text m_Objective;
        Text m_Creatures;
        Text m_Wallet;
        RectTransform m_BriefPanel;
        Text m_BriefTitle;
        Text m_BriefBody;
        float m_BriefHideAt;
        RectTransform m_DeathPanel;
        Text m_DeathTitle;
        Text m_DeathBody;
        RectTransform m_DebriefPanel;
        Text m_DebriefBody;
        Button m_DebriefContinue;
        RectTransform m_LetterPanel;
        Text m_LetterBody;
        RectTransform m_Scoreboard;
        Text m_ScoreboardBody;
        Text m_PrivateNote;
        float m_PrivateNoteHideAt;
        CollectorApparition m_Apparition;
        Transform m_Extraction;
        readonly List<(Vector3 position, float until)> m_AlarmPings = new();
        Sprite m_Circle;
        Sprite m_Ring;
        Sprite m_Radial;
        bool m_PanelSuppressing;
        MissionPhase m_LastPhase = MissionPhase.Preparing;
        float m_HeartbeatPulse;
        float m_ShakeTime;
        float m_ShakeStrength;
        Vector2 m_RootRest;

        sealed class HotbarSlot
        {
            public RectTransform Rect;
            public Image Frame;
            public Image Icon;
            public Text Initials;
            public Text State;
            public Text Number;
        }

        sealed class CompassMarker
        {
            public Image Image;
            public Text Label;
        }

        sealed class TeamRow
        {
            public RectTransform Rect;
            public Text Name;
            public Image Fill;
            public Image Back;
        }

        // ================================================================== lifecycle

        void Awake()
        {
            // Everything built here goes inside a container rather than straight onto the
            // canvas, because a Canvas drives its own RectTransform every frame -- nudging
            // it for the damage shake would be silently overwritten. The widgets the
            // scaffold wired stay on the canvas itself and deliberately do not shake.
            var container = new GameObject("HudContent", typeof(RectTransform));
            m_Root = (RectTransform)container.transform;
            m_Root.SetParent(transform, false);
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.one;
            m_Root.offsetMin = Vector2.zero;
            m_Root.offsetMax = Vector2.zero;
            m_Root.SetAsFirstSibling();

            // The UV trail renderer is lazily created; touching it here makes sure it is
            // ticking from the first frame rather than the first time someone asks.
            _ = ScentTrailSystem.Instance;

            BuildSprites();
            BuildVignette();
            BuildVitals();
            BuildHotbar();
            BuildHeldPanel();
            BuildUseBar();
            BuildDamageArcs();
            BuildHitMarker();
            BuildCompass();
            BuildTeamList();
            BuildTopRight();
            BuildBrief();
            BuildDeathPanel();
            BuildDebrief();
            BuildLetter();
            BuildScoreboard();
            BuildThermalMarkers();
            BuildPrivateNote();
        }

        void OnEnable()
        {
            MotionAlarm.Triggered += HandleAlarm;
            CollectorContract.OfferReceived += HandleOffer;
            CollectorContract.OfferClosed += HandleOfferClosed;
            CollectorContract.PrivateNote += HandlePrivateNote;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            MotionAlarm.Triggered -= HandleAlarm;
            CollectorContract.OfferReceived -= HandleOffer;
            CollectorContract.OfferClosed -= HandleOfferClosed;
            CollectorContract.PrivateNote -= HandlePrivateNote;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
            Unbind();
        }

        void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            m_Extraction = null;
            m_AlarmPings.Clear();
        }

        void Update()
        {
            BindLocalPlayerIfNeeded();
            RefreshSessionLabel();
            RefreshPhaseAndObjective();
            ExpireMessage();
            HandleCursorToggle();
            UpdateFades();
            UpdateVitals();
            UpdateHotbar();
            UpdateHeldPanel();
            UpdateUseBar();
            UpdateCrosshair();
            UpdateDamageArcs();
            UpdateHitMarker();
            UpdateVignette();
            UpdateCompass();
            UpdateThermal();
            UpdateTeamList();
            UpdateBrief();
            UpdateDeathPanel();
            UpdateDebrief();
            UpdateScoreboard();
            UpdatePrivateNote();
            UpdateShake();
        }

        // ================================================================== binding

        void BindLocalPlayerIfNeeded()
        {
            var local = PlayerCharacter.Local;
            if (local == null)
            {
                if (m_Character != null)
                {
                    Unbind();
                }

                return;
            }

            if (m_Character == local)
            {
                return;
            }

            Unbind();
            m_Character = local;
            m_Interactor = local.GetComponent<Interactor>();
            m_Feedback = local.GetComponent<PlayerFeedback>();
            m_Vitals = local.GetComponent<PlayerVitals>();
            m_Movement = local.GetComponent<FirstPersonController>();
            m_Inventory = local.GetComponent<InventoryComponent>();
            m_Equipment = local.GetComponent<PlayerEquipment>();
            m_Spectator = local.GetComponent<SpectatorController>();

            if (m_Interactor != null) m_Interactor.TargetChanged += HandleTargetChanged;
            if (m_Feedback != null) m_Feedback.MessageReceived += ShowMessage;
            if (m_Inventory != null) m_Inventory.Changed += RefreshHotbar;
            if (m_Equipment != null) m_Equipment.HitConfirmed += HandleHitConfirmed;
            if (m_Vitals != null && m_Vitals.Health != null) m_Vitals.Health.HitReceived += HandleHitReceived;
            if (m_Movement != null) m_Movement.Landed += HandleLanded;

            if (m_NameLabel != null)
            {
                m_NameLabel.text = PlayerIdentity.NameOf(local.OwnerClientId);
            }

            RefreshHotbar();
        }

        void Unbind()
        {
            if (m_Interactor != null) m_Interactor.TargetChanged -= HandleTargetChanged;
            if (m_Feedback != null) m_Feedback.MessageReceived -= ShowMessage;
            if (m_Inventory != null) m_Inventory.Changed -= RefreshHotbar;
            if (m_Equipment != null) m_Equipment.HitConfirmed -= HandleHitConfirmed;
            if (m_Vitals != null && m_Vitals.Health != null) m_Vitals.Health.HitReceived -= HandleHitReceived;
            if (m_Movement != null) m_Movement.Landed -= HandleLanded;

            m_Character = null;
            m_Interactor = null;
            m_Feedback = null;
            m_Vitals = null;
            m_Movement = null;
            m_Inventory = null;
            m_Equipment = null;
            m_Spectator = null;
            SetPrompt(string.Empty);
        }

        // ================================================================== prompt / message

        void HandleTargetChanged(IInteractable target, string prompt)
        {
            SetPrompt(target != null ? $"[E] {prompt}" : string.Empty);
        }

        void SetPrompt(string text)
        {
            if (promptLabel != null)
            {
                promptLabel.text = text;
            }
        }

        void UpdateFades()
        {
            var promptTarget = promptLabel != null && !string.IsNullOrEmpty(promptLabel.text) ? 1f : 0f;
            m_PromptAlpha = Mathf.MoveTowards(m_PromptAlpha, promptTarget, fadeSpeed * Time.deltaTime);
            ApplyAlpha(promptLabel, promptPlate, m_PromptAlpha, 0.55f);

            var messageTarget = messageLabel != null && !string.IsNullOrEmpty(messageLabel.text) ? 1f : 0f;
            m_MessageAlpha = Mathf.MoveTowards(m_MessageAlpha, messageTarget, fadeSpeed * Time.deltaTime);
            ApplyAlpha(messageLabel, messagePlate, m_MessageAlpha, 0.6f);
        }

        static void ApplyAlpha(Text label, Image plate, float alpha, float plateAlpha)
        {
            if (label != null)
            {
                var c = label.color;
                c.a = alpha;
                label.color = c;
            }

            if (plate != null)
            {
                var c = plate.color;
                c.a = alpha * plateAlpha;
                plate.color = c;
            }
        }

        void RefreshSessionLabel()
        {
            if (sessionLabel == null)
            {
                return;
            }

            var code = SessionManager.Exists ? SessionManager.Instance.JoinCode : null;
            sessionLabel.text = string.IsNullOrEmpty(code) ? string.Empty : $"Join code  {code}";
        }

        void ShowMessage(string message)
        {
            if (messageLabel != null)
            {
                messageLabel.text = message;
            }

            m_MessageClearAt = Time.time + messageDuration;
        }

        void ExpireMessage()
        {
            if (messageLabel != null && m_MessageClearAt > 0f && Time.time >= m_MessageClearAt)
            {
                messageLabel.text = string.Empty;
                m_MessageClearAt = 0f;
            }
        }

        void HandleCursorToggle()
        {
            if (m_PanelSuppressing)
            {
                return;
            }

            var local = PlayerCharacter.Local;
            if (local != null &&
                local.TryGetComponent<StationFocusController>(out var focus) && focus.IsOpen)
            {
                return;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                PlayerCharacter.SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
            }
        }

        // ================================================================== sprites

        void BuildSprites()
        {
            m_Circle = MakeSprite(64, (x, y) =>
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                return Mathf.Clamp01((1f - d) * 12f);
            });

            m_Ring = MakeSprite(64, (x, y) =>
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                return Mathf.Clamp01((1f - Mathf.Abs(d - 0.82f) * 7f));
            });

            m_Radial = MakeSprite(128, (x, y) =>
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 63.5f;
                return Mathf.Clamp01(Mathf.Pow(d, 2.2f));
            });
        }

        static Sprite MakeSprite(int size, System.Func<int, int, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha(x, y));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ================================================================== builders

        RectTransform Panel(string panelName, Vector2 anchor, Vector2 position, Vector2 size, Color color, Transform parent = null)
        {
            var go = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent != null ? parent : m_Root, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        Image MakeImage(string spriteName, Transform parent, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(spriteName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(string labelName, Transform parent, string content, int size, TextAnchor anchor,
            Vector2 anchorPoint, Vector2 position, Vector2 dimensions, Color color)
        {
            var go = new GameObject(labelName, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorPoint;
            rect.anchorMax = anchorPoint;
            rect.pivot = anchorPoint;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }

        static Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.24f, 0.28f, 0.33f, 0.95f);

            var text = Label("Label", rect, label, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size, Cream);
            text.raycastTarget = false;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }

        void BuildVignette()
        {
            var go = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Root, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-200f, -200f);
            rect.offsetMax = new Vector2(200f, 200f);
            m_Vignette = go.GetComponent<Image>();
            m_Vignette.sprite = m_Radial;
            m_Vignette.color = new Color(0f, 0f, 0f, 0f);
            m_Vignette.raycastTarget = false;

            var tint = new GameObject("Tint", typeof(RectTransform), typeof(Image));
            var tintRect = (RectTransform)tint.transform;
            tintRect.SetParent(m_Root, false);
            tintRect.SetAsFirstSibling();
            tintRect.anchorMin = Vector2.zero;
            tintRect.anchorMax = Vector2.one;
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            m_TintOverlay = tint.GetComponent<Image>();
            m_TintOverlay.color = new Color(0f, 0f, 0f, 0f);
            m_TintOverlay.raycastTarget = false;
        }

        void BuildVitals()
        {
            var anchor = new Vector2(0f, 0f);
            var plate = Panel("VitalsPlate", anchor, new Vector2(32f, 36f), new Vector2(360f, 84f), Plate);

            m_NameLabel = Label("Name", plate, "Hunter", 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(330f, 20f), Dim);

            m_HealthBack = MakeImage("HealthBack", plate, null, Vector2.zero, Vector2.zero, new Color(0.2f, 0.05f, 0.05f, 0.9f));
            Fit(m_HealthBack.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 30f), new Vector2(332f, 18f));
            m_HealthFill = MakeImage("HealthFill", plate, null, Vector2.zero, Vector2.zero, new Color(0.75f, 0.2f, 0.16f, 1f));
            Fit(m_HealthFill.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 30f), new Vector2(332f, 18f));
            m_HealthFill.type = Image.Type.Filled;
            m_HealthFill.fillMethod = Image.FillMethod.Horizontal;

            var staminaBack = MakeImage("StaminaBack", plate, null, Vector2.zero, Vector2.zero, new Color(0.05f, 0.1f, 0.12f, 0.9f));
            Fit(staminaBack.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(332f, 8f));
            m_StaminaFill = MakeImage("StaminaFill", plate, null, Vector2.zero, Vector2.zero, new Color(0.45f, 0.75f, 0.85f, 1f));
            Fit(m_StaminaFill.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(332f, 8f));
            m_StaminaFill.type = Image.Type.Filled;
            m_StaminaFill.fillMethod = Image.FillMethod.Horizontal;

            m_BleedLabel = Label("Bleed", plate, "BLEEDING", 14, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(160f, 20f), Blood);
            m_BleedLabel.enabled = false;
        }

        static void Fit(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        void BuildHotbar()
        {
            const float slotSize = 78f;
            const float gap = 8f;
            var total = HotbarSlots * slotSize + (HotbarSlots - 1) * gap;
            var bar = Panel("Hotbar", new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(total + 16f, slotSize + 16f), new Color(0f, 0f, 0f, 0.35f));

            for (var i = 0; i < HotbarSlots; i++)
            {
                var x = -total * 0.5f + i * (slotSize + gap) + slotSize * 0.5f;
                var frame = MakeImage($"Slot{i + 1}", bar, null, new Vector2(x, 0f), new Vector2(slotSize, slotSize), new Color(0.12f, 0.13f, 0.15f, 0.9f));

                var icon = MakeImage("Icon", frame.transform, null, Vector2.zero, new Vector2(slotSize - 12f, slotSize - 12f), Color.white);
                icon.preserveAspect = true;
                icon.enabled = false;

                var initials = Label("Initials", frame.transform, string.Empty, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(slotSize, slotSize), Cream);
                var state = Label("State", frame.transform, string.Empty, 13, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-5f, 3f), new Vector2(slotSize, 16f), Amber);
                var number = Label("Number", frame.transform, (i + 1).ToString(), 12, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(5f, -3f), new Vector2(20f, 16f), Dim);

                m_Hotbar.Add(new HotbarSlot { Rect = frame.rectTransform, Frame = frame, Icon = icon, Initials = initials, State = state, Number = number });
            }
        }

        void BuildHeldPanel()
        {
            var plate = Panel("HeldPlate", new Vector2(1f, 0f), new Vector2(-32f, 36f), new Vector2(300f, 84f), Plate);
            m_HeldName = Label("HeldName", plate, string.Empty, 19, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(272f, 24f), Cream);
            m_HeldAmmo = Label("HeldAmmo", plate, string.Empty, 30, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-14f, 8f), new Vector2(272f, 36f), Cream);
            m_HeldHint = Label("HeldHint", plate, string.Empty, 13, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(14f, 8f), new Vector2(180f, 18f), Dim);
        }

        void BuildUseBar()
        {
            m_UseBar = Panel("UseBar", new Vector2(0.5f, 0.5f), new Vector2(0f, -48f), new Vector2(160f, 8f), new Color(0f, 0f, 0f, 0.6f));
            m_UseFill = MakeImage("UseFill", m_UseBar, null, Vector2.zero, new Vector2(160f, 8f), Amber);
            m_UseFill.type = Image.Type.Filled;
            m_UseFill.fillMethod = Image.FillMethod.Horizontal;
            m_UseBar.gameObject.SetActive(false);
        }

        void BuildDamageArcs()
        {
            var ring = Panel("DamageRing", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45f;
                var arc = MakeImage($"Arc{i}", ring, m_Circle, Vector2.zero, new Vector2(70f, 34f), new Color(0.9f, 0.1f, 0.05f, 0f));
                var rect = arc.rectTransform;
                rect.anchoredPosition = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * 190f;
                rect.localRotation = Quaternion.Euler(0f, 0f, -angle);
                m_DamageArcs[i] = arc;
            }
        }

        void BuildHitMarker()
        {
            var marker = Panel("HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            for (var i = 0; i < 4; i++)
            {
                var line = MakeImage($"Line{i}", marker, null, Vector2.zero, new Vector2(2.5f, 12f), new Color(1f, 1f, 1f, 0f));
                var angle = 45f + i * 90f;
                line.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * 16f;
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
                m_HitLines[i] = line;
            }
        }

        void BuildCompass()
        {
            m_CompassStrip = Panel("Compass", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(620f, 34f), new Color(0f, 0f, 0f, 0.4f));
            var mask = m_CompassStrip.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            var centre = MakeImage("Centre", m_CompassStrip, null, new Vector2(0f, 0f), new Vector2(2f, 34f), new Color(1f, 1f, 1f, 0.35f));
            centre.rectTransform.anchoredPosition = Vector2.zero;

            for (var i = 0; i < 16; i++)
            {
                var marker = new CompassMarker
                {
                    Image = MakeImage($"Marker{i}", m_CompassStrip, m_Circle, Vector2.zero, new Vector2(12f, 12f), Color.white),
                    Label = Label($"MarkerLabel{i}", m_CompassStrip, string.Empty, 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 18f), Cream)
                };
                marker.Image.enabled = false;
                marker.Label.enabled = false;
                m_CompassMarkers.Add(marker);
            }
        }

        void BuildTeamList()
        {
            m_TeamList = Panel("Team", new Vector2(0f, 1f), new Vector2(24f, -56f), new Vector2(260f, 0f), Color.clear);
        }

        void BuildTopRight()
        {
            m_Objective = Label("Objective", m_Root, string.Empty, 16, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-24f, -50f), new Vector2(520f, 40f), Cream);
            m_Creatures = Label("Creatures", m_Root, string.Empty, 15, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-24f, -92f), new Vector2(320f, 20f), Amber);
            m_Wallet = Label("Wallet", m_Root, string.Empty, 15, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-24f, -114f), new Vector2(320f, 20f), new Color(0.65f, 0.8f, 0.7f));
        }

        void BuildBrief()
        {
            m_BriefPanel = Panel("Brief", new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(640f, 130f), new Color(0.05f, 0.06f, 0.08f, 0.9f));
            m_BriefTitle = Label("Title", m_BriefPanel, string.Empty, 26, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 34f), Amber);
            m_BriefBody = Label("Body", m_BriefPanel, string.Empty, 17, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(600f, 70f), Cream);
            m_BriefPanel.gameObject.SetActive(false);
        }

        void BuildDeathPanel()
        {
            m_DeathPanel = Panel("Death", new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(720f, 110f), new Color(0.08f, 0.02f, 0.02f, 0.85f));
            m_DeathTitle = Label("Title", m_DeathPanel, "YOU DIED", 34, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(700f, 44f), Blood);
            m_DeathBody = Label("Body", m_DeathPanel, string.Empty, 17, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(700f, 48f), Cream);
            m_DeathPanel.gameObject.SetActive(false);
        }

        void BuildDebrief()
        {
            m_DebriefPanel = Panel("Debrief", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 560f), new Color(0.07f, 0.08f, 0.1f, 0.97f));
            m_DebriefPanel.GetComponent<Image>().raycastTarget = true;
            Label("Title", m_DebriefPanel, "EXPEDITION DEBRIEF", 30, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(760f, 40f), Amber);
            m_DebriefBody = Label("Body", m_DebriefPanel, string.Empty, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(28f, -76f), new Vector2(764f, 400f), Cream);
            m_DebriefBody.lineSpacing = 1.15f;
            m_DebriefContinue = MakeButton(m_DebriefPanel, "Back to the depot", new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(240f, 44f), () =>
            {
                MissionDirector.Instance?.FinishDebriefRpc();
            });
            m_DebriefPanel.gameObject.SetActive(false);
        }

        void BuildLetter()
        {
            m_LetterPanel = Panel("Letter", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 420f), new Color(0.9f, 0.85f, 0.7f, 0.98f));
            m_LetterPanel.GetComponent<Image>().raycastTarget = true;
            m_LetterBody = Label("Body", m_LetterPanel, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(34f, -34f), new Vector2(492f, 300f), new Color(0.12f, 0.08f, 0.05f));
            m_LetterBody.lineSpacing = 1.25f;
            m_LetterBody.fontStyle = FontStyle.Italic;

            var accept = MakeButton(m_LetterPanel, "Take the money", new Vector2(0f, 0f), new Vector2(34f, 26f), new Vector2(220f, 44f), () => AnswerLetter(true));
            accept.GetComponent<Image>().color = new Color(0.35f, 0.12f, 0.1f, 0.95f);
            var decline = MakeButton(m_LetterPanel, "Burn it", new Vector2(1f, 0f), new Vector2(-34f, 26f), new Vector2(220f, 44f), () => AnswerLetter(false));
            decline.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.26f, 0.95f);
            m_LetterPanel.gameObject.SetActive(false);
        }

        void BuildScoreboard()
        {
            m_Scoreboard = Panel("Scoreboard", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(520f, 260f), new Color(0.05f, 0.06f, 0.08f, 0.92f));
            Label("Title", m_Scoreboard, "CREW", 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(460f, 30f), Amber);
            m_ScoreboardBody = Label("Body", m_Scoreboard, string.Empty, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(24f, -60f), new Vector2(470f, 190f), Cream);
            m_ScoreboardBody.lineSpacing = 1.2f;
            m_Scoreboard.gameObject.SetActive(false);
        }

        void BuildThermalMarkers()
        {
            for (var i = 0; i < 10; i++)
            {
                var marker = MakeImage($"Thermal{i}", m_Root, m_Ring, Vector2.zero, new Vector2(46f, 46f), new Color(1f, 0.45f, 0.1f, 0.85f));
                marker.rectTransform.anchorMin = Vector2.zero;
                marker.rectTransform.anchorMax = Vector2.zero;
                marker.enabled = false;
                m_ThermalMarkers.Add(marker);
            }
        }

        void BuildPrivateNote()
        {
            m_PrivateNote = Label("PrivateNote", m_Root, string.Empty, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(700f, 40f), new Color(0.6f, 0.62f, 0.9f));
            m_PrivateNote.fontStyle = FontStyle.Italic;
            m_PrivateNote.enabled = false;
        }

        // ================================================================== vitals

        void UpdateVitals()
        {
            if (m_Vitals == null || m_Vitals.Health == null)
            {
                if (m_HealthFill != null) m_HealthFill.fillAmount = 0f;
                return;
            }

            var health = m_Vitals.Health;
            m_HealthFill.fillAmount = Mathf.Lerp(m_HealthFill.fillAmount, health.Fraction, 10f * Time.deltaTime);
            m_HealthFill.color = health.Fraction < 0.3f
                ? Color.Lerp(new Color(0.75f, 0.2f, 0.16f), new Color(1f, 0.35f, 0.25f), Mathf.PingPong(Time.time * 3f, 1f))
                : new Color(0.75f, 0.2f, 0.16f);

            if (m_Movement != null)
            {
                var stamina = m_Movement.Stamina01;
                m_StaminaFill.fillAmount = stamina;
                var full = stamina >= 0.999f;
                var alpha = m_Movement.HasAdrenaline ? 1f : full ? 0.35f : 1f;
                m_StaminaFill.color = m_Movement.HasAdrenaline
                    ? new Color(1f, 0.85f, 0.3f, alpha)
                    : m_Movement.IsExhausted ? new Color(0.9f, 0.4f, 0.3f, alpha) : new Color(0.45f, 0.75f, 0.85f, alpha);
            }

            m_BleedLabel.enabled = health.IsBleeding && !health.IsDead;
            if (m_BleedLabel.enabled)
            {
                var c = Blood;
                c.a = 0.6f + Mathf.PingPong(Time.time * 2f, 0.4f);
                m_BleedLabel.color = c;
            }
        }

        // ================================================================== hotbar

        void RefreshHotbar()
        {
            for (var i = 0; i < m_Hotbar.Count; i++)
            {
                var slot = m_Hotbar[i];
                ItemDefinition definition = null;
                var state = 0;

                if (m_Inventory != null && i < m_Inventory.SlotCount)
                {
                    m_Inventory.TryGetDefinition(i, out definition);
                    state = m_Inventory.GetState(i);
                }

                if (definition == null)
                {
                    slot.Icon.enabled = false;
                    slot.Initials.text = string.Empty;
                    slot.State.text = string.Empty;
                    continue;
                }

                if (definition.Icon != null)
                {
                    slot.Icon.sprite = definition.Icon;
                    slot.Icon.enabled = true;
                    slot.Initials.text = string.Empty;
                }
                else
                {
                    slot.Icon.enabled = false;
                    slot.Initials.text = Initials(definition.DisplayName);
                }

                slot.State.text = definition.Behaviour != null ? definition.Behaviour.DescribeState(state) : string.Empty;
            }
        }

        static string Initials(string displayName)
        {
            var builder = new StringBuilder();
            foreach (var word in displayName.Split(' '))
            {
                if (word.Length > 0 && builder.Length < 3)
                {
                    builder.Append(char.ToUpperInvariant(word[0]));
                }
            }

            return builder.ToString();
        }

        void UpdateHotbar()
        {
            var selected = m_Inventory != null ? m_Inventory.SelectedSlot : -1;
            var lit = PlayerLight.Local;

            for (var i = 0; i < m_Hotbar.Count; i++)
            {
                var slot = m_Hotbar[i];
                var isSelected = i == selected;
                var isLit = lit != null && lit.IsOn && m_Inventory != null &&
                            m_Inventory.TryGetDefinition(i, out var definition) &&
                            definition.BehaviourAs<ToggleBehaviour>() != null && definition.BehaviourAs<ToggleBehaviour>().kind == lit.Kind;

                var target = isSelected ? new Color(0.85f, 0.66f, 0.32f, 0.95f)
                    : isLit ? new Color(0.35f, 0.5f, 0.6f, 0.9f)
                    : new Color(0.12f, 0.13f, 0.15f, 0.9f);
                slot.Frame.color = Color.Lerp(slot.Frame.color, target, 14f * Time.deltaTime);
                slot.Rect.localScale = Vector3.Lerp(slot.Rect.localScale, Vector3.one * (isSelected ? 1.08f : 1f), 14f * Time.deltaTime);
                slot.Number.color = isSelected ? new Color(0.1f, 0.09f, 0.07f) : Dim;
            }
        }

        // ================================================================== held item

        void UpdateHeldPanel()
        {
            if (m_Equipment == null || m_Inventory == null || m_Equipment.Held == null)
            {
                m_HeldName.text = string.Empty;
                m_HeldAmmo.text = string.Empty;
                m_HeldHint.text = m_Inventory != null ? "1-5 select   G drop   F light" : string.Empty;
                return;
            }

            var held = m_Equipment.Held;
            var slot = m_Inventory.SelectedSlot;
            var state = m_Inventory.GetState(slot);
            m_HeldName.text = held.DisplayName.ToUpperInvariant();

            switch (held.UseKind)
            {
                case ItemUseKind.Weapon:
                    var weapon = held.BehaviourAs<WeaponBehaviour>();
                    if (weapon != null && weapon.magazineSize > 0)
                    {
                        var reserve = 0;
                        if (weapon.usesAmmoBox)
                        {
                            for (var i = 0; i < m_Inventory.SlotCount; i++)
                            {
                                if (m_Inventory.TryGetDefinition(i, out var d) && d.ItemId == "item.ammo")
                                {
                                    reserve += m_Inventory.GetState(i);
                                }
                            }
                        }

                        m_HeldAmmo.text = m_Equipment.IsReloading
                            ? "<size=20>reloading</size>"
                            : weapon.usesAmmoBox ? $"{state} <size=18><color=#a0a0a0>| {reserve}</color></size>" : state.ToString();
                        m_HeldAmmo.color = state == 0 ? Blood : Cream;
                        m_HeldHint.text = weapon.usesAmmoBox && reserve == 0 && state == 0 ? "no ammo" : "R reload   RMB aim";
                    }
                    else
                    {
                        m_HeldAmmo.text = string.Empty;
                        m_HeldHint.text = "LMB swing";
                    }

                    break;

                case ItemUseKind.Consumable:
                    m_HeldAmmo.text = held.Behaviour.DescribeState(state);
                    m_HeldHint.text = "hold LMB to use";
                    break;

                case ItemUseKind.Deployable:
                    m_HeldAmmo.text = held.Behaviour.DescribeState(state);
                    m_HeldHint.text = "LMB place";
                    break;

                case ItemUseKind.Toggle:
                    m_HeldAmmo.text = held.Behaviour.DescribeState(state);
                    m_HeldHint.text = "F switch on";
                    break;

                default:
                    m_HeldAmmo.text = held.Behaviour != null ? held.Behaviour.DescribeState(state) : string.Empty;
                    m_HeldHint.text = "worn";
                    break;
            }
        }

        void UpdateUseBar()
        {
            var progress = m_Equipment != null ? m_Equipment.UseProgress : 0f;
            var show = progress > 0f;
            if (m_UseBar.gameObject.activeSelf != show)
            {
                m_UseBar.gameObject.SetActive(show);
            }

            if (show)
            {
                m_UseFill.fillAmount = progress;
            }
        }

        // ================================================================== crosshair / markers

        void UpdateCrosshair()
        {
            if (crosshair == null)
            {
                return;
            }

            var aiming = m_Equipment != null && m_Equipment.IsAiming;
            var hot = promptLabel != null && !string.IsNullOrEmpty(promptLabel.text);
            var spread = m_Equipment != null ? m_Equipment.CurrentSpread : 0f;

            var scale = hot ? 1.5f : 1f + spread * 0.35f;
            crosshair.rectTransform.localScale = Vector3.Lerp(crosshair.rectTransform.localScale, Vector3.one * scale, 16f * Time.deltaTime);

            var color = hot ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, aiming ? 0.15f : 0.5f);
            crosshair.color = color;
            if (m_Vitals != null && m_Vitals.IsDead)
            {
                crosshair.color = Color.clear;
            }
        }

        void HandleHitConfirmed(bool weakPoint, bool killed)
        {
            m_HitMarkerHeat = 1f;
            m_HitMarkerColor = killed ? Blood : weakPoint ? Amber : Color.white;
        }

        void UpdateHitMarker()
        {
            m_HitMarkerHeat = Mathf.MoveTowards(m_HitMarkerHeat, 0f, Time.deltaTime * 4f);
            foreach (var line in m_HitLines)
            {
                var c = m_HitMarkerColor;
                c.a = m_HitMarkerHeat;
                line.color = c;
                line.rectTransform.localScale = Vector3.one * (1f + (1f - m_HitMarkerHeat) * 0.4f);
            }
        }

        void HandleHitReceived(Vector3 direction, float amount)
        {
            if (m_Character == null || m_Character.PlayerCamera == null || amount <= 0f)
            {
                return;
            }

            // The hit came along 'direction'; the source is behind the victim along -direction.
            var source = -direction;
            source.y = 0f;
            if (source.sqrMagnitude < 0.001f)
            {
                source = Vector3.forward;
            }

            var cameraForward = m_Character.PlayerCamera.transform.forward;
            cameraForward.y = 0f;
            var angle = Vector3.SignedAngle(cameraForward, source.normalized, Vector3.up);
            var index = Mathf.RoundToInt(((angle + 360f) % 360f) / 45f) % 8;
            m_DamageArcHeat[index] = 1f;

            AudioPool.Play2D(SoundKind.Hurt, 0.7f, AudioPool.Vary(0.1f));
            Shake(Mathf.Clamp01(amount / 40f) * 0.8f + 0.2f);
        }

        void UpdateDamageArcs()
        {
            for (var i = 0; i < 8; i++)
            {
                m_DamageArcHeat[i] = Mathf.MoveTowards(m_DamageArcHeat[i], 0f, Time.deltaTime * 1.4f);
                var c = m_DamageArcs[i].color;
                c.a = m_DamageArcHeat[i] * 0.85f;
                m_DamageArcs[i].color = c;
            }
        }

        void HandleLanded(float impact)
        {
            if (impact > 6f)
            {
                Shake(Mathf.Clamp01((impact - 6f) / 10f) * 0.5f);
                AudioPool.Play2D(SoundKind.FootstepSprint, 0.6f, 0.7f);
            }
        }

        void Shake(float strength)
        {
            m_ShakeStrength = Mathf.Max(m_ShakeStrength, strength);
            m_ShakeTime = 0.25f;
        }

        void UpdateShake()
        {
            if (m_ShakeTime <= 0f)
            {
                m_Root.anchoredPosition = Vector2.Lerp(m_Root.anchoredPosition, m_RootRest, 20f * Time.deltaTime);
                m_ShakeStrength = 0f;
                return;
            }

            m_ShakeTime -= Time.deltaTime;
            var offset = new Vector2(
                (Mathf.PerlinNoise(Time.time * 40f, 0f) - 0.5f),
                (Mathf.PerlinNoise(0f, Time.time * 40f) - 0.5f)) * (m_ShakeStrength * 26f);
            m_Root.anchoredPosition = m_RootRest + offset;
        }

        // ================================================================== vignette / overlays

        void UpdateVignette()
        {
            var low = 0f;
            if (m_Vitals != null && m_Vitals.Health != null && !m_Vitals.IsDead)
            {
                low = 1f - Mathf.Clamp01((m_Vitals.Health.Fraction - 0.08f) / 0.35f);
            }

            var tension = ForestDirector.Instance != null && MissionDirector.Instance != null &&
                          MissionDirector.Instance.Phase == MissionPhase.Expedition
                ? ForestDirector.Instance.Tension
                : 0f;

            m_HeartbeatPulse += Time.deltaTime * (0.9f + tension * 1.2f) * Mathf.PI * 2f;
            var pulse = Mathf.Max(0f, Mathf.Sin(m_HeartbeatPulse)) * 0.5f + 0.5f;

            var redAlpha = low * (0.45f + pulse * 0.25f);
            var darkAlpha = Mathf.Clamp01(tension - 0.4f) * 0.4f * (0.7f + pulse * 0.3f);

            var color = Color.Lerp(new Color(0f, 0f, 0f, darkAlpha), new Color(0.6f, 0.02f, 0.02f, redAlpha), low > 0.02f ? 0.85f : 0f);
            if (low <= 0.02f)
            {
                color = new Color(0f, 0f, 0f, darkAlpha);
            }

            if (m_Vitals != null && m_Vitals.IsDead)
            {
                color = new Color(0f, 0f, 0f, 0.55f);
            }

            m_Vignette.color = Color.Lerp(m_Vignette.color, color, 6f * Time.deltaTime);

            // Optics tints.
            var light = PlayerLight.Local;
            var tint = new Color(0f, 0f, 0f, 0f);
            if (light != null && light.IsOn)
            {
                tint = light.Kind switch
                {
                    ToggleKind.NightVision => new Color(0.2f, 0.9f, 0.3f, 0.22f),
                    ToggleKind.Thermal => new Color(0.1f, 0.15f, 0.4f, 0.28f),
                    ToggleKind.UltraViolet => new Color(0.35f, 0.15f, 0.7f, 0.1f),
                    _ => tint
                };
            }

            m_TintOverlay.color = Color.Lerp(m_TintOverlay.color, tint, 8f * Time.deltaTime);
            ApplyNightVision(light);
        }

        float m_AmbientRest = -1f;

        /// <summary>NVG brightens the world by lifting ambient light; crude and effective.</summary>
        void ApplyNightVision(PlayerLight light)
        {
            var on = light != null && light.IsOn && light.Kind == ToggleKind.NightVision;
            if (on)
            {
                if (m_AmbientRest < 0f)
                {
                    m_AmbientRest = RenderSettings.ambientIntensity;
                }

                var gain = light.Active != null ? light.Active.nightVisionGain : 2f;
                RenderSettings.ambientIntensity = Mathf.Max(m_AmbientRest, 1f) * gain;
                RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, new Color(0.35f, 0.55f, 0.35f), 4f * Time.deltaTime);
            }
            else if (m_AmbientRest >= 0f)
            {
                RenderSettings.ambientIntensity = m_AmbientRest;
                m_AmbientRest = -1f;
            }
        }

        // ================================================================== compass

        void UpdateCompass()
        {
            var expedition = MissionDirector.Instance != null && MissionDirector.Instance.IsOnExpedition;
            m_CompassStrip.gameObject.SetActive(expedition && m_Character != null && !(m_Vitals != null && m_Vitals.IsDead));

            if (!m_CompassStrip.gameObject.activeSelf)
            {
                return;
            }

            var camera = m_Character.PlayerCamera;
            if (camera == null)
            {
                return;
            }

            var forward = camera.transform.forward;
            forward.y = 0f;
            var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            var index = 0;
            const float halfWidth = 300f;
            const float halfFov = 100f;

            void Place(Vector3 world, Color color, string text)
            {
                if (index >= m_CompassMarkers.Count)
                {
                    return;
                }

                var to = world - m_Character.transform.position;
                to.y = 0f;
                var bearing = Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                var marker = m_CompassMarkers[index++];
                var visible = Mathf.Abs(bearing) <= halfFov;
                marker.Image.enabled = visible;
                marker.Label.enabled = visible && !string.IsNullOrEmpty(text);
                if (!visible)
                {
                    return;
                }

                var x = bearing / halfFov * halfWidth;
                marker.Image.rectTransform.anchoredPosition = new Vector2(x, 0f);
                marker.Image.color = color;
                marker.Label.rectTransform.anchoredPosition = new Vector2(x, -13f);
                marker.Label.text = text;
                marker.Label.color = color;
            }

            // Cardinal points.
            foreach (var (label, heading) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) })
            {
                var bearing = Mathf.DeltaAngle(yaw, heading);
                if (index < m_CompassMarkers.Count)
                {
                    var marker = m_CompassMarkers[index++];
                    var visible = Mathf.Abs(bearing) <= halfFov;
                    marker.Image.enabled = false;
                    marker.Label.enabled = visible;
                    marker.Label.rectTransform.anchoredPosition = new Vector2(bearing / halfFov * halfWidth, 6f);
                    marker.Label.text = label;
                    marker.Label.color = Dim;
                }
            }

            if (m_Extraction == null)
            {
                var extraction = FindFirstObjectByType<ExtractionPoint>();
                m_Extraction = extraction != null ? extraction.transform : null;
            }

            if (m_Extraction != null)
            {
                Place(m_Extraction.position, Amber, "train");
            }

            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals == null || vitals == m_Vitals)
                {
                    continue;
                }

                Place(vitals.transform.position, vitals.IsDead ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.45f, 0.7f, 1f),
                    PlayerIdentity.NameOf(vitals.OwnerClientId));
            }

            for (var i = m_AlarmPings.Count - 1; i >= 0; i--)
            {
                if (Time.time > m_AlarmPings[i].until)
                {
                    m_AlarmPings.RemoveAt(i);
                    continue;
                }

                Place(m_AlarmPings[i].position, new Color(1f, 0.3f, 0.2f), "alarm");
            }

            for (; index < m_CompassMarkers.Count; index++)
            {
                m_CompassMarkers[index].Image.enabled = false;
                m_CompassMarkers[index].Label.enabled = false;
            }
        }

        void HandleAlarm(Vector3 position)
        {
            m_AlarmPings.Add((position, Time.time + 12f));
            ShowMessage("Motion alarm triggered.");
        }

        // ================================================================== thermal

        void UpdateThermal()
        {
            var light = PlayerLight.Local;
            var on = light != null && light.IsOn && light.Kind == ToggleKind.Thermal && m_Character != null && m_Character.PlayerCamera != null;
            var range = on && light.Active != null ? light.Active.thermalRange : 0f;
            var used = 0;

            if (on)
            {
                var camera = m_Character.PlayerCamera;
                foreach (var brain in CreatureBrain.All)
                {
                    if (brain == null || used >= m_ThermalMarkers.Count || brain.State == CreatureState.Dead)
                    {
                        continue;
                    }

                    var world = brain.transform.position + Vector3.up * 0.8f;
                    if (Vector3.Distance(world, camera.transform.position) > range)
                    {
                        continue;
                    }

                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f)
                    {
                        continue;
                    }

                    var marker = m_ThermalMarkers[used++];
                    marker.enabled = true;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, screen, null, out var local);
                    marker.rectTransform.anchoredPosition = local + m_Root.rect.size * 0.5f;
                    var size = Mathf.Lerp(70f, 26f, Mathf.Clamp01(screen.z / range));
                    marker.rectTransform.sizeDelta = new Vector2(size, size);
                    marker.color = new Color(1f, 0.45f, 0.1f, 0.6f + Mathf.PingPong(Time.time * 2f, 0.3f));
                }
            }

            for (; used < m_ThermalMarkers.Count; used++)
            {
                m_ThermalMarkers[used].enabled = false;
            }
        }

        // ================================================================== team

        void UpdateTeamList()
        {
            var row = 0;
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals == null || vitals == m_Vitals)
                {
                    continue;
                }

                if (row >= m_TeamRows.Count)
                {
                    var rect = Panel($"Row{row}", new Vector2(0f, 1f), new Vector2(0f, -row * 30f), new Vector2(260f, 26f), new Color(0f, 0f, 0f, 0.35f), m_TeamList);
                    var name = Label("Name", rect, string.Empty, 15, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(150f, 24f), Cream);
                    var back = MakeImage("Back", rect, null, Vector2.zero, Vector2.zero, new Color(0.2f, 0.05f, 0.05f, 0.9f));
                    Fit(back.rectTransform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(90f, 8f));
                    var fill = MakeImage("Fill", rect, null, Vector2.zero, Vector2.zero, new Color(0.75f, 0.2f, 0.16f));
                    Fit(fill.rectTransform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(90f, 8f));
                    fill.type = Image.Type.Filled;
                    fill.fillMethod = Image.FillMethod.Horizontal;
                    m_TeamRows.Add(new TeamRow { Rect = rect, Name = name, Fill = fill, Back = back });
                }

                var entry = m_TeamRows[row];
                entry.Rect.gameObject.SetActive(true);
                entry.Rect.anchoredPosition = new Vector2(0f, -row * 30f);
                entry.Name.text = PlayerIdentity.NameOf(vitals.OwnerClientId) + (vitals.IsDead ? "  <color=#888888>dead</color>" : string.Empty);
                entry.Fill.fillAmount = vitals.Health != null ? vitals.Health.Fraction : 0f;
                entry.Fill.color = vitals.Health != null && vitals.Health.IsBleeding ? new Color(1f, 0.35f, 0.25f) : new Color(0.75f, 0.2f, 0.16f);
                row++;
            }

            for (; row < m_TeamRows.Count; row++)
            {
                m_TeamRows[row].Rect.gameObject.SetActive(false);
            }
        }

        // ================================================================== phase / objective

        void RefreshPhaseAndObjective()
        {
            var director = MissionDirector.Instance;
            var phase = director != null ? director.Phase : MissionPhase.Preparing;

            if (phaseLabel != null)
            {
                phaseLabel.text = director != null ? phase.ToString().ToUpperInvariant() : string.Empty;
            }

            if (phase != m_LastPhase)
            {
                HandlePhaseChanged(m_LastPhase, phase);
                m_LastPhase = phase;
            }

            var contract = director != null ? director.ActiveContract : null;
            m_Objective.text = contract != null && director.IsOnExpedition ? contract.Objective : string.Empty;

            var forest = ForestDirector.Instance;
            m_Creatures.text = forest != null && director != null && director.IsOnExpedition
                ? $"creatures remaining  {forest.CreaturesRemaining}"
                : string.Empty;

            var wallet = m_Character != null ? m_Character.GetComponent<PlayerWallet>() : null;
            m_Wallet.text = wallet != null ? $"{wallet.Credits} cr" : string.Empty;
        }

        void HandlePhaseChanged(MissionPhase previous, MissionPhase current)
        {
            var director = MissionDirector.Instance;

            if (current == MissionPhase.Expedition && director != null && director.ActiveContract != null)
            {
                var contract = director.ActiveContract;
                m_BriefTitle.text = contract.title.ToUpperInvariant();
                m_BriefBody.text = contract.Objective + (string.IsNullOrEmpty(contract.briefing) ? string.Empty : "\n<size=14><color=#b0b0b0>" + contract.briefing + "</color></size>");
                m_BriefPanel.gameObject.SetActive(true);
                m_BriefHideAt = Time.time + 9f;
                AudioPool.Play2D(SoundKind.Chime, 0.5f);
            }

            if (current == MissionPhase.Debrief)
            {
                OpenDebrief();
            }
            else if (previous == MissionPhase.Debrief)
            {
                CloseDebrief();
            }

            if (current == MissionPhase.Preparing)
            {
                m_DeathPanel.gameObject.SetActive(false);
                if (m_LetterPanel.gameObject.activeSelf)
                {
                    CloseLetter();
                }
            }
        }

        void UpdateBrief()
        {
            if (m_BriefPanel.gameObject.activeSelf && Time.time >= m_BriefHideAt)
            {
                m_BriefPanel.gameObject.SetActive(false);
            }
        }

        // ================================================================== death

        void UpdateDeathPanel()
        {
            var dead = m_Vitals != null && m_Vitals.IsDead;
            if (m_DeathPanel.gameObject.activeSelf != dead)
            {
                m_DeathPanel.gameObject.SetActive(dead);
            }

            if (!dead)
            {
                return;
            }

            var following = m_Spectator != null ? m_Spectator.Following : null;
            m_DeathBody.text = following != null
                ? $"Spectating {PlayerIdentity.NameOf(following.OwnerClientId)}    <color=#a0a0a0>1 / 2 to switch</color>"
                : "Nobody left to watch. The train leaves when the crew is gone.";
        }

        // ================================================================== debrief

        void OpenDebrief()
        {
            m_DebriefPanel.gameObject.SetActive(true);
            m_DebriefContinue.gameObject.SetActive(Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsHost);
            SetPanelSuppression(true);
            RefreshDebriefText();
        }

        void CloseDebrief()
        {
            m_DebriefPanel.gameObject.SetActive(false);
            SetPanelSuppression(false);
        }

        void UpdateDebrief()
        {
            if (m_DebriefPanel.gameObject.activeSelf)
            {
                RefreshDebriefText();
            }
        }

        void RefreshDebriefText()
        {
            var director = MissionDirector.Instance;
            if (director == null)
            {
                m_DebriefBody.text = "No mission director.";
                return;
            }

            var builder = new StringBuilder();
            var contract = director.ActiveContract;
            if (contract != null)
            {
                builder.AppendLine($"<color=#f2c774>{contract.title}</color>");
            }

            var creatures = GameContent.Creatures;

            for (var i = 0; i < director.LedgerCount; i++)
            {
                var entry = director.GetLedgerEntry(i);
                var creature = creatures != null ? creatures.Find(entry.SubjectId) : null;
                var creatureName = creature != null ? creature.displayName : "creature";

                switch (entry.Kind)
                {
                    case LedgerKind.Kill:
                        builder.AppendLine($"{creatureName} killed, tagged by {PlayerIdentity.NameOf(entry.ClientId)}    <color=#a6ccb3>+{entry.Value}</color>");
                        break;
                    case LedgerKind.Capture:
                        builder.AppendLine($"{creatureName} taken alive by {PlayerIdentity.NameOf(entry.ClientId)}    <color=#a6ccb3>+{entry.Value}</color>");
                        break;
                    case LedgerKind.PlayerDeath:
                        builder.AppendLine($"<color=#e07070>{PlayerIdentity.NameOf(entry.ClientId)} died</color>  -  {CauseText(entry.Cause, creature)}");
                        break;
                    case LedgerKind.Exposed:
                        builder.AppendLine($"<color=#ff5050>{PlayerIdentity.NameOf(entry.ByClientId)} murdered {PlayerIdentity.NameOf(entry.ClientId)}. It was seen.</color>");
                        break;
                    case LedgerKind.Completion:
                        builder.AppendLine($"Contract completed    <color=#a6ccb3>+{entry.Value}</color>");
                        break;
                    case LedgerKind.Extracted:
                        builder.AppendLine($"{PlayerIdentity.NameOf(entry.ClientId)} extracted    <color=#a6ccb3>{entry.Value} cr</color>");
                        break;
                    case LedgerKind.Lost:
                        builder.AppendLine("<color=#ff5050>The crew did not come back. Carried gear is gone.</color>");
                        break;
                }
            }

            if (director.LedgerCount == 0)
            {
                builder.AppendLine("Nothing to report.");
            }

            m_DebriefBody.text = builder.ToString();
        }

        static string CauseText(DamageKind cause, CreatureDefinition creature)
        {
            return cause switch
            {
                DamageKind.Bite => creature != null ? $"killed by a {creature.displayName}" : "killed by a creature",
                DamageKind.Ballistic => "<color=#f2c774>gunfire</color>",
                DamageKind.Melee => "<color=#f2c774>a blade</color>",
                DamageKind.Sedative => "sedative overdose",
                DamageKind.Bleed => "bled out",
                DamageKind.Trap => "a bear trap",
                DamageKind.Fall => "a fall",
                _ => "unknown"
            };
        }

        // ================================================================== the letter

        void HandleOffer()
        {
            m_Apparition = CollectorApparition.Spawn();
            Invoke(nameof(OpenLetter), 3.5f);
        }

        void OpenLetter()
        {
            if (!CollectorContract.HasPendingOffer)
            {
                return;
            }

            m_LetterBody.text =
                "A letter is pressed into your hand. The paper is cold.\n\n" +
                $"\"There is a price on <b>{CollectorContract.OfferTargetName}</b>. " +
                $"See that they do not board the train, and see that no one sees you do it. " +
                $"<b>{CollectorContract.OfferBonus} cr</b> waits at the depot for you alone.\n\n" +
                "If you are seen, you will be named. If you refuse, nothing changes.\"\n\n" +
                "<size=15>Your shots do full damage to them while this holds. The ledger will say only: gunfire.</size>";

            m_LetterPanel.gameObject.SetActive(true);
            SetPanelSuppression(true);
        }

        void AnswerLetter(bool accept)
        {
            CollectorContract.Instance?.Answer(accept);
            CloseLetter();
            AudioPool.Play2D(accept ? SoundKind.Chime : SoundKind.Click, 0.6f, accept ? 0.7f : 1f);
        }

        void HandleOfferClosed()
        {
            if (m_LetterPanel.gameObject.activeSelf)
            {
                CloseLetter();
            }
        }

        void CloseLetter()
        {
            m_LetterPanel.gameObject.SetActive(false);
            if (!m_DebriefPanel.gameObject.activeSelf)
            {
                SetPanelSuppression(false);
            }

            if (m_Apparition != null)
            {
                m_Apparition.Dismiss();
                m_Apparition = null;
            }
        }

        void HandlePrivateNote(string note)
        {
            m_PrivateNote.text = note;
            m_PrivateNote.enabled = true;
            m_PrivateNoteHideAt = Time.time + 12f;
            AudioPool.Play2D(SoundKind.Whisper, 0.7f, 0.9f);
        }

        void UpdatePrivateNote()
        {
            if (m_PrivateNote.enabled && Time.time >= m_PrivateNoteHideAt)
            {
                m_PrivateNote.enabled = false;
            }
        }

        // ================================================================== scoreboard

        void UpdateScoreboard()
        {
            var show = m_Character != null && m_Character.Input != null && m_Character.Input.ScoreboardHeld;
            if (m_Scoreboard.gameObject.activeSelf != show)
            {
                m_Scoreboard.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (var identity in PlayerIdentity.All)
            {
                if (identity == null)
                {
                    continue;
                }

                var vitals = PlayerVitals.Find(identity.OwnerClientId);
                var wallet = identity.GetComponent<PlayerWallet>();
                var status = vitals == null ? string.Empty : vitals.IsDead ? "<color=#888888>dead</color>" : "alive";
                builder.AppendLine($"{identity.DisplayName,-18}  {status,-8}  {(wallet != null ? wallet.Credits : 0)} cr");
            }

            m_ScoreboardBody.text = builder.ToString();
        }

        // ================================================================== input suppression

        void SetPanelSuppression(bool on)
        {
            if (m_PanelSuppressing == on)
            {
                return;
            }

            m_PanelSuppressing = on;
            var input = m_Character != null ? m_Character.Input : null;
            input?.SetSuppressed(on);
            PlayerCharacter.SetCursorLocked(!on);
        }
    }
}
