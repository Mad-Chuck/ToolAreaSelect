using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Enchantments;
using StardewValley.Pathfinding;
using StardewValley.Tools;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ToolAreaSelect {
    internal sealed class ModEntry : Mod
    {
        private int _powerSelected = 0;

        private PathFindController? _pendingPath;
        private Vector2? _pendingTile;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
        }
        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            bool ctrl =
                Helper.Input.IsDown(SButton.LeftControl) ||
                Helper.Input.IsDown(SButton.RightControl);

            if (!ctrl)
                return;

            if (Game1.player.CurrentTool is not WateringCan)
                return;

            var can = (WateringCan) Game1.player.CurrentTool;

            Helper.Input.SuppressScrollWheel();

            if (e.Delta > 0)
                _powerSelected++;
            else if (e.Delta < 0)
                _powerSelected--;
            int maxPower = can.UpgradeLevel;
            bool hasReaching = can.enchantments?.Any(en => en is ReachingToolEnchantment) ?? false;
            if (can.UpgradeLevel == 4 && hasReaching)
                maxPower = 5;
            _powerSelected = Math.Clamp(_powerSelected, 0, maxPower);

            Game1.playSound("shwip");
            Monitor.Log($"Watering power changed: {_powerSelected}/{maxPower}", LogLevel.Debug);
        }
        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (Game1.activeClickableMenu != null)
                return;

            if (Game1.player.CurrentTool is not WateringCan)
                return;
        

            //Game1.player.ActiveObject.drawPlacementBounds(Game1.spriteBatch, currentLocation);

            var mouseTile = Game1.GetPlacementGrabTile();

            // get tiles
            // HACK: call protected method
            Type type = Game1.player.CurrentTool.GetType();
            var output = (List<Vector2>) type.InvokeMember(
                "tilesAffected",
                BindingFlags.Instance | BindingFlags.InvokeMethod | BindingFlags.NonPublic,
                null,
                Game1.player.CurrentTool,
                //new object[] { Game1.player.GetToolLocation() / 64f, _powerSelected, Game1.player }
                new object[] { mouseTile, _powerSelected, Game1.player }
            );

            // for each tile
            foreach (var v in output)
            {
                // draw 
                e.SpriteBatch.Draw(Game1.mouseCursors,
                    Game1.GlobalToLocal(new Vector2((int) v.X * 64, (int) v.Y * 64)),
                    new Rectangle(194, 388, 16, 16),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    4f,
                    SpriteEffects.None,
                    0.01f
                );
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // używamy konewki tylko w grze, nie w menu
            if (Game1.activeClickableMenu != null)
                return;

            // only left mouse button
            if (e.Button != SButton.MouseLeft)
                return;

            if (IsCursorOnUI(e))
                return;

            if (Game1.player.CurrentTool is not WateringCan can)
                return;

            Helper.Input.Suppress(SButton.MouseLeft);

            // pobierz tile kursora
            var tile = Game1.GetPlacementGrabTile();

            // 🟦 NOWY WARUNEK: jeśli klikamy wodę → REFILL bez chodzenia
            if (Game1.currentLocation.isWaterTile((int)tile.X, (int)tile.Y))
            {
                TryRefillWateringCan(can);
                return;
            }

            _pendingTile = tile;

            // uruchom pathfinding
            _pendingPath = new PathFindController(Game1.player, Game1.currentLocation, new Point((int)tile.X, (int)tile.Y), Game1.player.FacingDirection, new PathFindController.endBehavior(OnReachedTile));

            Game1.player.controller = _pendingPath;

            Monitor.Log($"Moving to {new Point((int)tile.X, (int)tile.Y)}", LogLevel.Debug);
        }

        private void OnReachedTile(Character c, GameLocation location)
        {
            // w przyszłości tu odpalisz podlewanie
            Monitor.Log($"Arrived at tile {_pendingTile}", LogLevel.Debug);

            if (Game1.player.CurrentTool is not WateringCan can || _pendingTile is null)
                return;

            _pendingPath = null;

            // Pobierz pola objęte zasięgiem
            Type type = can.GetType();
            var affectedTiles = (List<Vector2>)type.InvokeMember(
                "tilesAffected",
                BindingFlags.Instance | BindingFlags.InvokeMethod | BindingFlags.NonPublic,
                null,
                can,
                new object[] { _pendingTile.Value, _powerSelected, Game1.player }
            );

            // Odtwórz animację + efekt konewki (to robi vanilla kod)
            Game1.player.lastClick = _pendingTile.Value * 64f;
            can.DoFunction(location, (int)_pendingTile.Value.X * 64, (int)_pendingTile.Value.Y * 64, _powerSelected, Game1.player);

            // Ustaw animację „watering”
            Game1.player.jitterStrength = 0.5f;
            Farmer.useTool(Game1.player);

            Monitor.Log($"Watered {affectedTiles.Count} tiles at {_pendingTile}", LogLevel.Debug);

            _pendingTile = null;

        }

        private bool IsCursorOnUI(ButtonPressedEventArgs e)
        {
            // Check if cursor over HUD (np. toolbar, health bar, equipment slots)
            foreach (var menu in Game1.onScreenMenus)
            {
                if (menu.isWithinBounds((int)e.Cursor.ScreenPixels.X, (int)e.Cursor.ScreenPixels.Y))
                    return true;
            }

            return false;
        }

        private void TryRefillWateringCan(WateringCan can)
        {
            if (can.WaterLeft >= can.waterCanMax)
            {
                Game1.playSound("cancel");
                Monitor.Log("Watering can already full.", LogLevel.Debug);
                return;
            }

            can.WaterLeft = can.waterCanMax;
            Game1.playSound("slosh");
            Game1.player.jitterStrength = 0.3f;

            Monitor.Log($"Refilled watering can: {can.WaterLeft}/{can.waterCanMax}", LogLevel.Debug);
        }
    }
}
