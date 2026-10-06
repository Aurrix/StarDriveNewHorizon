using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game;
using Ship_Game.StoryAndEvents;
using Ship_Game.GameScreens.LoadGame;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using UnitTests.Serialization;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Ships;

[TestClass]
public class PirateUnderworldTests : StarDriveTest
{
    public TestContext TestContext { get; set; }
    PirateUnderworld Market => UState.Underworld;
    readonly Planet Colony;

    public PirateUnderworldTests()
    {
        LoadStarterShips("Corsair", "Corsair-Fighter", "Corsair Asteroid Base", "Corsair-Mk3", "Corsair-Fighter-Mk3");
        CreateUniverseAndPlayerEmpire();
        CreateAMinorFaction("Corsairs");
        Universe.NotificationManager = new NotificationManager(Universe.ScreenManager, Universe);
        UState.Objects.EnableParallelUpdate = false;
        Faction.Pirates.Init();
        Faction.Pirates.SetLevel(1);
        Faction.AI.ClearGoals();
        Colony = AddHomeWorldToEmpire(new Vector2(1000), Player);
        AddHomeWorldToEmpire(new Vector2(250_000), Enemy);
        SpawnShip("Corsair Asteroid Base", Faction, new Vector2(500_000));
        Player.AddMoney(100_000);
        Enemy.AddMoney(100_000);
        Market.Settings.InvestmentCost = float.MaxValue;
        Market.Settings.AuctionTurns = 10_000;
        Market.Settings.DeliveryTurns = 1;
        Market.Settings.LeaseTurns = 10;
        UState.Events.Disabled = true;
    }

    void Tick(int count = 1)
    {
        for (int i = 0; i < count; ++i)
        {
            UState.Objects.Update(TestSimStep); // publish newly spawned ships to empire lists
            UState.StarDate += .1f;
            Market.Update(UState);
            UState.Objects.Update(TestSimStep);
        }
    }

    PirateLease Hire()
    {
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Tick();
        var lease = Market.Leases.Last;
        Assert.AreEqual(PirateLeaseStatus.Active, lease.Status);
        Assert.AreEqual(5, lease.Ships.Count);
        return lease;
    }

    [TestMethod]
    public void ContributionsPoolAndFundEachFactionOnlyOnce()
    {
        CreateThirdMajorEmpire();
        ThirdMajor.AddMoney(5000);
        float before = Player.Money;
        Assert.IsTrue(Market.Contribute(Player, Enemy, 1000));
        Assert.IsTrue(Market.Contribute(ThirdMajor, Enemy, 500));
        Assert.AreEqual(1500d, Market.BountyFor(Enemy));
        Assert.AreEqual(before - 1000, Player.Money);
        Assert.AreEqual(1500d, Faction.Pirates.Market.Investment);
        Tick(2);
        Assert.AreEqual(1500d, Faction.Pirates.Market.Investment);
        Assert.AreEqual(1500d, Market.BountyFor(Enemy));
    }

    [TestMethod]
    public void ContributionsSplitAcrossLivingFactions()
    {
        Empire corsairs = Faction;
        CreateAMinorFaction("Draugar");
        Assert.IsTrue(Market.Contribute(Player, Enemy, 1000));
        Assert.AreEqual(500d, corsairs.Pirates.Market.Investment);
        Assert.AreEqual(500d, Faction.Pirates.Market.Investment);
    }

    [TestMethod]
    public void InvalidPaymentsNeverChangeBalances()
    {
        float balance = Player.Money;
        foreach (double amount in new[] { double.NaN, double.PositiveInfinity, -1, 0, double.MaxValue })
            Assert.IsFalse(Market.Contribute(Player, Enemy, amount));
        Assert.IsFalse(Market.Contribute(Player, Player, 100));
        Assert.IsFalse(Market.Contribute(Player, Enemy, balance + 100));
        Assert.AreEqual(balance, Player.Money);
        Assert.AreEqual(0, Market.Contributions.Count);
    }

    [TestMethod]
    public void UpdatesAreIdempotentAtTheSameStarDate()
    {
        Market.Update(UState);
        int turn = Market.Turn;
        Market.Update(UState);
        Assert.AreEqual(turn, Market.Turn);
    }

