using System;
using SDUtils;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using SDGraphics;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Audio;
using Ship_Game.Fleets;
using Ship_Game.GameScreens.ShipDesign;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;
using Ship_Game.UI;

namespace Ship_Game
{
    public sealed class RefitToWindow : GameScreen
    {
        public override bool HelpKeyOpensCodex => true;

        readonly ShipListScreen Screen;
        readonly Ship ShipToRefit;
        Empire Player => ShipToRefit.Universe.Player;
        SubmenuScrollList<RefitShipListItem> sub_ships;
        ScrollList<RefitShipListItem> RefitShipList;
        UIButton RefitOne;
        UIButton RefitAll;
        UIButton RefitInFleet;
        UICheckBox RushRefit;
        IShipDesign RefitTo;
        DanButton ConfirmRefit;
        ShipInfoOverlayComponent ShipInfoOverlay;
        bool Rush;
        Power ProposedPower;

        public RefitToWindow(ShipListScreen screen, ShipListScreenItem item) : base(screen, toPause: null)
        {
            Screen = screen;
            ShipToRefit = item.Ship;
            IsPopup = true;
            TransitionOnTime = 0.25f;
            TransitionOffTime = 0.25f;
            Rush = false;
        }

        public RefitToWindow(UniverseScreen parent, Ship ship) : base(parent, toPause: parent)
        {
            ShipToRefit = ship;
            IsPopup = true;
            TransitionOnTime = 0.25f;
            TransitionOffTime = 0.25f;
        }

        class RefitShipListItem : ScrollListItem<RefitShipListItem>
        {
            readonly RefitToWindow Screen;
            public readonly IShipDesign Design;

            public RefitShipListItem(RefitToWindow screen, IShipDesign design)
            {
                Screen = screen;
                Design = design;
            }
            public override void Draw(SpriteBatch batch, DrawTimes elapsed)
            {
                batch.Draw(Design.Icon, new Rectangle((int)X, (int)Y, 29, 30), Color.White);

                var tCursor = new Vector2(X + 40f, Y + 3f);
                batch.DrawString(Fonts.Arial12Bold, Design.Name, tCursor, Color.White);

                if (Screen.sub_ships.SelectedIndex == 0)
                {
                    tCursor.Y += Fonts.Arial12Bold.LineSpacing;
                    batch.DrawString(Fonts.Arial12Bold, Design.GetRole(), tCursor, Color.Orange);
                }

                var moneyRect = new Rectangle((int)X + 285, (int)Y, 21, 20);
                var moneyText = new Vector2((moneyRect.X + 25), (moneyRect.Y - 2));
                batch.Draw(ResourceManager.Texture("NewUI/icon_production"), moneyRect, Color.White);
                int refitCost = Screen.ShipToRefit.RefitCost(Design);
                batch.DrawString(Fonts.Arial12Bold, refitCost.ToString(), moneyText, Color.White);
            }
        }

        public override void LoadContent()
        {
            RectF shipDesignsRect = new(ScreenWidth / 2 - 200, 50, 400, Math.Min(360, ScreenHeight - 310));
            sub_ships = Add(new SubmenuScrollList<RefitShipListItem>(shipDesignsRect, "Refit to..."));
            sub_ships.SetBackground(Colors.TransparentBlackFill);
            
            RefitShipList = sub_ships.List;
            RefitShipList.EnableItemHighlight = true;
            RefitShipList.OnClick = OnRefitShipItemClicked;

            if (!ShipToRefit.IsSubspaceProjector)
            {
                foreach (IShipDesign design in ShipToRefit.Loyalty.ShipsWeCanBuildSnapshot)
                {
                    if (StarbaseRules.CanRefit(ShipToRefit, design))
                    {
                        RefitShipList.AddItem(new RefitShipListItem(this, design));
                    }
                }
            }

            ConfirmRefit = new DanButton(new Vector2(shipDesignsRect.X, (shipDesignsRect.Bottom + 8)), "Do Refit");

            RefitOne = ButtonMedium(shipDesignsRect.X + 10, shipDesignsRect.Bottom + 8, text:GameText.RefitOne, click: OnRefitOneClicked);
            RefitOne.Tooltip = GameText.RefitOnlyThisShipTo;
            RefitAll = ButtonMedium(shipDesignsRect.X + 270, shipDesignsRect.Bottom + 8, text:GameText.RefitAll, click: OnRefitAllClicked);
            RefitAll.Tooltip = GameText.RefitAllShipsOfThis;
            RefitInFleet = ButtonMedium(shipDesignsRect.X + 140, shipDesignsRect.Bottom + 8, text: GameText.RefitInFleet, click: OnRefitFleetClicked);
            RefitInFleet.Tooltip = GameText.RefitInFleetTip;
            RushRefit = Add(new UICheckBox(() => Rush, Fonts.Arial12Bold,
                title: GameText.RushRefit, tooltip: GameText.RushRefitTip));
            RushRefit.TextColor = Color.Gray;
            RushRefit.CheckedTextColor = Color.Red;
            RushRefit.Pos = new Vector2(shipDesignsRect.X, shipDesignsRect.Bottom + 44);
            RushRefit.Visible = false;

            ShipInfoOverlay = Add(new ShipInfoOverlayComponent(this, ShipToRefit.Universe));
            RefitShipList.OnHovered = (item) =>
            {
                ShipInfoOverlay.ShowToLeftOf(item?.Pos ?? Vector2.Zero, item?.Design);
            };

            base.LoadContent();
            RefitOne.Visible = RefitAll.Visible = RefitInFleet.Visible = RushRefit.Visible = false;
        }

