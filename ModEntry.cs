using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Pathfinding;
using StardewValley.Tools;
using System.Reflection;

namespace ToolAreaSelect {
    internal sealed class ModEntry : Mod
    {
        private int _powerSelected = 0;

        private PathFindController? _pendingPath;
        private Vector2? _pendingTile;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
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

            if (Game1.player.CurrentTool is not (WateringCan or Hoe))
                return;

            var can = Game1.player.CurrentTool;

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

            if (Game1.player.CurrentTool is not (WateringCan or Hoe))
                return;

            DrawToolArea(e);
        }

        private void DrawToolArea(RenderedWorldEventArgs e)
        {
            Vector2 mouseTile = Game1.GetPlacementGrabTile();
            Vector2 toolTile = GetPlacementToolTile();
            // HACK: call protected method
            var tiles = (List<Vector2>)Game1.player.CurrentTool.GetType().InvokeMember(
                    "tilesAffected",
                    BindingFlags.Instance | BindingFlags.InvokeMethod | BindingFlags.NonPublic,
                    null,
                    Game1.player.CurrentTool,
                    new object[] { toolTile, _powerSelected, Game1.player }
                );

            // draw red border tile
            e.SpriteBatch.Draw(
                Game1.mouseCursors,
                Game1.GlobalToLocal(new Vector2((int)mouseTile.X * 64, (int)mouseTile.Y * 64)),
                new Rectangle(448, 128, 64, 64),
                Color.White,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0.01f
            );
            foreach (var v in tiles)
            {
                // draw green tile
                e.SpriteBatch.Draw(
                    Game1.mouseCursors,
                    Game1.GlobalToLocal(new Vector2((int)v.X * 64, (int)v.Y * 64)),
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
            if (Game1.activeClickableMenu != null)
                return;
            if (e.Button != SButton.MouseLeft)
                return;
            if (IsCursorOnUI(e))
                return;
            if (Game1.player.CurrentTool is not (WateringCan or Hoe))
                return;

            Helper.Input.Suppress(SButton.MouseLeft);

            var tile = Game1.GetPlacementGrabTile();

            if (Game1.player.CurrentTool is WateringCan && Game1.currentLocation.isWaterTile((int)tile.X, (int)tile.Y))
            {
                TryRefillWateringCan();
                return;
            }

            _pendingTile = tile;
            _pendingPath = new PathFindController(Game1.player, Game1.currentLocation, new Point((int)tile.X, (int)tile.Y), Game1.player.FacingDirection, new PathFindController.endBehavior(OnReachedTile));
            Game1.player.controller = _pendingPath;

            Monitor.Log($"Moving to {new Point((int)tile.X, (int)tile.Y)}", LogLevel.Debug);
        }

        private void OnReachedTile(Character c, GameLocation location)
        {
            Monitor.Log($"Arrived at tile {_pendingTile}", LogLevel.Debug);

            if (Game1.player.CurrentTool is not (WateringCan or Hoe) || _pendingTile is null)
                return;

            _pendingPath = null;
            var pendingTile = _pendingTile;
            _pendingTile = null;

            // watering logic
            Game1.player.toolPower.Value = _powerSelected;      // Ensures correct power since it's read from player
            Farmer.useTool(Game1.player);

            Monitor.Log($"Tool used on {pendingTile}", LogLevel.Debug);
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

        private Vector2 GetPlacementToolTile()
        {
            Vector2 toolTile = Game1.GetPlacementGrabTile();
            if (Game1.player.FacingDirection == 0)
            {
                // up
                toolTile.Y -= 1;
            }
            else if (Game1.player.FacingDirection == 1)
            {
                // right
                toolTile.X += 1;
            }
            else if (Game1.player.FacingDirection == 2)
            {
                // down
                toolTile.Y += 1;
            }
            else if (Game1.player.FacingDirection == 3)
            {
                // left
                toolTile.X -= 1;
            }
            return toolTile;
        }

        private void TryRefillWateringCan()
        {
            Vector2 tile = Game1.GetPlacementGrabTile();
            Game1.player.CurrentTool.DoFunction(
                Game1.currentLocation,(int)tile.X * 64, (int)tile.Y * 64, _powerSelected, Game1.player);

            Monitor.Log($"Refilled watering can", LogLevel.Debug);
        }
    }
}
