using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game;

namespace UnitTests.UI;

[TestClass]
public class ResearchScreenTests : StarDriveTest
{
    public TestContext TestContext { get; set; }

    public ResearchScreenTests()
    {
        CreateUniverseAndPlayerEmpire();
        AddHomeWorldToEmpire(new Vector2(1000), Player);
    }

    [TestMethod]
    public void BranchStylesRespectSeedsVisibilityAndUnknownModRoots()
    {
        Game.Tick();
        using var screen = new ResearchScreenNew(Universe, Universe,
            new EmpireUIOverlay(Player, Game.GraphicsDevice, Universe));
        Universe.UState.ResearchRootUIDToDisplay = "Colonization";
        screen.LoadContent();
        var styles = Ship_Game.Data.Yaml.YamlParser.DeserializeArray<ResearchBranchStyle>("ResearchBranchStyles.yaml").ToArray();
        foreach (var root in Player.TechEntries.Where(t => t.IsRoot))
            Assert.IsTrue(styles.Any(s => s.Root == root.UID), $"Missing category artwork mapping: {root.UID}");
        foreach (string texture in styles.Select(s => s.Texture).Distinct())
            Assert.IsNotNull(ResourceManager.TextureOrNull(texture), $"Missing artwork: {texture}");
        var entries = screen.SubNodes.Values.Select(n => n.Entry).ToArray();
        var assignments = ResearchBranchStyle.Assign("Colonization", entries, styles);
        Assert.AreEqual(0, assignments["Aeroponics"]);
        Assert.AreEqual(0, assignments["Aeroponics"]);
        Assert.AreEqual(1, assignments["ScientificFoundations"]);
        Assert.AreEqual(1, assignments["SystemNetworks"]);
        Assert.AreEqual(2, assignments["IndustrialFoundations"]);
        Assert.AreEqual(2, assignments["GeoSurvey"]);
        Assert.AreEqual("Colonization", Player.GetTechEntry("ScientificFoundations").GetPreReq(Player).UID,
            "The visual science band must not change research prerequisites");
        Assert.AreEqual(0, ResearchBranchStyle.Assign("UnknownModRoot", entries, styles).Count);
        Assert.AreEqual(0, ResearchBranchStyle.Assign("Colonization", entries, System.Array.Empty<ResearchBranchStyle>()).Count);
        Assert.AreEqual(0, ResearchBranchStyle.Assign("Colonization", entries,
            new[] { new ResearchBranchStyle { Root = "Colonization", Seeds = new[] { "MissingModTechnology" } } }).Count);
        var hidden = ResearchBranchStyle.Assign("Colonization", entries.Where(t => t.UID != "Aeroponics").ToArray(), styles);
        Assert.IsFalse(hidden.ContainsKey("Aeroponics"));
        Assert.IsFalse(hidden.ContainsKey("XenoFarming"), "Do not invent a visible seed from a hidden technology");
        var science = screen.SubNodes["ScientificFoundations"].BaseRect;
        var industry = screen.SubNodes["IndustrialFoundations"].BaseRect;
        Assert.IsTrue(science.Y < industry.Y, "Science is presented in its own band before industry");
        Assert.IsTrue(screen.SubNodes["Aeroponics"].BaseRect.X < screen.TreeViewport.X + 30,
            "Removing the root card must reclaim its empty column");
        typeof(ResearchScreenNew).GetMethod("PanTree", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(screen, new object[] { new Vector2(0, 100000) });
        var industryOnScreen = screen.camera.GetScreenSpaceFromWorldSpace(screen.SubNodes["IS Government"].BaseRect.Center);
        Assert.IsTrue(screen.TreeViewport.HitTest(industryOnScreen), "The final industry band must remain reachable by scrolling");
    }

    [TestMethod]
    public void DockReordersIndependentResearchButProtectsPrerequisitesAndRemovesDependents()
    {
        Game.Tick();
        using var screen = new ResearchScreenNew(Universe, Universe,
            new EmpireUIOverlay(Player, Game.GraphicsDevice, Universe));
        screen.LoadContent();
        Player.Research.AddTechToQueue("XenoFarming");
        Player.Research.AddTechToQueue("IndustrialFoundations");
        screen.Queue.ReloadResearchQueue();
        Assert.AreEqual("Aeroponics", Player.Research.Topic);
        void SelectQueued(int index)
        {
            var rect = (RectF)typeof(ResearchQueueUIComponent)
                .GetMethod("QueuedIcon", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(screen.Queue, new object[] { index });
            var provider = new MockInputProvider { MousePos = rect.Center };
            var input = new InputState { Provider = provider };
            input.Update(new UpdateTimes(.016f, 1));
            provider.LeftMouse = SDGraphics.Input.ButtonState.Pressed;
            input.Update(new UpdateTimes(.016f, 1));
            screen.Queue.HandleInput(input);
            provider.LeftMouse = SDGraphics.Input.ButtonState.Released;
            input.Update(new UpdateTimes(.016f, 1));
            screen.Queue.HandleInput(input);
        }
        SelectQueued(0);
        Assert.AreEqual("XenoFarming", screen.Queue.SelectedUid);
        Assert.IsTrue(screen.Find<UIButton>("ResearchQueueEarlier", out var earlier));
        Assert.IsFalse(earlier.Enabled, "Cannot move XenoFarming above its prerequisite");
        SelectQueued(1);
        Assert.AreEqual("IndustrialFoundations", screen.Queue.SelectedUid);
        Assert.IsTrue(earlier.Enabled);
        earlier.OnClick(earlier);
        earlier.OnClick(earlier);
        Assert.AreEqual("IndustrialFoundations", Player.Research.Topic);
        Assert.IsTrue(screen.Find<UIButton>("ResearchQueueRemove", out var remove));
        remove.OnClick(remove);
        Assert.AreEqual("Aeroponics", Player.Research.Topic);
        Assert.AreEqual("Aeroponics", screen.Queue.SelectedUid);
        remove.OnClick(remove);
        Assert.IsFalse(Player.Research.HasTopic, "Removing Aeroponics also removes dependent XenoFarming");
        Assert.IsFalse(remove.Visible);
        Assert.IsFalse(earlier.Visible);
    }

    [TestMethod]
    [DataRow(1920, 1080)]
    [DataRow(1280, 720)]
    [DataRow(1024, 768)]
    public void CategoriesSelectTreesAndQueueControlsRemainOutsideViewport(int width, int height)
    {
        Game.Tick();
        ResourceManager.Blank ??= ResourceManager.Texture("blank");
        int oldWidth = GameBase.ScreenWidth, oldHeight = GameBase.ScreenHeight;
        void SetSize(int w, int h)
        {
            typeof(GameBase).GetProperty("ScreenWidth").SetValue(null, w);
            typeof(GameBase).GetProperty("ScreenHeight").SetValue(null, h);
            typeof(GameBase).GetProperty("ScreenSize").SetValue(null, new Vector2(w, h));
            typeof(GameBase).GetProperty("ScreenCenter").SetValue(null, new Vector2(w / 2f, h / 2f));
        }
        SetSize(width, height);
        try
        {
            Universe.UState.ResearchRootUIDToDisplay = "missing-mod-category";
            var overlay = new EmpireUIOverlay(Player, Game.GraphicsDevice, Universe);
            using var screen = new ResearchScreenNew(Universe, Universe, overlay);
            screen.LoadContent();
            screen.PreUpdate(new UpdateTimes(1, 1), false, false);
            screen.Update(new UpdateTimes(.016f, 1), true);
            screen.PerformLayout();
            Assert.IsTrue(screen.Find<ScrollListBase>("ResearchCategories", out var categories));
            var rows = ((IEnumerable)categories.GetType().GetProperty("AllEntries").GetValue(categories))
                .Cast<ScrollListItemBase>().ToArray();
            var roots = Player.TechEntries.Where(t => t.IsRoot && t.Discovered).OrderBy(t => t.Tech.RootNode).ToArray();
            CollectionAssert.AreEqual(roots.Select(t => "ResearchCategory-" + t.UID).ToArray(), rows.Select(r => r.Name).ToArray());
            Assert.IsTrue(rows.Length > 0);
            foreach (var row in rows)
            {
                screen.camera.Pos += new Vector2(100, 100);
                categories.OnItemClicked(row);
                Assert.AreEqual(row.Name.Substring("ResearchCategory-".Length), Universe.UState.ResearchRootUIDToDisplay);
                Assert.AreEqual(GameBase.ScreenCenter, screen.camera.Pos, "Switching categories resets tree pan");
            }
            categories.OnItemClicked(rows[0]);
            Assert.IsTrue(screen.SubNodes.Count > 0);
            var tech = screen.SubNodes.Values.FirstOrDefault(n => !n.Entry.Unlocked)?.Entry;
            Assert.IsNotNull(tech);
            screen.Queue.AddToResearchQueue(tech);
            Assert.IsTrue(Player.Research.HasTopic);
            Assert.IsTrue(screen.Find<UITextEntry>("ResearchSearch", out var search));
            Assert.IsTrue(search.Bottom < screen.TreeViewport.Y);
            Assert.IsTrue(screen.Queue.Y > screen.TreeViewport.Bottom);
            Assert.IsTrue(categories.Right <= screen.TreeViewport.X);
            // Search matches technologies across categories, without moving the tree camera.
            var outsideCategory = Player.TechEntries.First(t => t.Discovered && !t.IsRoot && !screen.SubNodes.ContainsKey(t.UID));
            search.Text = outsideCategory.Tech.Name.Text;
            Assert.IsTrue(screen.Find<ScrollListBase>("ResearchSearchResults", out var globalResults));
            Assert.IsTrue(((IEnumerable)globalResults.GetType().GetProperty("AllEntries").GetValue(globalResults))
                .Cast<ScrollListItemBase>().Any(r => r.Name == "ResearchResult-" + outsideCategory.UID));
            search.Text = Player.GetTechEntry("Aeroponics").Tech.Name.Text;
            Assert.IsTrue(screen.Find<ScrollListBase>("ResearchSearchResults", out var results));
            var found = ((IEnumerable)results.GetType().GetProperty("AllEntries").GetValue(results))
                .Cast<ScrollListItemBase>().ToArray();
            Assert.AreEqual(1, found.Length);
            results.OnItemClicked(found[0]);
            Assert.IsTrue(Player.Research.IsQueued("Aeroponics"));
            search.Text = "No such technology name";
            Assert.AreEqual(0, ((IEnumerable)results.GetType().GetProperty("AllEntries").GetValue(results)).Cast<object>().Count());
            search.Clear();
            Assert.IsFalse(results.Visible);
            // Populate enough future research to exercise the overflow controls.
            foreach (var entry in Player.TechEntries.Where(t => t.Discovered && !t.IsRoot && !t.Unlocked).Take(screen.Queue.VisibleCapacity + 4))
                Player.Research.AddTechToQueue(entry.UID);
            screen.Queue.ReloadResearchQueue();
            tech.Progress = tech.TechCost * .62f;
            Assert.IsTrue(screen.Find<UIButton>("ResearchQueueNext", out var next));
            Assert.IsTrue(next.Visible);
            next.OnClick(next);
            Assert.IsTrue(screen.Queue.FirstVisible > 0);
            Assert.IsTrue(screen.Find<UIButton>("ResearchQueuePrevious", out var previousPage));
            previousPage.OnClick(previousPage);
            Assert.AreEqual(0, screen.Queue.FirstVisible);
            // Active icon selection is independent of the panned tree camera.
            screen.camera.Pos += new Vector2(400, 200);
            var provider = new MockInputProvider { MousePos = screen.Queue.ActiveIcon.Center };
            var input = new InputState { Provider = provider };
            input.Update(new UpdateTimes(.016f, 1));
            provider.LeftMouse = SDGraphics.Input.ButtonState.Pressed;
            input.Update(new UpdateTimes(.016f, 1));
            screen.HandleInput(input);
            provider.LeftMouse = SDGraphics.Input.ButtonState.Released;
            input.Update(new UpdateTimes(.016f, 1));
            screen.HandleInput(input);
            Assert.AreEqual(Player.Research.Topic, screen.Queue.SelectedUid);
            Assert.IsTrue(screen.Find<UIButton>("ResearchQueueEarlier", out var earlier));
            Assert.IsFalse(earlier.Enabled, "Active topic cannot be moved earlier");
            screen.camera.Pos = GameBase.ScreenCenter;
            screen.PerformLayout();
            var device = Game.GraphicsDevice;
            using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            var previousRenderTargets = device.GetRenderTargets();
            try
            {
                device.SetRenderTarget(target);
                device.Clear(Microsoft.Xna.Framework.Color.Black);
                screen.Draw(Game.Manager.SpriteBatch, new DrawTimes());
            }
            finally { device.SetRenderTargets(previousRenderTargets); }
            string path = Path.GetFullPath(Path.Combine(StarDriveTestContext.StarDriveAbsolutePath,
                $"../output/imagegen/research-branches-{width}x{height}.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = File.Create(path)) target.SaveAsPng(file, width, height);
            TestContext.AddResultFile(path);
            search.Text = "Terraform";
            screen.PerformLayout();
            try
            {
                device.SetRenderTarget(target);
                device.Clear(Microsoft.Xna.Framework.Color.Black);
                screen.Draw(Game.Manager.SpriteBatch, new DrawTimes());
            }
            finally { device.SetRenderTargets(previousRenderTargets); }
            string searchPath = path.Replace("research-branches-", "research-branches-search-");
            using (var file = File.Create(searchPath)) target.SaveAsPng(file, width, height);
            TestContext.AddResultFile(searchPath);
            provider.KeysDown.Add(SDGraphics.Input.Keys.Escape);
            input.Update(new UpdateTimes(.016f, 1));
            screen.HandleInput(input);
            Assert.IsFalse(screen.IsExiting, "Escape clears search before closing research");
            Assert.IsFalse(results.Visible);
            Assert.AreEqual("", search.Text);
            if (width == 1920)
            {
                foreach (var row in rows)
                {
                    categories.OnItemClicked(row);
                    screen.PerformLayout();
                    try
                    {
                        device.SetRenderTarget(target);
                        device.Clear(Microsoft.Xna.Framework.Color.Black);
                        screen.Draw(Game.Manager.SpriteBatch, new DrawTimes());
                    }
                    finally { device.SetRenderTargets(previousRenderTargets); }
                    string categoryPath = Path.Combine(Path.GetDirectoryName(path),
                        $"research-category-{Universe.UState.ResearchRootUIDToDisplay}.png");
                    using var file = File.Create(categoryPath);
                    target.SaveAsPng(file, width, height);
                    TestContext.AddResultFile(categoryPath);
                    if (Universe.UState.ResearchRootUIDToDisplay == "Colonization")
                    {
                        typeof(ResearchScreenNew).GetMethod("PanTree", BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(screen, new object[] { new Vector2(0, 100000) });
                        try
                        {
                            device.SetRenderTarget(target);
                            device.Clear(Microsoft.Xna.Framework.Color.Black);
                            screen.Draw(Game.Manager.SpriteBatch, new DrawTimes());
                        }
                        finally { device.SetRenderTargets(previousRenderTargets); }
                        string lowerPath = categoryPath.Replace(".png", "-lower.png");
                        using var lowerFile = File.Create(lowerPath);
                        target.SaveAsPng(lowerFile, width, height);
                        TestContext.AddResultFile(lowerPath);
                    }
                }
            }
        }
        finally { SetSize(oldWidth, oldHeight); }
    }
}