    [TestMethod]
    public void RaidLaunchAndDamageSpendBountyWithoutSecondRevenue()
    {
        Ship victim = SpawnShip("Corsair", Enemy, new Vector2(250_000));
        Assert.IsTrue(Market.Contribute(Player, Enemy, 1000));
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.AreEqual(1, Market.Raids.Count);
        Assert.IsTrue(Market.Raids[0].Launched);
        Assert.AreEqual(800d, Market.BountyFor(Enemy));
        Assert.AreEqual(1000d, Faction.Pirates.Market.Investment);
        Market.RecordAssetLoss(victim, Enemy, Faction, captured: true);
        double remaining = Math.Max(0, 800 - victim.ShipData.BaseCost);
        Assert.AreEqual(remaining, Market.BountyFor(Enemy));
        Market.RecordAssetLoss(victim, Enemy, Faction);
        Assert.AreEqual(remaining, Market.BountyFor(Enemy));
        Assert.AreEqual(1000d, Faction.Pirates.Market.Investment);
    }

    [TestMethod]
    public void ActualShipDeathCreditsTheProjectilesOriginalFactionOnce()
    {
        Ship victim = SpawnShip("Corsair", Enemy, new Vector2(250_000));
        Ship attacker = SpawnShip("Corsair", Faction, new Vector2(251_000));
        Market.Contribute(Player, Enemy, 1000);
        var weapon = attacker.Weapons.First(w => !w.IsBeam);
        var projectile = Projectile.Create(weapon, attacker, attacker.Position,
            (victim.Position - attacker.Position).Normalized(), victim, playSound: false);
        attacker.LoyaltyChangeFromBoarding(Player, addNotification: false);
        Tick();
        Assert.AreSame(Player, attacker.Loyalty);
        victim.Die(projectile, cleanupOnly: true);
        double remaining = Math.Max(0, 1000 - victim.ShipData.BaseCost);
        Assert.AreEqual(remaining, Market.BountyFor(Enemy));
        victim.Die(projectile, cleanupOnly: true);
        Assert.AreEqual(remaining, Market.BountyFor(Enemy));
    }

    [TestMethod]
    public void HighestBountyWinsAndLosingBountiesPersist()
    {
        CreateThirdMajorEmpire();
        SpawnShip("Corsair", Enemy, new Vector2(250_000));
        SpawnShip("Corsair", ThirdMajor, new Vector2(-250_000));
        Market.Contribute(Player, Enemy, 1000);
        Market.Contribute(Player, ThirdMajor, 2000);
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.AreSame(ThirdMajor, Market.Raids[0].Target);
        Assert.AreEqual(1000d, Market.BountyFor(Enemy));
    }

    [TestMethod]
    public void FailedLaunchDoesNotSpendBounty()
    {
        SpawnShip("Corsair", Enemy, new Vector2(250_000));
        Market.Contribute(Player, Enemy, 1000);
        Faction.data.PirateFrigateBasic = "missing-test-design";
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.AreEqual(1000d, Market.BountyFor(Enemy));
        Assert.IsTrue(Market.Raids[0].Complete);
    }

    [TestMethod]
    public void ProtectionEarnsGraduallyAndBetrayalRefundsUnspentRevenue()
    {
        Faction.data.PiratePaymentPeriodTurns = 100;
        Market.Settings.WarningTurns = 2;
        Assert.IsTrue(Market.PurchaseProtection(Faction, Player));
        Assert.AreEqual(0d, Faction.Pirates.Market.Investment);
        var client = Market.Client(Faction, Player);
        float price = client.ProtectionPrice;
        SpawnShip("Corsair", Player, new Vector2(10_000));
        Assert.IsTrue(Market.Contribute(Enemy, Player, 10_000));
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.IsTrue(Market.Protected(Faction, Player));
        Assert.IsTrue(Market.Locked(Faction, Player));
        Assert.IsFalse(Market.PurchaseProtection(Faction, Player));
        float balance = Player.Money;
        Tick(2);
        Assert.IsFalse(Market.Protected(Faction, Player));
        Assert.IsTrue(Market.Raids[0].Launched);
        Assert.AreEqual(balance + price * .97f, Player.Money, .1f);
        Assert.AreEqual(10_000 + price * .03, Faction.Pirates.Market.Investment, .1);
    }