        void OnRefitShipItemClicked(RefitShipListItem item)
        {
            RefitTo = item.Design;
            var modules = new SDUtils.Array<ShipModule>();
            foreach (var slot in RefitTo.GetOrLoadDesignSlots())
                if (ResourceManager.GetModuleTemplate(slot.ModuleUID, out ShipModule module)) modules.Add(module);
            ProposedPower = Power.Calculate(modules, Player, designModule: true);
            RushRefit.Visible = RefitAll.Visible = RefitOne.Visible = RefitTo != null;
            RefitInFleet.Visible = RefitAll.Visible && ShipToRefit.Fleet != null;
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
            batch.SafeBegin();
            base.Draw(batch, elapsed);
            if (RefitTo != null)
            {
                var panel = new RectF(ConfirmRefit.r.X, ConfirmRefit.r.Y + 72, 400, 154);
                batch.FillRectangle(panel, new Color(4, 15, 25).Alpha(.95f));
                var capability = StationCapabilities.ForDesign(RefitTo);
                float y = panel.Y + 8;
                batch.DrawString(Fonts.Arial12Bold, "REFIT COMPARISON", panel.X + 10, y, Color.White);
                void Row(string label, float before, float after, string suffix = "")
                {
                    y += 20;
                    batch.DrawString(Fonts.Arial10, $"{label}: {before.String(0)} -> {after.String(0)}{suffix}",
                        panel.X + 10, y, Color.LightGray);
                }
                Row("Borders", ShipToRefit.BorderClaimRadius, capability.Borders);
                Row("Sensors", ShipToRefit.SensorRange, capability.Sensors * Player.data.SensorModifier);
                Row("Inhibition", ShipToRefit.InhibitionRadius, capability.Inhibition);
                Row("Power surplus", ShipToRefit.NetPower.PowerFlowMax - ShipToRefit.NetPower.NetSubLightPowerDraw,
                    ProposedPower.PowerFlowMax - ProposedPower.NetSubLightPowerDraw);
                y += 20;
                batch.DrawString(Fonts.Arial10, $"Upkeep: {ShipToRefit.ShipData.GetMaintenanceCost(Player).String(2)} -> {RefitTo.GetMaintenanceCost(Player).String(2)} BC/T",
                    panel.X + 10, y, Color.LightGray);
                if (ShipToRefit.IsStarbase && capability.Borders <= 0)
                    batch.DrawString(Fonts.Arial10, "WARNING: this refit removes territorial control", panel.X + 10, y + 20, Color.Orange);
            }
            batch.SafeEnd();
        }

        public override void ExitScreen()
        {
            if (Screen != null)
                Screen.Universe.RunOnSimThread(() => Screen.StatusDirty = true);
            base.ExitScreen();
        }

        void OnRefitOneClicked(UIButton b)
        {
            if (!StarbaseRules.CanRefit(ShipToRefit, RefitTo)) { GameAudio.NegativeClick(); return; }
            Player.AI.AddGoalAndEvaluate(GetRefitGoal(ShipToRefit));
            GameAudio.EchoAffirmative();
            ExitScreen();
        }

        void OnRefitAllClicked(UIButton b)
        {
            RefitAllShips();
            foreach (Fleet fleet in Player.AllFleets)
                fleet.RefitNodeName(ShipToRefit.Name, RefitTo.Name);

            GameAudio.EchoAffirmative();
            ExitScreen();
        }

        void RefitAllShips(Fleet specificFleet = null)
        {
            var ships = Player.OwnedShips;
            foreach (Ship ship in ships)
            {
                if (ship.Name == ShipToRefit.Name && (specificFleet == null || ship.Fleet == specificFleet)
                    && StarbaseRules.CanRefit(ship, RefitTo))
                    Player.AI.AddGoalAndEvaluate(GetRefitGoal(ship));
            }

            foreach (Planet planet in Player.GetPlanets())
                planet.Construction.RefitShipsBeingBuilt(ShipToRefit, RefitTo);
        }

        void OnRefitFleetClicked(UIButton b)
        {
            ShipToRefit.Fleet?.RefitNodeName(ShipToRefit.Name, RefitTo.Name);
            RefitAllShips(ShipToRefit.Fleet);
            GameAudio.EchoAffirmative();
            ExitScreen();
        }

        Goal GetRefitGoal(Ship ship)
        {
            Goal refitShip;
            if (ShipToRefit.IsPlatformOrStation)
                refitShip = new RefitOrbital(ship, RefitTo, Player, Rush);
            else
                refitShip = new RefitShip(ship, RefitTo, Player, Rush);

            return refitShip;
        }
    }
}
