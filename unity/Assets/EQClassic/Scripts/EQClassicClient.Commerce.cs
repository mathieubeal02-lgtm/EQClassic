using System.Collections.Generic;
using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The Trilogy merchant, bank, trade and loot windows in the classic frame, from the client's own
    /// pieces: main9.bmp (the player's GENERAL column, the bank's vault, the merchant's ITEMS FOR SALE,
    /// SELL), main6.bmp (the trade window) and main5.bmp (the corpse's sixteen cells). The items show
    /// their icons; clicks buy, sell, move, offer and take. Places are measured on the 640 × 480 art.
    /// </summary>
    public sealed partial class EQClassicClient
    {
        private int _merchantTop;          // first row of goods shown (two a row)
        private int? _consideredGood;      // a merchant's good picked
        private int? _consideredOwn;       // one of the player's items picked to sell

        /// <summary>main9's GENERAL column: the player's eight general slots, placed with its left edge at <paramref name="atX"/>.</summary>
        private void GeneralColumn(ClassicSkin skin, float atX, System.Func<float, float, float, float, Rect> C, ref string hovered, Vector2 mouse,
            System.Action<int, ItemView> click)
        {
            var inventory = _client.Inventory;
            GUI.DrawTextureWithTexCoords(C(atX, 4, 120, 472), skin.Commerce, ClassicSkin.Uv(0, 0, 120, 472));
            if (inventory == null)
                return;
            for (int i = 0; i < 8; i++)
            {
                int slot = PlayerInventory.FirstGeneral + i;
                var r = C(atX + (i % 2 == 0 ? 14 : 62), 4 + 255 + 48 * (i / 2), 45, 45);
                var item = ItemIn(inventory, slot);
                DrawIcon(skin, r, item);
                if (r.Contains(mouse) && item.ItemId != 0)
                    hovered = item.Name;
                if (Hit(r))
                    click(slot, item);
            }
        }

        private void DrawIcon(ClassicSkin skin, Rect r, ItemView item)
        {
            if (item.ItemId == 0 || ItemIcon(skin, item.Icon) is not { } icon)
                return;
            float size = Mathf.Min(r.width, r.height) * 0.86f;
            GUI.DrawTextureWithTexCoords(new Rect(r.x + (r.width - size) / 2, r.y + (r.height - size) / 2, size, size), icon.Sheet, icon.Uv);
            if (item.Charges > 1)
                GUI.Label(new Rect(r.x + 2, r.yMax - 16, r.width, 16), item.Charges.ToString(), _small);
        }

        private System.Func<float, float, float, float, Rect> Canvas()
        {
            float sx = Screen.width / ClassicSkin.Width, sy = Screen.height / ClassicSkin.Height;
            return (x, y, w, h) => new Rect(x * sx, y * sy, w * sx, h * sy);
        }

        /// <summary>The merchant: ITEMS FOR SALE (click, then PURCHASE), the player's items (click, then SELL).</summary>
        private bool DrawMerchantArt(ClassicSkin skin, MerchantGoods merchant)
        {
            if (skin.Commerce == null)
                return false;
            var C = Canvas();
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.08f, 0.1f, 1f));
            string hovered = null;
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            // ITEMS FOR SALE (main9 x 242), placed at 260.
            GUI.DrawTextureWithTexCoords(C(260, 4, 118, 472), skin.Commerce, ClassicSkin.Uv(242, 0, 118, 472));
            int rows = (merchant.Items.Count + 1) / 2;
            _merchantTop = Mathf.Clamp(_merchantTop, 0, System.Math.Max(0, rows - 6));
            for (int i = 0; i < 12; i++)
            {
                int index = _merchantTop * 2 + i;
                var r = C(260 + (i % 2 == 0 ? 10 : 58), 4 + 48 + 48 * (i / 2), 45, 45);
                if (index >= merchant.Items.Count)
                    continue;
                var good = merchant.Items[index];
                DrawIcon(skin, r, good);
                if (_consideredGood == index)
                    Fill(r, new Color(1f, 1f, 0.6f, 0.25f));
                if (r.Contains(mouse))
                    hovered = $"{good.Name}  {MerchantRules.Coins(MerchantRules.BuyPrice(good.Price))}";
                if (Hit(r))
                {
                    _consideredGood = index;
                    _consideredOwn = null;
                }
            }
            if (Hit(C(275, 347, 36, 32))) _merchantTop--;
            if (Hit(C(323, 347, 36, 32))) _merchantTop++;
            var considered = C(295, 404, 46, 46);
            if (_consideredGood is int g && g < merchant.Items.Count)
                DrawIcon(skin, considered, merchant.Items[g]);
            if (Hit(C(280, 454, 76, 18)) && _consideredGood is int buy && buy < merchant.Items.Count)
                _client.Buy(buy);

            // The player's GENERAL column at 400, SELL under the information.
            var inventory = _client.Inventory;
            GeneralColumn(skin, 400, C, ref hovered, mouse, (slot, item) =>
            {
                if (item.ItemId == 0)
                    return;
                _consideredOwn = slot;
                _consideredGood = null;
            });
            if (_consideredOwn is int own && inventory != null)
                DrawIcon(skin, considered, ItemIn(inventory, own));
            GUI.DrawTextureWithTexCoords(C(140, 400, 76, 15), skin.Commerce, ClassicSkin.Uv(362, 82, 76, 15));
            if (Hit(C(140, 400, 76, 15)) && _consideredOwn is int sell)
            {
                _client.Sell(sell);
                _consideredOwn = null;
            }
            if (Hit(C(438, 456, 46, 16)))
            {
                _client.CloseMerchant();
                _layout.SetOpen(HudLayout.Inventory, false);
            }

            // Information on the left: the merchant, what is considered and its price, the purse.
            GUI.Label(C(12, 12, 240, 20), _client.Zone?.Get(merchant.NpcId)?.DisplayName ?? "Merchant", _smallBold);
            string about = _consideredGood is int cg && cg < merchant.Items.Count
                ? $"{merchant.Items[cg].Name}\nPrice: {MerchantRules.Coins(MerchantRules.BuyPrice(merchant.Items[cg].Price))}\nPURCHASE to buy it."
                : _consideredOwn is int co && inventory != null && ItemIn(inventory, co) is { ItemId: not 0 } mine
                    ? $"{mine.Name}\nThe merchant pays {MerchantRules.Coins(MerchantRules.SellPrice(mine.Price))}\nSELL to sell it."
                    : "Click an item for sale, or one of yours.";
            GUI.Label(C(12, 40, 240, 80), about, new GUIStyle(_small) { wordWrap = true });
            if (inventory != null)
                GUI.Label(C(12, 440, 240, 20), $"You have {inventory.Platinum}p {inventory.Gold}g {inventory.Silver}s {inventory.Copper}c", _small);
            if (hovered != null)
                GUI.Label(C(12, 130, 240, 40), hovered, new GUIStyle(_small) { wordWrap = true });
            return true;
        }

        /// <summary>The bank: the vault's eight slots and money (main9), the player's general slots; click to move, coins to move money.</summary>
        private bool DrawBankArt(ClassicSkin skin, BankContents bank)
        {
            if (skin.Commerce == null)
                return false;
            var C = Canvas();
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.08f, 0.1f, 1f));
            string hovered = null;
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            var inventory = _client.Inventory;
            GUI.DrawTextureWithTexCoords(C(260, 4, 118, 472), skin.Commerce, ClassicSkin.Uv(122, 0, 118, 472));
            for (int i = 0; i < 8 && i < bank.Slots.Count; i++)
            {
                int slot = BankContents.BankBase + i;
                var r = C(260 + (i % 2 == 0 ? 11 : 61), 4 + 90 + 48 * (i / 2), 45, 45);
                DrawIcon(skin, r, bank.Slots[i]);
                if (_heldSlot == slot)
                    Fill(r, new Color(1f, 1f, 0.6f, 0.25f));
                if (r.Contains(mouse) && bank.Slots[i].ItemId != 0)
                    hovered = bank.Slots[i].Name;
                if (Hit(r))
                    PickOrPut(slot, bank.Slots[i]);
            }
            // The vault's money: a click takes that coin out.
            int[] held = { bank.Platinum, bank.Gold, bank.Silver, bank.Copper };
            for (int c = 0; c < 4; c++)
            {
                var r = C(260 + 38, 4 + 322 + 36 * c, 66, 18);
                GUI.Label(r, held[c].ToString(), _smallBold);
                if (Hit(r) && held[c] > 0)
                    _client.BankMoney(0, 0, 0, 0, c == 0 ? held[c] : 0, c == 1 ? held[c] : 0, c == 2 ? held[c] : 0, c == 3 ? held[c] : 0);
            }
            GeneralColumn(skin, 400, C, ref hovered, mouse, PickOrPut);
            if (Hit(C(438, 456, 46, 16)))
            {
                _heldSlot = null;
                _client.CloseBank();
                _layout.SetOpen(HudLayout.Inventory, false);
            }
            GUI.Label(C(12, 12, 240, 20), "Bank", _smallBold);
            if (inventory != null)
            {
                // The purse: a click puts that coin in the vault.
                int[] mine = { inventory.Platinum, inventory.Gold, inventory.Silver, inventory.Copper };
                string[] names = { "platinum", "gold", "silver", "copper" };
                for (int c = 0; c < 4; c++)
                {
                    var r = C(12, 60 + 22 * c, 200, 20);
                    if (Hit(r) && mine[c] > 0)
                        _client.BankMoney(c == 0 ? mine[c] : 0, c == 1 ? mine[c] : 0, c == 2 ? mine[c] : 0, c == 3 ? mine[c] : 0, 0, 0, 0, 0);
                    GUI.Label(r, $"{mine[c]} {names[c]}", _small);
                }
                GUI.Label(C(12, 40, 240, 20), "Your money (click to deposit):", _small);
            }
            GUI.Label(C(12, 170, 240, 60), _heldSlot is int h && inventory != null ? $"Holding {HeldName(inventory, h)}" : hovered ?? "Click an item, then where it goes; the vault's coins to take them.",
                new GUIStyle(_small) { wordWrap = true });
            return true;
        }

        private void PickOrPut(int slot, ItemView item)
        {
            if (_heldSlot is int from)
            {
                if (from != slot)
                    _client.MoveItem(from, slot);
                _heldSlot = null;
            }
            else if (item.ItemId != 0)
                _heldSlot = slot;
        }

        /// <summary>The trade window (main6): both offers of eight, money, TRADE and CANCEL; a click on a general slot offers it.</summary>
        private bool DrawTradeArt(ClassicSkin skin, TradeWindow trade)
        {
            if (skin.Trading == null || skin.Commerce == null)
                return false;
            var C = Canvas();
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.08f, 0.1f, 1f));
            string hovered = null;
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            // main6's trade window: 402..620 across, the buttons under it.
            GUI.DrawTextureWithTexCoords(C(160, 20, 218, 320), skin.Trading, ClassicSkin.Uv(402, 0, 218, 320));
            GUI.DrawTextureWithTexCoords(C(160, 340, 138, 140), skin.Trading, ClassicSkin.Uv(402, 320, 138, 140));
            void Offer(IReadOnlyList<ItemView> items, float x0)
            {
                for (int i = 0; i < 8; i++)
                {
                    var r = C(x0 + (i % 2 == 0 ? 10 : 58), 20 + 30 + 48 * (i / 2), 45, 45);
                    if (i >= items.Count)
                        continue;
                    DrawIcon(skin, r, items[i]);
                    if (r.Contains(mouse) && items[i].ItemId != 0)
                        hovered = items[i].Name;
                }
            }
            Offer(trade.Mine.Items, 160);
            Offer(trade.Theirs.Items, 272);
            GUI.Label(C(172, 248, 90, 60), MerchantRules.Coins(trade.Mine.Copper), new GUIStyle(_small) { wordWrap = true });
            GUI.Label(C(284, 248, 90, 60), MerchantRules.Coins(trade.Theirs.Copper), new GUIStyle(_small) { wordWrap = true });
            GUI.Label(C(172, 22, 100, 18), trade.Mine.Accepted ? "You (accepted)" : "You", _small);
            GUI.Label(C(284, 22, 100, 18), trade.Theirs.Accepted ? $"{trade.Partner} (accepted)" : trade.Partner, _small);
            if (Hit(C(188, 360, 64, 64))) _client.AcceptTrade();
            if (Hit(C(194, 450, 60, 28))) _client.CancelTrade();
            GeneralColumn(skin, 400, C, ref hovered, mouse, (slot, item) =>
            {
                if (item.ItemId != 0)
                    _client.OfferItem(slot);
            });
            // Money: platinum, gold, silver, copper, then Offer.
            GUI.Label(C(12, 12, 140, 20), $"Trading with {trade.Partner}", _smallBold);
            GUI.Label(C(12, 40, 140, 20), "Money p / g / s / c", _small);
            _tradePlatinum = GUI.TextField(C(12, 60, 32, 18), _tradePlatinum);
            _tradeGold = GUI.TextField(C(48, 60, 32, 18), _tradeGold);
            _tradeSilver = GUI.TextField(C(84, 60, 32, 18), _tradeSilver);
            _tradeCopper = GUI.TextField(C(120, 60, 32, 18), _tradeCopper);
            if (GUI.Button(C(12, 82, 140, 18), "Offer money"))
                _client.OfferCoins(Number(_tradePlatinum), Number(_tradeGold), Number(_tradeSilver), Number(_tradeCopper));
            GUI.Label(C(12, 120, 140, 80), hovered ?? "Click your items (right) to offer them; TRADE when both sides are right.", new GUIStyle(_small) { wordWrap = true });
            return true;
        }

        /// <summary>The corpse's cells (main5's column of sixteen) over the right column: a click takes the item.</summary>
        private void DrawLootArt(ClassicSkin skin, int corpse)
        {
            if (skin.Social == null)
                return;
            var C = Canvas();
            GUI.DrawTextureWithTexCoords(C(520, 4, 118, 470), skin.Social, ClassicSkin.Uv(416, 0, 118, 470));
            GUI.Label(C(524, 8, 110, 16), _client.Zone?.Get(corpse)?.DisplayName ?? "Corpse", _small);
            var items = _client.LootItems;
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            string hovered = null;
            for (int i = 0; i < 16 && i < items.Count; i++)
            {
                var r = C(520 + (i % 2 == 0 ? 10 : 58), 4 + 38 + 48 * (i / 2), 45, 45);
                DrawIcon(skin, r, items[i]);
                if (r.Contains(mouse))
                    hovered = items[i].Charges > 1 ? $"{items[i].Name} ({items[i].Charges})" : items[i].Name;
                if (Hit(r))
                    _client.TakeLoot(i);
            }
            if (GUI.Button(C(560, 440, 60, 18), "Done"))
                _client.EndLoot();
            if (hovered != null)
            {
                var at = C(380, 420, 138, 36);
                Fill(at, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(at, hovered, new GUIStyle(_small) { wordWrap = true });
            }
            _uiRects.Add(C(520, 4, 118, 470));
        }
    }
}