    [TestMethod]
    public void SmallBountyCannotBreakProtection()
    {
        Market.PurchaseProtection(Faction, Player);
        SpawnShip("Corsair", Player, new Vector2(10_000));
        Market.Contribute(Enemy, Player, 1);
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.AreEqual(0, Market.Raids.Count);
        Assert.IsTrue(Market.Protected(Faction, Player));
    }

    [TestMethod]
    public void RentalCannotBeScrappedGiftedOrRefittedAndHasUpkeep()
    {
        Ship ship = Hire().Ships[0];
        Assert.IsFalse(ship.CanBeScrapped);
        Assert.IsFalse(ship.CanBeRefitted);
        Assert.IsTrue(ShipMaintenance.GetMaintenanceCost(ship, Player, 0) > 0);
        ship.LoyaltyChangeByGift(Enemy);
        ship.AI.OrderScuttleShip();
        Tick();
        Assert.AreSame(Player, ship.Loyalty);
        Assert.IsTrue(ship.ScuttleTimer < 0);
    }

    [TestMethod]
    public void ExpiryFindsShipsAfterFleetSplitAndReturnsWithoutSalvage()
    {
        var lease = Hire();
        Ship ship = lease.Ships[0];
        ship.RemoveFromPoolAndFleet(clearOrders: true);
        Tick(11);
        Assert.AreSame(Faction, ship.Loyalty);
        Assert.AreEqual(0, ship.PirateLeaseId);
        Assert.AreEqual(PirateLeaseStatus.Returned, lease.Status);
        Assert.IsTrue(Faction.Pirates.SpawnedShips.Contains(ship.Id));
    }

    [TestMethod]
    public void CapturedRentalNeverReturnsToOriginalFaction()
    {
        var lease = Hire();
        Ship ship = lease.Ships[0];
        ship.LoyaltyChangeFromBoarding(Enemy, addNotification: false);
        Tick();
        Assert.AreSame(Enemy, ship.Loyalty);
        Assert.AreEqual(0, ship.PirateLeaseId);
        Tick(11);
        Assert.AreSame(Enemy, ship.Loyalty);
    }

    [TestMethod]
    public void CaptureQueuedBeforeExpirationTakesPrecedenceOverRecall()
    {
        var lease = Hire();
        Ship ship = lease.Ships[0];
        lease.EndTurn = Market.Turn + 1;
        ship.LoyaltyChangeFromBoarding(Enemy, addNotification: false);
        UState.StarDate += .1f;
        Market.Update(UState);
        UState.Objects.Update(TestSimStep);
        Assert.AreSame(Enemy, ship.Loyalty);
        Assert.AreEqual(0, ship.PirateLeaseId);
    }

    [TestMethod]
    public void ExpiredProtectionDoesNotPenalizeAClientForLaterHostilities()
    {
        Faction.data.PiratePaymentPeriodTurns = 1;
        Market.PurchaseProtection(Faction, Player);
        Tick();
        Assert.IsFalse(Market.Protected(Faction, Player));
        var client = Market.Client(Faction, Player);
        Faction.AI.DeclareWarFromEvent(Player, WarType.SkirmishWar);
        Tick();
        Assert.AreEqual(0f, client.ReputationPenalty);
    }

    [TestMethod]
    public void BetrayalHonorsExistingLeasesButBlocksNewOnes()
    {
        var lease = Hire();
        Faction.AI.DeclareWarFromEvent(Player, WarType.SkirmishWar);
        Tick();
        Assert.AreSame(Player, lease.Ships[0].Loyalty);
        Assert.IsFalse(Market.PurchaseRental(Faction, Player, Colony, 0));
        Assert.IsFalse(Market.Renew(Player, lease.Id));
    }

    [TestMethod]
    public void MissingDeliveryDesignRefundsWithoutFundingGrowth()
    {
        float balance = Player.Money;
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Market.Leases[0].Designs[0] = "missing-test-design";
        Tick();
        Assert.AreEqual(balance, Player.Money);
        Assert.AreEqual(0d, Faction.Pirates.Market.Investment);
        Assert.AreEqual(PirateLeaseStatus.Refunded, Market.Leases[0].Status);
    }

