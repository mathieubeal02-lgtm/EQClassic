using System.Collections.Generic;
using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// PERSONA, the Trilogy inventory, assembled from the client's own pieces (main3.bmp: the inventory
    /// column with the eight GENERAL slots and the statistics column; main4.bmp: the worn slot strips and
    /// the money) over the whole screen, with the items' icons (dragitem01-04.bmp). A click picks an item
    /// up and puts it down (the server checks it fits); a right click opens a bag, eats or drinks, or
    /// scribes a spell scroll. Places are measured on the 640 × 480 art.
    /// </summary>
    public sealed partial class EQClassicClient
    {
        // The worn slots of main4's strips (source rectangles) and the inventory slot each one is.
        private static readonly (Rect Source, int Slot)[] WornSlots = BuildWornSlots();

        private static (Rect, int)[] BuildWornSlots()
        {
            var list = new List<(Rect, int)>();
            void Row(float y, float h, float step, int[] slots)
            {
                for (int i = 0; i < slots.Length; i++)
                    list.Add((new Rect(273 + step * i, y, 46, h), slots[i]));
            }
            Row(6, 52, 50f, new[] { 1, 5, 3, 2, 4 });              // ear, neck, face, head, ear
            Row(69, 55, 49.7f, new[] { 15, 9, 7, 12, 10, 16 });    // finger, wrist, arms, hands, wrist, finger
            Row(135, 55, 49.7f, new[] { 6, 17, 8, 20, 18, 19 });   // shoulders, chest, back, waist, legs, feet
            Row(201, 55, 50.5f, new[] { 13, 14, 11, 21 });         // primary, secondary, range, ammo
            return list.ToArray();
        }

        private static readonly Rect[] GeneralSlots =
        {
            new Rect(133, 255, 46, 45), new Rect(182, 255, 46, 45),
            new Rect(133, 303, 46, 45), new Rect(182, 303, 46, 45),
            new Rect(133, 351, 46, 45), new Rect(182, 351, 46, 45),
            new Rect(133, 399, 46, 45), new Rect(182, 399, 46, 45),
        };

        private int? _openBag; // the general slot whose bag is shown under the strips

        /// <summary>True when drawn (the art is there and no merchant, bank or trade needs the list with its buttons).</summary>
        private bool DrawPersona(ClassicSkin skin)
        {
            var inventory = _client.Inventory;
            if (skin.Persona == null || skin.Strips == null || inventory == null
                || _client.Merchant != null || _client.Bank != null || _client.Trade != null)
                return false;
            float sx = Screen.width / ClassicSkin.Width, sy = Screen.height / ClassicSkin.Height;
            Rect C(float x, float y, float w, float h) => new Rect(x * sx, y * sy, w * sx, h * sy);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.08f, 0.1f, 1f));
            // The pieces: statistics column, inventory column, the four strips, the money.
            void Piece(Texture2D sheet, float srcX, float srcY, float w, float h, float atX, float atY) =>
                GUI.DrawTextureWithTexCoords(C(atX, atY, w, h), sheet, ClassicSkin.Uv(srcX, srcY, w, h));
            Piece(skin.Persona, 241, 26, 104, 414, 6, 30);
            Piece(skin.Persona, 122, 0, 118, 472, 116, 4);
            Piece(skin.Strips, 270, 2, 254, 60, 240, 10);
            Piece(skin.Strips, 270, 64, 302, 64, 240, 74);
            Piece(skin.Strips, 270, 130, 302, 66, 240, 142);
            Piece(skin.Strips, 270, 196, 206, 64, 240, 212);
            Piece(skin.Strips, 190, 390, 300, 70, 240, 290);

            // The statistics we know: level, hit points, the next level's bar.
            GUI.Label(C(12, 6, 220, 20), CharacterName, _smallBold);
            if (_client.Experience is { } xp)
            {
                GUI.Label(C(56, 50, 50, 16), xp.Level.ToString(), _smallBold);
                DrawBar(C(20, 186, 76, 6), xp.Fraction, new Color(0.9f, 0.6f, 0.1f), "");
            }
            GUI.Label(C(56, 86, 50, 16), $"{_client.Hp}/{_client.MaxHp}", _smallBold);

            string hovered = null;
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            // Worn slots (main4 strips placed at 240 − 270 = −30 across, and at their rows).
            foreach (var (source, slot) in WornSlots)
            {
                float dy = source.y < 64 ? 8 : source.y < 130 ? 10 : source.y < 196 ? 12 : 16;
                var r = C(source.x - 30, source.y + dy, source.width, source.height);
                ItemCell(skin, r, slot, ItemIn(inventory, slot), ref hovered, mouse);
            }
            // General slots (the inventory column moved by −6, +4).
            for (int i = 0; i < GeneralSlots.Length; i++)
            {
                var s = GeneralSlots[i];
                int slot = PlayerInventory.FirstGeneral + i;
                ItemCell(skin, C(s.x - 6, s.y + 4, s.width, s.height), slot, ItemIn(inventory, slot), ref hovered, mouse);
            }
            // An open bag: its cells in a row under the strips.
            if (_openBag is int bag && ItemIn(inventory, bag) is { BagSlots: > 0 } bagItem)
            {
                GUI.Label(C(240, 366, 380, 16), bagItem.Name, _smallBold);
                for (int cell = 0; cell < bagItem.BagSlots && cell < PlayerInventory.BagCells; cell++)
                {
                    int bagSlot = PlayerInventory.BagSlotBase + (bag - PlayerInventory.FirstGeneral) * PlayerInventory.BagCells + cell;
                    var r = C(240 + 39 * cell, 384, 37, 37);
                    Fill(r, new Color(0.18f, 0.18f, 0.22f, 1f));
                    ItemCell(skin, r, bagSlot, ItemIn(inventory, bagSlot), ref hovered, mouse);
                }
            }
            else
                _openBag = null;
            // Money in the strip's boxes (moved by +50, −100).
            GUI.Label(C(254, 316, 60, 16), inventory.Platinum.ToString(), _smallBold);
            GUI.Label(C(328, 316, 60, 16), inventory.Gold.ToString(), _smallBold);
            GUI.Label(C(402, 316, 60, 16), inventory.Silver.ToString(), _smallBold);
            GUI.Label(C(474, 316, 60, 16), inventory.Copper.ToString(), _smallBold);
            // DONE (the inventory column's button) closes.
            if (Hit(C(152, 454, 46, 18)))
            {
                _heldSlot = null;
                ToggleWindow(HudLayout.Inventory);
            }
            // What is under the mouse, what is held.
            string info = _heldSlot is int held ? $"Holding: {HeldName(inventory, held)}" : hovered ?? "Click an item to pick it up; right click opens bags, eats, drinks, scribes.";
            GUI.Label(C(240, 440, 390, 18), info, _small);
            if (_heldSlot is int h2 && ItemIn(inventory, h2) is { ItemId: not 0 } heldItem && ItemIcon(skin, heldItem.Icon) is { } icon)
                GUI.DrawTextureWithTexCoords(new Rect(mouse.x + 6, mouse.y + 6, 40 * sx, 40 * sy), icon.Sheet, icon.Uv);
            return true;
        }

        /// <summary>A slot: its item's icon; click to pick up or put down, right click to use.</summary>
        private void ItemCell(ClassicSkin skin, Rect r, int slot, ItemView item, ref string hovered, Vector2 mouse)
        {
            if (item.ItemId != 0 && ItemIcon(skin, item.Icon) is { } icon)
            {
                float size = Mathf.Min(r.width, r.height) * 0.86f;
                var at = new Rect(r.x + (r.width - size) / 2, r.y + (r.height - size) / 2, size, size);
                GUI.DrawTextureWithTexCoords(at, icon.Sheet, icon.Uv);
                if (item.Charges > 1)
                    GUI.Label(new Rect(r.x + 2, r.yMax - 16, r.width, 16), item.Charges.ToString(), _small);
            }
            if (_heldSlot == slot)
                Fill(r, new Color(1f, 1f, 0.6f, 0.25f));
            if (r.Contains(mouse) && item.ItemId != 0)
                hovered = item.Charges > 1 ? $"{item.Name} ({item.Charges})" : item.Name;
            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition))
            {
                e.Use();
                if (item.BagSlots > 0 && PlayerInventory.IsGeneralSlot(slot))
                    _openBag = _openBag == slot ? (int?)null : slot;
                else if (item.ItemType == 14 || item.ItemType == 15)
                    _client.Consume(slot);
                else if (item.Name.StartsWith("Spell: "))
                    _client.Scribe(slot);
                return;
            }
            if (!Hit(r))
                return;
            if (_heldSlot is int from)
            {
                if (from != slot)
                    _client.MoveItem(from, slot);
                _heldSlot = null;
            }
            else if (item.ItemId != 0)
                _heldSlot = slot;
        }

        /// <summary>An item's icon: dragitem01-04, 192 icons of 40 × 40 each, twelve to a column; icon − 500.</summary>
        private static (Texture2D Sheet, Rect Uv)? ItemIcon(ClassicSkin skin, int icon)
        {
            int k = icon - 500;
            if (k < 0 || skin.ItemIcons == null)
                return null;
            int sheet = k / 192, index = k % 192;
            if (sheet >= skin.ItemIcons.Length || skin.ItemIcons[sheet] == null)
                return null;
            return (skin.ItemIcons[sheet], ClassicSkin.Uv(40 * (index / 12), 40 * (index % 12), 40, 40));
        }
    }
}
