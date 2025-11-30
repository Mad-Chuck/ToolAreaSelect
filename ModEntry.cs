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
        private const int CHARGING_TICKS_NUMBER = 38;
        private int _powerSelected = 0;
        private Vector2? _pendingTile = null;
        private bool _charging = false;
        private int _chargeDelay = 0;
        private int? _facingDirection = null;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            _powerSelected = 0;
            _pendingTile = null;
            _charging = false;
            _chargeDelay = 0;
            _facingDirection = null;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!_charging)
                return;

            _chargeDelay--;
            if (_chargeDelay < 0)
                _chargeDelay = 0;

            if (_chargeDelay <= 0)
            {
                if (Game1.player.toolPower.Value < _powerSelected)
                {
                    Game1.player.toolPowerIncrease();
                    _chargeDelay = CHARGING_TICKS_NUMBER;
                }
                else
                {
                    Game1.player.EndUsingTool();
                    _charging = false;
                }
            }
        }

        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;
            if (Game1.player.UsingTool)
                return;
            if (IsRiddingHorse())
                return;
            if (Game1.player.CurrentTool is not (WateringCan or Hoe))
                return;

            if (IsWalking() || _charging)
            {
                Helper.Input.SuppressScrollWheel();
                return;
            }

            bool ctrl = Helper.Input.IsDown(SButton.LeftControl) || Helper.Input.IsDown(SButton.RightControl);
            if (!ctrl)
                return;

            Helper.Input.SuppressScrollWheel();

            var canOrHoe = Game1.player.CurrentTool;
            if (e.Delta > 0)
                _powerSelected++;
            else if (e.Delta < 0)
                _powerSelected--;
            int maxPower = canOrHoe.UpgradeLevel;
            bool hasReaching = canOrHoe.enchantments?.Any(enchant => enchant is ReachingToolEnchantment) ?? false;
            if (canOrHoe.UpgradeLevel == 4 && hasReaching)
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
            if (IsRiddingHorse())
                return;

            int facingDirectionBackup = Game1.player.FacingDirection;
            if (_facingDirection is not null)
                Game1.player.FacingDirection = (int)_facingDirection;

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

            Game1.player.FacingDirection = facingDirectionBackup;

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
            if (!Game1.currentLocation.CanRefillWateringCanOnTile((int)mouseTile.X, (int)mouseTile.Y))
            {
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
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;
            if (Game1.activeClickableMenu is not null)
                return;
            if (IsRiddingHorse())
                return;
            if (Game1.player.CurrentTool is not (WateringCan or Hoe))
                return;

            if (IsWalking() || _charging)
            {
                Helper.Input.Suppress(e.Button);
                return;
            }

            if (e.Button != SButton.MouseLeft)
                return;
            if (IsCursorOnUI(e))
                return;
            if (!Game1.player.CanMove)
                return;

            Helper.Input.Suppress(e.Button);

            if (Game1.player.UsingTool)
                return;

            var tile = Game1.GetPlacementGrabTile();

            if (IsWarpTile(tile))
            {
                Game1.playSound("cancel");
                Monitor.Log("Cannot move: path ends at warp tile", LogLevel.Debug);
                return;
            }

            if (Game1.player.CurrentTool is WateringCan && Game1.currentLocation.CanRefillWateringCanOnTile((int)tile.X, (int)tile.Y))
            {
                RefillWateringCan();
                return;
            }

            _pendingTile = tile;
            var controller = new PathFindController(
                Game1.player,
                Game1.currentLocation,
                new Point((int)tile.X, (int)tile.Y),
                Game1.player.FacingDirection,
                new PathFindController.endBehavior(OnReachedTile)
            );

            if (ContainsWarpTile(controller))
            {
                Game1.playSound("cancel");
                Monitor.Log("Cannot move: path crosses warp tile", LogLevel.Debug);
                return;
            }

            Game1.player.controller = controller;

            if (!IsWalking())
                _facingDirection = null;
            else
                _facingDirection = Game1.player.FacingDirection;

            Monitor.Log($"Moving to {tile}", LogLevel.Debug);
        }

        private void OnReachedTile(Character c, GameLocation location)
        {
            Monitor.Log($"Arrived at tile {_pendingTile}", LogLevel.Debug);

            if (Game1.player.CurrentTool is not (WateringCan or Hoe) || _pendingTile is null)
                return;

            var pendingTile = _pendingTile;
            _pendingTile = null;
            Game1.player.controller = null;

            // watering logic
            Game1.player.BeginUsingTool();

            if (Game1.player.CurrentTool is WateringCan can && can.WaterLeft <= 0)
                return;

            // start charging
            _charging = true;
            _chargeDelay = CHARGING_TICKS_NUMBER;
            Game1.player.toolPower.Value = 0;
            Game1.player.jitterStrength = 0f;
            _facingDirection = null;

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

        private bool ContainsWarpTile(PathFindController controller)
        {
            if (controller.pathToEndPoint is not null)
            {
                // Maybe it's possible to reduce complexity to O(n*log(n))?
                foreach (var node in controller.pathToEndPoint)
                    if (IsWarpTile(new Vector2(node.X, node.Y)))
                        return true;
            }
            return false;
        }
        
        private bool IsWarpTile(Vector2 tile)
        {
            return Game1.currentLocation.warps.Any(warpTile => warpTile.X == (int)tile.X && warpTile.Y == (int)tile.Y);
        }

        private bool IsWalking()
        {
            return Game1.player.controller is not null && Game1.player.controller.pathToEndPoint is not null;
        }
        private bool IsRiddingHorse()
        {
            return Game1.player.mount != null;
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

        private void RefillWateringCan()
        {
            Vector2 tile = Game1.GetPlacementGrabTile();
            Game1.player.CurrentTool.DoFunction(
                Game1.currentLocation,(int)tile.X * 64, (int)tile.Y * 64, _powerSelected, Game1.player);

            Monitor.Log($"Refilled watering can", LogLevel.Debug);
        }
    }
}