    [TestMethod]
    public void RenewalChargesOnceAndExtendsWithoutReplacingLosses()
    {
        var lease = Hire();
        lease.Ships[0].QueueTotalRemoval();
        int end = lease.EndTurn;
        float balance = Player.Money;
        Assert.IsTrue(Market.Renew(Player, lease.Id));
        Assert.AreEqual(end + 10, lease.EndTurn);
        Assert.AreEqual(balance - lease.Fee, Player.Money);
        Assert.IsFalse(Market.Renew(Player, lease.Id));
        Assert.AreEqual(5, lease.Ships.Count);
    }

    [TestMethod]
    public void LeaseLimitAndReputationGatesAreEnforced()
    {
        Assert.IsFalse(Market.PurchaseRental(Faction, Player, Colony, 1));
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Assert.IsFalse(Market.PurchaseRental(Faction, Player, Colony, 0));
    }

    [TestMethod]
    public void BaseLossErasesUnspentInvestment()
    {
        Faction.Pirates.SetLevel(2);
        Faction.Pirates.Market.Investment = 500;
        Faction.Pirates.LevelDown();
        Assert.AreEqual(1, Faction.Pirates.Level);
        Assert.AreEqual(0d, Faction.Pirates.Market.Investment);
    }

    [TestMethod]
    public void SettingsAndEmptyMarketRoundTrip()
    {
        var market = new PirateUnderworld { Turn = 32 };
        market.Settings.LeaseTurns = 180;
        var loaded = BinarySerializerTests.SerDes(market);
        Assert.AreEqual(32, loaded.Turn);
        Assert.AreEqual(180, loaded.Settings.LeaseTurns);
        Assert.AreEqual(0, loaded.Leases.Count);
    }

    [TestMethod]
    public void LegacyProtectionTimerMigratesWithoutResettingPirateStrength()
    {
        Faction.Pirates.SetLevel(7);
        Faction.Pirates.ThreatLevels[Player.Id] = 1;
        Faction.Pirates.PaymentTimers[Player.Id] = 42;
        Assert.IsTrue(Market.Protected(Faction, Player));
        Assert.AreEqual(42, Market.Client(Faction, Player).ProtectionEnd - Market.Turn);
        Assert.AreEqual(7, Faction.Pirates.Level);
        Assert.AreEqual(0d, Faction.Pirates.Market.Investment);
    }

    [TestMethod]
    public void EncounterUsesSamePriceAndIgnoresQueuedDuplicateResponse()
    {
        Assert.IsTrue(Encounter.GetEncounterForAI(Faction, 0, out Encounter encounter));
        var instance = new EncounterInstance(encounter, Player, Faction);
        var response = instance.CurrentDialog.ResponseOptions[0];
        float before = Player.Money, price = Market.ProtectionPrice(Faction, Player);
        instance.OnResponseItemClicked(response);
        instance.OnResponseItemClicked(response);
        Universe.InvokePendingSimThreadActions();
        Assert.AreEqual(before - price, Player.Money);
        Assert.IsTrue(Market.Protected(Faction, Player));
        Assert.IsFalse(Faction.IsAtWarWith(Player));
    }

    [TestMethod]
    public void DestroyedIssuerHonorsDeliveredLeaseButRefundsPendingDelivery()
    {
        var active = Hire();
        float before = Player.Money;
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Faction.SetAsDefeated();
        Tick();
        Assert.AreEqual(before, Player.Money);
        Assert.IsTrue(active.Ships[0].Active);
        Assert.AreEqual(PirateLeaseStatus.Refunded, Market.Leases[1].Status);
        Tick(11);
        Assert.IsFalse(active.Ships[0].Active);
    }

    [TestMethod]
    public void LostDeliveryColonyFallsBackToAnotherOwnedColony()
    {
        Planet alternative = AddHomeWorldToEmpire(new Vector2(-250_000), Player);
        Assert.IsTrue(Market.PurchaseRental(Faction, Player, Colony, 0));
        Colony.SetOwner(Enemy);
        Tick();
        Assert.AreSame(alternative, Market.Leases[0].Destination);
        Assert.AreEqual(PirateLeaseStatus.Active, Market.Leases[0].Status);
    }

