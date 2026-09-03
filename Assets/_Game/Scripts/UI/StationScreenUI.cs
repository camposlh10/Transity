using System.Collections.Generic;
using System.Linq;
using Transity.Core;
using Transity.Inventory;
using Transity.Missions;
using Transity.Player;
using Transity.Train;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Transity.UI
{
    /// <summary>
    /// The screens behind the depot stations: the vendor's market, the wardrobe loadout
    /// board, and the mission computer.
    ///
    /// Rows are built at runtime from the item registry rather than authored as prefabs, so
    /// adding a tenth item to the registry puts it on the shelf with no UI work. Nothing
    /// here mutates state directly -- every button sends a server RPC and then waits for
    /// the replicated values to come back and redraw it.
    /// </summary>
    public sealed class StationScreenUI : MonoBehaviour
    {
        public static StationScreenUI Instance { get; private set; }

        const int RowHeight = 46;
        const float PreviewWidth = 420f;
        const int PanelWidth = 900;
        const int PanelHeight = 620;

        Canvas m_Canvas;
        RectTransform m_Panel;
        RectTransform m_Content;
        RectTransform m_Viewport;
        Image m_Dim;
        RectTransform m_PreviewPanel;
        RectTransform m_PickerRow;
        RawImage m_PreviewImage;
        Text m_PreviewCaption;
        Text m_Title;
        Text m_Subtitle;
        Text m_Footer;

        StationTerminal m_Terminal;
        StationFocusController m_Focus;
        readonly Dictionary<int, int> m_BuyQuantity = new();

        PlayerStash m_BoundStash;
        InventoryComponent m_BoundInventory;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            EnsureEventSystem();
            BuildChrome();
            SetVisible(false);
        }

        void OnDestroy()
        {
            Unbind();

            if (Instance == this)
            {
                Instance = null;
            }
        }

/// <summary>
        /// Guarantees exactly one EventSystem.
        ///
        /// EventSystem.current is not a safe test here: it is only assigned once an
        /// EventSystem has had its OnEnable run, so checking it during Awake in the Boot
        /// scene reports "none" and happily creates a second one that then collides with the
        /// menu's. Searching for the objects themselves is the reliable check, and any extras
        /// found later (a scene that ships its own) are removed.
        /// </summary>
        static void EnsureEventSystem()
        {
            var systems = FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (systems.Length > 0)
            {
                for (var i = 1; i < systems.Length; i++)
                {
                    Destroy(systems[i].gameObject);
                }

                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Re-checked on every scene load, because a freshly loaded scene can bring its own.
        /// </summary>
        void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        void Update()
        {
            // The mission computer redraws when the contract or phase changes on the
            // server; polling is simpler than binding to a director that may not exist yet.
            if (m_Terminal != null && m_Terminal.Screen == StationScreenKind.MissionTerminal &&
                MissionDirector.Instance != null && MissionDirector.Instance.ContractIndex != m_DrawnContract)
            {
                m_DrawnContract = MissionDirector.Instance.ContractIndex;
                Redraw();
            }
        }

        int m_DrawnContract = -2;

        static void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            EnsureEventSystem();
        }

        // ------------------------------------------------------------------ open / close

        public void Open(StationTerminal terminal, StationFocusController focus)
        {
            m_Terminal = terminal;
            m_Focus = focus;
            m_BuyQuantity.Clear();

            Bind();
            SetVisible(true);
            Redraw();
        }

        public void Close()
        {
            SetPreviewVisible(false);
            CharacterPreview.Instance?.Hide();
            Unbind();
            m_Terminal = null;
            m_Focus = null;
            SetVisible(false);
        }

        void Bind()
        {
            Unbind();

            var local = PlayerCharacter.Local;
            if (local == null)
            {
                return;
            }

            if (local.TryGetComponent<PlayerStash>(out var stash))
            {
                m_BoundStash = stash;
                m_BoundStash.Changed += Redraw;
            }

            if (local.TryGetComponent<InventoryComponent>(out var inventory))
            {
                m_BoundInventory = inventory;
                m_BoundInventory.Changed += Redraw;
            }
        }

        void Unbind()
        {
            if (m_BoundStash != null)
            {
                m_BoundStash.Changed -= Redraw;
                m_BoundStash = null;
            }

            if (m_BoundInventory != null)
            {
                m_BoundInventory.Changed -= Redraw;
                m_BoundInventory = null;
            }
        }

        void SetVisible(bool visible)
        {
            if (m_Canvas != null)
            {
                m_Canvas.enabled = visible;
            }
        }

        // ---------------------------------------------------------------------- drawing

        void Redraw()
        {
            if (m_Terminal == null)
            {
                return;
            }

            for (var i = m_Content.childCount - 1; i >= 0; i--)
            {
                Destroy(m_Content.GetChild(i).gameObject);
            }

            switch (m_Terminal.Screen)
            {
                case StationScreenKind.Market:
                    DrawMarket();
                    break;
                case StationScreenKind.Loadout:
                    DrawLoadout();
                    break;
                case StationScreenKind.MissionTerminal:
                    DrawMissionTerminal();
                    break;
            }
        }

        void DrawMarket()
        {
            // The vendor is framed on the right, so the board sits on the left beside him
            // rather than covering him. Dimming stays light so he reads as present.
            SetPanelLayout(900f, 660f, -470f, 0.22f);
            SetPreviewVisible(false);

            m_Title.text = "QUARTERMASTER";
            m_Subtitle.text = "Bought gear goes to your stash. Collect it at the wardrobe before you depart.";
            m_Footer.text = m_BoundStash != null && m_BoundStash.ChargeForPurchases
                ? "Charging is enabled but wallets do not exist yet."
                : "Free while the economy is unbuilt - prices shown are what they will cost later.";

            var registry = GameContent.ItemRegistry;
            if (registry == null)
            {
                AddLabelRow(0, "No item registry assigned on GameContent.");
                return;
            }

            var items = registry.Items
                .Where(i => i != null)
                .OrderBy(i => i.Category)
                .ThenBy(i => i.DisplayName)
                .ToList();

            var row = 0;
            var lastCategory = (ItemCategory)(-1);

            foreach (var item in items)
            {
                if (item.Category != lastCategory)
                {
                    AddHeaderRow(row++, item.Category.ToString().ToUpperInvariant());
                    lastCategory = item.Category;
                }

                AddMarketRow(row++, item);
            }

            SetContentHeight(row);
        }

        void AddMarketRow(int row, ItemDefinition item)
        {
            var rowObject = AddRow(row);
            var owned = m_BoundStash != null ? m_BoundStash.GetCount(item.NetworkId) : 0;
            var quantity = GetQuantity(item.NetworkId);

            AddText(rowObject, item.DisplayName, 20, TextAnchor.MiddleLeft, 16, 300);
            AddText(rowObject, item.Price > 0 ? $"{item.Price} cr" : "issued", 17,
                TextAnchor.MiddleLeft, 330, 110, new Color(0.75f, 0.72f, 0.6f));
            AddText(rowObject, $"in stash: {owned}", 17, TextAnchor.MiddleLeft, 450, 150,
                new Color(0.65f, 0.8f, 0.7f));

            AddButton(rowObject, "-", 610, 36, () => ChangeQuantity(item.NetworkId, -1));
            AddText(rowObject, quantity.ToString(), 19, TextAnchor.MiddleCenter, 650, 44);
            AddButton(rowObject, "+", 700, 36, () => ChangeQuantity(item.NetworkId, 1));

            AddButton(rowObject, "Buy", 750, 92, () =>
            {
                m_BoundStash?.RequestPurchaseRpc(item.NetworkId, GetQuantity(item.NetworkId));
            });
        }

        void DrawLoadout()
        {
            SetPanelLayout(1480f, 880f, 0f, 0.72f);
            ShowCharacterPreview();

            m_Title.text = "WARDROBE";
            m_Subtitle.text = "Take gear from the stash. Anything you carry out is at risk; " +
                              "what stays here is safe.";

            var inventory = m_BoundInventory;
            var carried = 0;
            if (inventory != null)
            {
                for (var i = 0; i < inventory.SlotCount; i++)
                {
                    if (inventory.GetSlot(i) != InventoryComponent.EmptySlot)
                    {
                        carried++;
                    }
                }
            }

            m_Footer.text = inventory != null
                ? $"Carrying {carried} of {inventory.Capacity} slots."
                : "No local player inventory found.";

            var row = 0;
            AddHeaderRow(row++, "YOUR STASH");

            var stash = m_BoundStash;
            var registry = GameContent.ItemRegistry;

            if (stash == null || registry == null)
            {
                AddLabelRow(row++, "Stash unavailable.");
            }
            else if (stash.EntryCount == 0)
            {
                AddLabelRow(row++, "Empty. Buy something from the quartermaster.");
            }
            else
            {
                for (var i = 0; i < stash.EntryCount; i++)
                {
                    var entry = stash.GetEntry(i);
                    if (!registry.TryGet(entry.ItemId, out var definition))
                    {
                        continue;
                    }

                    var rowObject = AddRow(row++);
                    AddText(rowObject, definition.DisplayName, 20, TextAnchor.MiddleLeft, 16, 320);
                    AddText(rowObject, $"x{entry.Count}", 19, TextAnchor.MiddleLeft, 350, 90,
                        new Color(0.65f, 0.8f, 0.7f));

                    var itemId = entry.ItemId;
                    AddButton(rowObject, "Take one", 700, 140,
                        () => m_BoundStash?.RequestWithdrawRpc(itemId));
                }
            }

            row++;
            AddHeaderRow(row++, "CARRYING");

            if (inventory == null)
            {
                AddLabelRow(row++, "No inventory.");
            }
            else
            {
                for (var slot = 0; slot < inventory.Capacity; slot++)
                {
                    var itemId = inventory.GetSlot(slot);
                    var rowObject = AddRow(row++);

                    if (itemId == InventoryComponent.EmptySlot)
                    {
                        AddText(rowObject, $"Slot {slot + 1}   -   empty", 19, TextAnchor.MiddleLeft,
                            16, 400, new Color(0.55f, 0.55f, 0.55f));
                        continue;
                    }

                    var label = registry != null && registry.TryGet(itemId, out var definition)
                        ? definition.DisplayName
                        : "Unknown item";

                    AddText(rowObject, $"Slot {slot + 1}   -   {label}", 19, TextAnchor.MiddleLeft, 16, 400);

                    var captured = slot;
                    AddButton(rowObject, "Store", 700, 140,
                        () => m_BoundStash?.RequestDepositRpc(captured));
                }
            }

            SetContentHeight(row);
        }

        void DrawMissionTerminal()
        {
            SetPanelLayout(1480f, 880f, 0f, 0.72f);
            SetPreviewVisible(false);

            m_Title.text = "MISSION COMPUTER";

            var director = MissionDirector.Instance;
            var wallet = PlayerCharacter.Local != null ? PlayerCharacter.Local.GetComponent<PlayerWallet>() : null;
            m_Subtitle.text = director != null
                ? $"Phase: {director.Phase}    Credits: {(wallet != null ? wallet.Credits : 0)}"
                : "Mission director offline.";
            m_Footer.text = "Pick a contract, then depart. Bounties are split between whoever comes back.";

            var row = 0;
            AddHeaderRow(row++, "CONTRACTS");

            var contracts = GameContent.Contracts;
            var selected = director != null ? director.ContractIndex : -1;
            var canChoose = director != null && director.Phase == MissionPhase.Preparing;

            if (contracts == null || contracts.Count == 0)
            {
                AddLabelRow(row++, "No contracts on the board.");
            }
            else
            {
                for (var i = 0; i < contracts.Count; i++)
                {
                    var contract = contracts.Get(i);
                    if (contract == null)
                    {
                        continue;
                    }

                    var isSelected = i == selected;
                    var rowObject = AddRow(row++);
                    if (isSelected)
                    {
                        rowObject.GetComponent<Image>().color = new Color(0.85f, 0.66f, 0.32f, 0.2f);
                    }

                    var tier = new string('I', contract.tier);
                    AddText(rowObject, $"{contract.title}   <size=13><color=#b0a080>tier {tier}</color></size>", 19,
                        TextAnchor.MiddleLeft, 16, 420, isSelected ? new Color(0.95f, 0.78f, 0.45f) : Color.white);
                    AddText(rowObject, contract.creature != null
                            ? $"{contract.count}x {contract.creature.displayName}" +
                              (contract.secondaryCreature != null && contract.secondaryCount > 0
                                  ? $" +{contract.secondaryCount}"
                                  : string.Empty)
                            : "?",
                        16, TextAnchor.MiddleLeft, 440, 250, new Color(0.75f, 0.72f, 0.6f));

                    if (canChoose)
                    {
                        var index = i;
                        AddButton(rowObject, isSelected ? "Selected" : "Select", 700, 140,
                            () => MissionDirector.Instance?.SelectContractRpc(index));
                    }
                }
            }

            if (selected >= 0 && contracts != null && contracts.Get(selected) is { } chosen)
            {
                row++;
                AddHeaderRow(row++, "BRIEFING");
                AddLabelRow(row++, chosen.Objective);
                if (!string.IsNullOrEmpty(chosen.briefing))
                {
                    AddLabelRow(row++, chosen.briefing);
                }
            }

            row++;
            AddHeaderRow(row++, "YOUR STASH");

            var stash = m_BoundStash;
            var registry = GameContent.ItemRegistry;

            if (stash == null || registry == null || stash.EntryCount == 0)
            {
                AddLabelRow(row++, "Nothing in the stash.");
            }
            else
            {
                for (var i = 0; i < stash.EntryCount; i++)
                {
                    var entry = stash.GetEntry(i);
                    if (!registry.TryGet(entry.ItemId, out var definition))
                    {
                        continue;
                    }

                    var rowObject = AddRow(row++);
                    AddText(rowObject, definition.DisplayName, 20, TextAnchor.MiddleLeft, 16, 320);
                    AddText(rowObject, $"x{entry.Count}", 19, TextAnchor.MiddleLeft, 350, 90,
                        new Color(0.65f, 0.8f, 0.7f));
                }
            }

            row++;
            var canDepart = canChoose && selected >= 0;
            var departRow = AddRow(row++);
            AddText(departRow, canDepart ? "Ready to depart." : canChoose ? "Select a contract first." : "Cannot depart in this phase.",
                20, TextAnchor.MiddleLeft, 16, 420);

            if (canDepart)
            {
                AddButton(departRow, "Depart", 700, 140, () =>
                {
                    MissionDirector.Instance?.RequestDepartRpc();
                    m_Focus?.Close();
                });
            }

            SetContentHeight(row);
        }

        // -------------------------------------------------------------- character preview

        /// <summary>
        /// Turns on the render-texture preview beside the wardrobe list and narrows the
        /// content area to make room. The panel is deliberately its own strip rather than a
        /// row in the list, because the skin picker will live under it.
        /// </summary>
        void ShowCharacterPreview()
        {
            var preview = CharacterPreview.Instance;
            if (preview == null || m_PreviewPanel == null)
            {
                SetPreviewVisible(false);
                return;
            }

            var texture = preview.Show();
            if (texture == null)
            {
                SetPreviewVisible(false);
                return;
            }

            m_PreviewImage.texture = texture;
            SetPreviewVisible(true);
            BuildCharacterPicker(preview);
        }

        /// <summary>
        /// The row of character buttons under the portrait. Rebuilt each time the wardrobe
        /// opens so it always matches the roster and the player's current choice.
        /// </summary>
        void BuildCharacterPicker(CharacterPreview preview)
        {
            if (m_PickerRow == null)
            {
                return;
            }

            for (var i = m_PickerRow.childCount - 1; i >= 0; i--)
            {
                Destroy(m_PickerRow.GetChild(i).gameObject);
            }

            var skin = CharacterSkin.Local;
            var roster = preview.Roster;

            if (roster == null || roster.Count == 0)
            {
                m_PreviewCaption.text = "No character roster assigned.";
                return;
            }

            var selected = skin != null ? skin.Selected : 0;
            preview.ShowIndex(selected);

            var entry = roster.Get(selected);
            m_PreviewCaption.text = entry.rigged
                ? entry.displayName
                : $"{entry.displayName}\n<size=12>no skeleton - cannot animate</size>";

            var width = (PreviewWidth - 24f - (roster.Count - 1) * 8f) / roster.Count;

            for (var i = 0; i < roster.Count; i++)
            {
                var index = i;
                var option = roster.Get(i);
                var isCurrent = i == selected;

                var go = new GameObject($"Pick_{option.id}",
                    typeof(RectTransform), typeof(Image), typeof(Button));
                var rect = (RectTransform)go.transform;
                rect.SetParent(m_PickerRow, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, 0f);
                rect.anchoredPosition = new Vector2(12f + i * (width + 8f), 0f);

                go.GetComponent<Image>().color = isCurrent
                    ? new Color(0.85f, 0.66f, 0.32f, 0.9f)
                    : new Color(0.16f, 0.18f, 0.22f, 0.95f);

                var label = AddText(rect, option.displayName, 14, TextAnchor.MiddleCenter, 0f, width,
                    isCurrent ? new Color(0.1f, 0.09f, 0.07f) : Color.white);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(2f, 2f);
                label.rectTransform.offsetMax = new Vector2(-2f, -2f);

                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    // Change the body first; the preview follows the replicated value so
                    // the picture and the player never disagree.
                    CharacterSkin.Local?.Select(index);
                    preview.ShowIndex(index);
                    BuildCharacterPicker(preview);
                });
            }
        }

        void SetPreviewVisible(bool visible)
        {
            if (m_PreviewPanel != null)
            {
                m_PreviewPanel.gameObject.SetActive(visible);
            }

            // The list shares the panel with the preview, so it has to give up the space.
            if (m_Viewport != null)
            {
                m_Viewport.offsetMax = new Vector2(visible ? -PreviewWidth - 36f : -20f,
                    m_Viewport.offsetMax.y);
            }
        }

        // ------------------------------------------------------------------ row helpers

        int GetQuantity(int itemId) => m_BuyQuantity.TryGetValue(itemId, out var q) ? Mathf.Max(1, q) : 1;

        void ChangeQuantity(int itemId, int delta)
        {
            m_BuyQuantity[itemId] = Mathf.Clamp(GetQuantity(itemId) + delta, 1, 99);
            Redraw();
        }

        RectTransform AddRow(int row)
        {
            var go = new GameObject($"Row_{row:00}", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Content, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight - 4);
            rect.anchoredPosition = new Vector2(0f, -row * RowHeight);

            go.GetComponent<Image>().color = row % 2 == 0
                ? new Color(1f, 1f, 1f, 0.03f)
                : new Color(1f, 1f, 1f, 0.06f);

            return rect;
        }

        void AddHeaderRow(int row, string label)
        {
            var rowObject = AddRow(row);
            rowObject.GetComponent<Image>().color = new Color(0.9f, 0.7f, 0.35f, 0.16f);
            AddText(rowObject, label, 17, TextAnchor.MiddleLeft, 16, 500,
                new Color(0.95f, 0.78f, 0.45f));
        }

        void AddLabelRow(int row, string label)
        {
            var rowObject = AddRow(row);
            AddText(rowObject, label, 19, TextAnchor.MiddleLeft, 16, 700,
                new Color(0.7f, 0.7f, 0.7f));
        }

        static Text AddText(RectTransform parent, string content, int size, TextAnchor anchor,
            float x, float width, Color? color = null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(x, 0f);
            rect.offsetMax = new Vector2(x + width, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            return text;
        }

        void AddButton(RectTransform parent, string label, float x, float width,
            UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, RowHeight - 14);
            rect.anchoredPosition = new Vector2(x, 0f);

            go.GetComponent<Image>().color = new Color(0.24f, 0.28f, 0.33f, 0.95f);

            var text = AddText(rect, label, 18, TextAnchor.MiddleCenter, 0f, width);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            go.GetComponent<Button>().onClick.AddListener(action);
        }

        void SetContentHeight(int rows)
        {
            var visible = m_Panel.sizeDelta.y - 190f;
            m_Content.sizeDelta = new Vector2(0f, Mathf.Max(rows * RowHeight, visible));
        }

        /// <summary>
        /// Each screen sizes and places its own panel. The market sits to one side so the
        /// vendor stays on camera; the wardrobe and mission computer take the whole screen.
        /// </summary>
        void SetPanelLayout(float width, float height, float offsetX, float dimAlpha)
        {
            m_Panel.sizeDelta = new Vector2(width, height);
            m_Panel.anchoredPosition = new Vector2(offsetX, 0f);

            if (m_Dim != null)
            {
                m_Dim.color = new Color(0f, 0f, 0f, dimAlpha);
            }
        }

        /// <summary>Pins a label across the top of the panel so it follows a resize.</summary>
        static void StretchTop(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-56f, height);
            rect.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>
        /// The character strip down the right of the panel. Hidden by default; only the
        /// wardrobe turns it on, and the skin picker will sit under the portrait.
        /// </summary>
        void BuildPreviewPanel()
        {
            var panel = new GameObject("CharacterPreviewPanel", typeof(RectTransform), typeof(Image));
            m_PreviewPanel = (RectTransform)panel.transform;
            m_PreviewPanel.SetParent(m_Panel, false);
            m_PreviewPanel.anchorMin = new Vector2(1f, 0f);
            m_PreviewPanel.anchorMax = new Vector2(1f, 1f);
            m_PreviewPanel.pivot = new Vector2(1f, 0.5f);
            m_PreviewPanel.sizeDelta = new Vector2(PreviewWidth, -176f);
            m_PreviewPanel.anchoredPosition = new Vector2(-20f, -28f);
            panel.GetComponent<Image>().color = new Color(0.05f, 0.055f, 0.07f, 0.9f);

            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            var portraitRect = (RectTransform)portrait.transform;
            portraitRect.SetParent(m_PreviewPanel, false);
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(10f, 98f);
            portraitRect.offsetMax = new Vector2(-10f, -10f);

            m_PreviewImage = portrait.GetComponent<RawImage>();
            m_PreviewImage.raycastTarget = false;

            m_PreviewCaption = AddText(m_PreviewPanel, "Your character", 16,
                TextAnchor.MiddleCenter, 0f, PreviewWidth, new Color(0.78f, 0.8f, 0.84f));
            var captionRect = m_PreviewCaption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(1f, 0f);
            captionRect.pivot = new Vector2(0.5f, 0f);
            captionRect.sizeDelta = new Vector2(-20f, 44f);
            captionRect.anchoredPosition = new Vector2(0f, 8f);
            m_PreviewCaption.supportRichText = true;

            var picker = new GameObject("PickerRow", typeof(RectTransform));
            m_PickerRow = (RectTransform)picker.transform;
            m_PickerRow.SetParent(m_PreviewPanel, false);
            m_PickerRow.anchorMin = new Vector2(0f, 0f);
            m_PickerRow.anchorMax = new Vector2(1f, 0f);
            m_PickerRow.pivot = new Vector2(0.5f, 0f);
            m_PickerRow.sizeDelta = new Vector2(0f, 38f);
            m_PickerRow.anchoredPosition = new Vector2(0f, 54f);

            panel.SetActive(false);
        }

        // ------------------------------------------------------------------- chrome

        void BuildChrome()
        {
            var canvasObject = new GameObject("StationScreenCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            m_Canvas = canvasObject.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Dimmer behind the panel.
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var dimRect = (RectTransform)dim.transform;
            dimRect.SetParent(canvasObject.transform, false);
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            m_Dim = dim.GetComponent<Image>();
            m_Dim.color = new Color(0f, 0f, 0f, 0.55f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            m_Panel = (RectTransform)panel.transform;
            m_Panel.SetParent(canvasObject.transform, false);
            m_Panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            m_Panel.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.12f, 0.97f);

            m_Title = AddText(m_Panel, "STATION", 32, TextAnchor.UpperLeft, 28, PanelWidth - 56);
            StretchTop(m_Title.rectTransform, -24f, 44f);

            m_Subtitle = AddText(m_Panel, string.Empty, 17, TextAnchor.UpperLeft, 28, PanelWidth - 56,
                new Color(0.7f, 0.72f, 0.75f));
            StretchTop(m_Subtitle.rectTransform, -70f, 44f);

            // Scrollable body.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(m_Panel, false);
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(20f, 60f);
            viewportRect.offsetMax = new Vector2(-20f, -116f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = true;

            var content = new GameObject("Content", typeof(RectTransform));
            m_Content = (RectTransform)content.transform;
            m_Content.SetParent(viewportRect, false);
            m_Content.anchorMin = new Vector2(0f, 1f);
            m_Content.anchorMax = new Vector2(1f, 1f);
            m_Content.pivot = new Vector2(0.5f, 1f);
            m_Content.anchoredPosition = Vector2.zero;

            m_Viewport = viewportRect;

            BuildPreviewPanel();

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.content = m_Content;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            m_Footer = AddText(m_Panel, string.Empty, 16, TextAnchor.MiddleLeft, 28, PanelWidth - 56,
                new Color(0.62f, 0.64f, 0.66f));
            m_Footer.rectTransform.anchorMin = new Vector2(0f, 0f);
            m_Footer.rectTransform.anchorMax = new Vector2(1f, 0f);
            m_Footer.rectTransform.pivot = new Vector2(0.5f, 0f);
            m_Footer.rectTransform.sizeDelta = new Vector2(-220f, 40f);
            m_Footer.rectTransform.anchoredPosition = new Vector2(-82f, 14f);

            var close = new GameObject("CloseHint", typeof(RectTransform), typeof(Image), typeof(Button));
            var closeRect = (RectTransform)close.transform;
            closeRect.SetParent(m_Panel, false);
            closeRect.anchorMin = new Vector2(1f, 0f);
            closeRect.anchorMax = new Vector2(1f, 0f);
            closeRect.pivot = new Vector2(1f, 0f);
            closeRect.sizeDelta = new Vector2(150f, 38f);
            closeRect.anchoredPosition = new Vector2(-24f, 14f);
            close.GetComponent<Image>().color = new Color(0.3f, 0.2f, 0.2f, 0.95f);

            var closeText = AddText(closeRect, "Close  (E)", 17, TextAnchor.MiddleCenter, 0f, 150f);
            closeText.rectTransform.anchorMin = Vector2.zero;
            closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = Vector2.zero;
            closeText.rectTransform.offsetMax = Vector2.zero;

            close.GetComponent<Button>().onClick.AddListener(() => m_Focus?.Close());
        }
    }
}