    [TestMethod]
    public void AiRentalsRemainAvailableForMilitaryTasks()
    {
        Assert.IsTrue(Market.PurchaseRental(Faction, Enemy, Enemy.GetPlanets()[0], 0));
        Tick();
        var lease = Market.Leases[0];
        Assert.AreEqual(PirateLeaseStatus.Active, lease.Status);
        Assert.IsTrue(lease.Ships.All(s => s.Fleet == null));
        Assert.IsTrue(Enemy.AllFleetReadyShips().Contains(lease.Ships[0]));
    }

    [TestMethod]
    public void InvestmentBuildsABaseAndAdvancesExactlyOneLevel()
    {
        Market.Settings.InvestmentCost = 1000;
        Faction.Pirates.Market.Investment = 1000;
        for (int i = 0; i < 20 && Faction.Pirates.Level == 1; ++i) Tick();
        Assert.AreEqual(2, Faction.Pirates.Level);
        Assert.AreEqual(0d, Faction.Pirates.Market.Investment);
        Assert.IsTrue(Faction.Pirates.GetBases(out var bases));
        Assert.IsTrue(bases.Count >= 2);
    }

    [TestMethod]
    public void AiSponsorsWarEnemiesWithDiscretionaryFunds()
    {
        Market.Settings.AuctionTurns = 1;
        Tick();
        Assert.IsTrue(Market.BountyFor(Player) > 0);
        Assert.IsTrue(Market.Contributions.All(c => c.Sponsor == Enemy));
        Assert.IsTrue(Enemy.Money >= 1000);
    }

    [TestMethod]
    public void DangerTracksFactionGrowthAndBaseLossIndependentlyOfStanding()
    {
        Faction.Pirates.SetLevel(17);
        var before = new PirateFactionPresentation(Faction, Player, Market);
        Assert.AreEqual(5, before.Danger);
        Market.Client(Faction, Player).Business = 100_000;
        Assert.AreEqual(5, new PirateFactionPresentation(Faction, Player, Market).Danger);
        Faction.Pirates.LevelDown();
        Assert.AreEqual(4, new PirateFactionPresentation(Faction, Player, Market).Danger);
        Assert.AreEqual(17, before.Level, "UI snapshots must not change beneath the renderer");
        foreach (int level in new[] { 1, 4, 5, 8, 9, 12, 13, 16, 17, 20 })
            Assert.AreEqual((level - 1) / 4 + 1, PirateFactionPresentation.DangerTier(level));
        Assert.AreEqual(0, PirateFactionPresentation.DangerTier(0));
        Assert.AreEqual(5, PirateFactionPresentation.DangerTier(25));
    }

    [TestMethod]
    [DataRow(1280, 720)]
    [DataRow(1920, 1080)]
    public void UnderworldPagesRenderAtSupportedResolutions(int width, int height)
    {
        ResourceManager.Blank ??= ResourceManager.Texture("blank");
        if (ResourceManager.NumFlags == 0)
            typeof(ResourceManager).GetMethod("LoadFlagTextures", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Universe.ScreenManager.UpdateGraphicsDevice();
        Hire();
        Market.Contribute(Player, Enemy, 1000);
        Market.Settings = new PirateMarketSettings();
        Faction.Pirates.Market.NextAuction = Market.Turn + 50;
        Empire corsairs = Faction;
        corsairs.Pirates.SetLevel(13);
        CreateAMinorFaction("Draugar");
        Faction.Pirates.SetLevel(18);
        Empire draugar = Faction;
        PropertyInfo screenWidth = typeof(GameBase).GetProperty("ScreenWidth", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo screenHeight = typeof(GameBase).GetProperty("ScreenHeight", BindingFlags.Public | BindingFlags.Static);
        int oldWidth = GameBase.ScreenWidth, oldHeight = GameBase.ScreenHeight;
        var device = Game.GraphicsDevice;
        var previous = device.GetRenderTargets();
        try
        {
            screenWidth.SetValue(null, width);
            screenHeight.SetValue(null, height);
            using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            using var screen = new PirateUnderworldScreen(Universe);
            screen.LoadContent();
            foreach (string tab in new[] { "PirateBounties", "PirateRentals", "PirateProtection", "PirateContracts" })
            {
                Universe.InvokePendingSimThreadActions();
                screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
                Assert.IsTrue(screen.Find<UIButton>(tab, out var button));
                button.OnClick(button);
                Universe.InvokePendingSimThreadActions();
                screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
                screen.Update(.3f);
                screen.PerformLayout();
                device.SetRenderTarget(target);
                device.Clear(Microsoft.Xna.Framework.Color.Black);
                using (var batch = new SpriteBatch(device)) screen.Draw(batch, new DrawTimes());
                device.SetRenderTargets(previous);
                string directory = Path.GetFullPath(Path.Combine(StarDriveTestContext.StarDriveAbsolutePath, "../output/pirates-preview"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"{tab}-{width}.png");
                using (var file = File.Create(path)) target.SaveAsPng(file, width, height);
                TestContext.AddResultFile(path);
                var pixels = new Microsoft.Xna.Framework.Color[width * height];
                target.GetData(pixels);
                Assert.IsTrue(pixels.Any(p => p.R > 150 && p.G > 100), "Text and controls must render");
                if (tab == "PirateBounties")
                {
                    Assert.IsTrue(screen.Find<ScrollListBase>("PirateMarketRows", out var marketRows));
                    var provider = new UnitTests.UI.MockInputProvider
                    {
                        MousePos = marketRows.ItemsHousing.Pos + new Vector2(100, 30)
                    };
                    var input = new InputState { Provider = provider };
                    input.Update(new UpdateTimes(.016f, 1));
                    provider.LeftMouse = SDGraphics.Input.ButtonState.Pressed;
                    input.Update(new UpdateTimes(.016f, 1));
                    screen.HandleInput(input);
                    provider.LeftMouse = SDGraphics.Input.ButtonState.Released;
                    input.Update(new UpdateTimes(.016f, 1));
                    screen.HandleInput(input);
                    Assert.IsTrue(screen.Find<UIButton>("PirateSelectRow", out var contribute));
                    Assert.IsTrue(contribute.Enabled, "Selecting an enemy enables the bounty action");
                    float balance = Player.Money;
                    contribute.OnClick(contribute);
                    Universe.InvokePendingSimThreadActions();
                    screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
                    Universe.InvokePendingSimThreadActions();
                    screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
                    Assert.AreEqual(balance - 1000, Player.Money, "The redesigned controls must submit the displayed contribution");
                }
            }
            Assert.IsTrue(screen.Find<ScrollList<PirateFactionCard>>("PirateFactions", out var factions));
            Assert.AreEqual(2, factions.AllEntries.Count);
            var draugarCard = factions.AllEntries.First(c => c.Info.Faction == draugar);
            factions.OnItemClicked(draugarCard);
            Universe.InvokePendingSimThreadActions();
            screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            Assert.IsTrue(factions.AllEntries.First(c => c.Info.Faction == draugar).IsSelected);
            Assert.IsFalse(factions.AllEntries.First(c => c.Info.Faction == corsairs).IsSelected);
            using var diplomacy = new MainDiplomacyScreen(Universe);
            diplomacy.LoadContent();
            Universe.InvokePendingSimThreadActions();
            diplomacy.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            diplomacy.PerformLayout();
            foreach (Empire faction in new[] { corsairs, draugar })
            {
                Assert.IsTrue(diplomacy.Find<PirateContactButton>("PirateContact" + faction.Id, out var contact));
                Assert.IsTrue(contact.X >= 0 && contact.Right <= width);
                Assert.IsTrue(contact.Bottom <= diplomacy.SelectedInfoRect.Y, "Pirate cards must not cover empire details");
            }
            device.SetRenderTarget(target);
            device.Clear(Microsoft.Xna.Framework.Color.Black);
            diplomacy.Draw(Universe.ScreenManager.SpriteBatch, new DrawTimes());
            device.SetRenderTargets(previous);
            string diploPath = Path.GetFullPath(Path.Combine(StarDriveTestContext.StarDriveAbsolutePath,
                $"../output/pirates-preview/PirateDiplomacy-{width}.png"));
            using (var file = File.Create(diploPath)) target.SaveAsPng(file, width, height);
            TestContext.AddResultFile(diploPath);

            // Exercise a future roster large enough to require several diplomacy pages and scrolling.
            var expandedRoster = new System.Collections.Generic.List<Empire> { corsairs, draugar };
            for (int i = 0; i < 6; ++i)
            {
                var data = ResourceManager.MinorRaces.First(r => r.Name == "Corsairs").CreateInstance(copyTraits: true);
                data.Traits.Name = "Test pirate faction " + i;
                data.PirateArtwork = i == 0 ? "Underworld/Draugar" : "Underworld/missing-test-art";
                Empire extra = UState.CreateEmpire(data, isPlayer: false);
                extra.Pirates.SetLevel(i + 1);
                if (i == 5) continue; // Undiscovered factions must not be exposed by either screen.
                Empire.SetRelationsAsKnown(Player, extra);
                expandedRoster.Add(extra);
            }
            Assert.AreEqual("Underworld/Draugar", new PirateFactionPresentation(expandedRoster[2], Player, Market).ArtPath);
            Assert.AreSame(ResourceManager.Texture("Encounters/pirates3"), PirateFactionPresentation.LoadArt("Underworld/missing-test-art"));
            diplomacy.BecameActive();
            Universe.InvokePendingSimThreadActions();
            diplomacy.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            var seen = new System.Collections.Generic.HashSet<Empire>();
            for (int page = 0; page < 4; ++page)
            {
                diplomacy.PerformLayout();
                if (page == 1)
                {
                    device.SetRenderTarget(target);
                    device.Clear(Microsoft.Xna.Framework.Color.Black);
                    diplomacy.Draw(Universe.ScreenManager.SpriteBatch, new DrawTimes());
                    device.SetRenderTargets(previous);
                    string pagePath = Path.GetFullPath(Path.Combine(StarDriveTestContext.StarDriveAbsolutePath,
                        $"../output/pirates-preview/PirateDiplomacyExpanded-{width}.png"));
                    using (var file = File.Create(pagePath)) target.SaveAsPng(file, width, height);
                    TestContext.AddResultFile(pagePath);
                }
                foreach (Empire faction in expandedRoster)
                {
                    if (!diplomacy.Find<PirateContactButton>("PirateContact" + faction.Id, out var contact)) continue;
                    seen.Add(faction);
                    Assert.IsTrue(contact.Right <= width && contact.Bottom <= diplomacy.SelectedInfoRect.Y);
                }
                Assert.IsTrue(diplomacy.Find<UIButton>("PirateContactsNext", out var next));
                Assert.AreEqual(page < 3, next.Enabled);
                if (!next.Enabled) break;
                next.OnClick(next);
                diplomacy.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            }
            Assert.AreEqual(expandedRoster.Count, seen.Count, "Every known faction must be reachable in diplomacy");
            Assert.IsTrue(screen.Find<UIButton>("PirateBounties", out var bounties));
            bounties.OnClick(bounties);
            Universe.InvokePendingSimThreadActions();
            screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            screen.PerformLayout();
            Assert.AreEqual(expandedRoster.Count, factions.AllEntries.Count);
            var lastCard = factions.AllEntries.Last();
            factions.OnItemClicked(lastCard);
            Universe.InvokePendingSimThreadActions();
            screen.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            Assert.AreEqual(lastCard.Info.Faction, factions.AllEntries.Single(c => c.IsSelected).Info.Faction);
            foreach (Empire extra in expandedRoster.Skip(2)) extra.SetAsDefeated();
            diplomacy.BecameActive();
            Universe.InvokePendingSimThreadActions();
            diplomacy.PreUpdate(new UpdateTimes(.3f, 1), false, false);
            Assert.IsFalse(diplomacy.Find<UIButton>("PirateContactsNext", out _), "Shrinking the roster removes unnecessary paging");
            Assert.IsTrue(diplomacy.Find<PirateContactButton>("PirateContact" + corsairs.Id, out _));
        }
        finally
        {
            device.SetRenderTargets(previous);
            screenWidth.SetValue(null, oldWidth);
            screenHeight.SetValue(null, oldHeight);
        }
    }

    [TestMethod]
    public void SaveLoadPreservesBountyProtectionAndActiveLease()
    {
        var lease = Hire();
        Market.Contribute(Player, Enemy, 1000);
        Market.PurchaseProtection(Faction, Player);
        var save = Universe.Save("UnitTest.PirateUnderworld", throwOnError: true);
        var loaded = LoadGame.Load(save.SaveFile, noErrorDialogs: true, startSimThread: false);
        var market = loaded.UState.Underworld;
        Assert.AreEqual(1000d, market.BountyFor(loaded.UState.GetEmpireById(Enemy.Id)));
        Assert.AreEqual(lease.EndTurn, market.Leases[0].EndTurn);
        Assert.AreEqual(lease.Id, loaded.UState.Objects.FindShip(lease.Ships[0].Id).PirateLeaseId);
        Assert.IsTrue(market.Protected(loaded.UState.GetEmpireById(Faction.Id), loaded.UState.Player));
        int turn = market.Turn;
        market.Update(loaded.UState);
        Assert.AreEqual(turn, market.Turn, "Loading does not advance contracts");
    }

    [TestMethod]
    public void SaveDuringDeferredReturnCannotTurnALeaseIntoAPermanentGift()
    {
        var lease = Hire();
        lease.EndTurn = Market.Turn + 1;
        UState.StarDate += .1f;
        Market.Update(UState); // transfer queued; ship update deliberately has not run
        Assert.AreSame(Player, lease.Ships[0].Loyalty);
        var save = Universe.Save("UnitTest.PirateLeaseReturn", throwOnError: true);
        var loaded = LoadGame.Load(save.SaveFile, noErrorDialogs: true, startSimThread: false);
        var market = loaded.UState.Underworld;
        loaded.UState.StarDate += .1f;
        market.Update(loaded.UState);
        loaded.UState.Objects.Update(TestSimStep);
        Ship ship = loaded.UState.Objects.FindShip(lease.Ships[0].Id);
        Assert.AreEqual(Faction.Id, ship.Loyalty.Id);
        Assert.AreEqual(0, ship.PirateLeaseId);
    }

    [TestMethod]
    public void WarningAndPendingDeliverySurviveSaveLoad()
    {
        Market.PurchaseProtection(Faction, Player);
        Market.Contribute(Enemy, Player, 10_000);
        SpawnShip("Corsair", Player, new Vector2(10_000));
        Market.Settings.DeliveryTurns = 10;
        Market.PurchaseRental(Faction, Player, Colony, 0);
        Faction.Pirates.Market.NextAuction = 1;
        Tick();
        Assert.IsFalse(Market.Raids[0].Launched);
        var save = Universe.Save("UnitTest.PirateWarning", throwOnError: true);
        var loaded = LoadGame.Load(save.SaveFile, noErrorDialogs: true, startSimThread: false);
        Assert.AreEqual(Market.Raids[0].LaunchTurn, loaded.UState.Underworld.Raids[0].LaunchTurn);
        Assert.AreEqual(Market.Leases[0].DeliveryTurn, loaded.UState.Underworld.Leases[0].DeliveryTurn);
        Assert.AreEqual(PirateLeaseStatus.Delivering, loaded.UState.Underworld.Leases[0].Status);
    }

    [TestMethod]
    public void DefeatedTargetRetiresBountyWithoutRefund()
    {
        Market.Contribute(Player, Enemy, 1000);
        float before = Player.Money;
        Enemy.SetAsDefeated();
        Tick();
        Assert.AreEqual(0d, Market.BountyFor(Enemy));
        Assert.AreEqual(before, Player.Money);
    }

    [TestMethod]
    public void DestroyedRentalCannotClaimBountyAndCapturedRentalCannotBeSalvaged()
    {
        var lease = Hire();
        Market.Contribute(Enemy, Player, 1000);
        Ship ship = lease.Ships[0];
        Market.RecordAssetLoss(ship, Player, Faction, captured: true);
        Assert.AreEqual(1000d, Market.BountyFor(Player));
        double investment = Faction.Pirates.Market.Investment;
        Faction.Pirates.TakeInLandedShip(ship);
        Assert.AreEqual(investment, Faction.Pirates.Market.Investment);
    }
}
