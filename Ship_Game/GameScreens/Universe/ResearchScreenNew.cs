using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using Ship_Game.Audio;
using System;
using System.Collections.Generic;
using Ship_Game.GameScreens.Universe.Debug;
using SDGraphics;
using SDUtils;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;
using System.Linq;
using Ship_Game.Graphics;

namespace Ship_Game
{
    public sealed partial class ResearchScreenNew : GameScreen
    {
        public override bool HelpKeyOpensCodex => true;

        public readonly UniverseScreen Universe;
        public readonly Empire Player;
        public Camera2D camera = new();

        readonly Map<string, RootNode> RootNodes = new(StringComparer.OrdinalIgnoreCase);
        public Map<string, TreeNode> SubNodes = new(StringComparer.OrdinalIgnoreCase);

        CloseButton Close;
        UITextEntry Search;
        public EmpireUIOverlay empireUI;

        Vector2 MainMenuOffset;

        public ResearchQueueUIComponent Queue;

        int GridWidth  = 175;
        int GridHeight = 100;

        readonly HashSet<(int X, int Y)> ClaimedSpots = new();

        ResearchDebugUnlocks DebugUnlocks;

        public Color ApplyCurrentAlphaColor(Color color) => ApplyCurrentAlphaToColor(color, 100 / 255f);

        public ResearchScreenNew(GameScreen parent, UniverseScreen u, EmpireUIOverlay empireUi)
            : base(parent, toPause: u)
        {
            Universe = u;
            Player = u.Player;
            empireUI = empireUi;
            IsPopup = false;
            CanEscapeFromScreen = true;
            TransitionOnTime = 0.25f;
            TransitionOffTime = 0.25f;
        }

        public override void LoadContent()
        {
            camera = new Camera2D { Pos = GameBase.ScreenCenter };
            CreateResearchLayout();
            LoadResearchBranchStyles();
            RootNodes.Clear();
            SubNodes.Clear();
            var rootTechs = Player.TechEntries.Filter(t => t.IsRoot && t.Discovered);
            rootTechs = rootTechs.Sorted(t => t.Tech.RootNode);
            foreach (TechEntry tech in rootTechs)
            {
                var rootNode = new RootNode(Vector2.Zero, tech) { NodePosition = Vector2.Zero, isResearched = tech.Unlocked };
                RootNodes[tech.UID] = rootNode;
                Categories.AddItem(new CategoryRow(this, rootNode));
            }
            if (!RootNodes.TryGetValue(Universe.UState.ResearchRootUIDToDisplay ?? "", out RootNode root))
                root = RootNodes.Values.FirstOrDefault();
            if (root != null) PopulateNodesFromRoot(root);

            // Create queue once all techs are populated
            Queue = Add(new ResearchQueueUIComponent(this, QueueBounds));
            CreateResearchSearch();

            DebugUnlocks = Add(new ResearchDebugUnlocks(Universe, () =>
            {
                Universe.UState.ResearchRootUIDToDisplay = GetCurrentlySelectedRootNode()?.Entry.UID ?? "";
                ReloadContent();
            }));
            DebugUnlocks.AxisAlign = Align.BottomRight;
            DebugUnlocks.SetLocalPos(-25, -Queue.Height - 40);

            base.LoadContent();
        }

        public override void Update(float fixedDeltaTime)
        {
            DebugUnlocks.Visible = Universe.Debug || Universe is DeveloperUniverse;
            base.Update(fixedDeltaTime);
        }

        public void OnSearchButtonClicked(UIButton button)
        {
            ScreenManager.AddScreen(new SearchTechScreen(this));
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);

            batch.SafeBegin();
            DrawResearchChrome(batch);
            batch.SafeEnd();

            RenderStates.EnableScissorTest(batch.GraphicsDevice, TreeViewport);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, rasterizerState: RenderStates.ScissorEnabled,
                transformMatrix: camera.Transform);
            try
            {
                if (!SearchResults.Visible)
                {
                    DrawResearchBands(batch);
                    DrawConnectingLines(batch);
                    foreach (TreeNode treeNode in SubNodes.Values)
                        treeNode.Draw(batch);
                }
            }
            finally
            {
                batch.SafeEnd();
                RenderStates.DisableScissorTest(batch.GraphicsDevice);
            }

            batch.SafeBegin();
            base.Draw(batch, elapsed);
            batch.SafeEnd();
        }

        static Vector2 CenterBetweenPoints(Vector2 left, Vector2 right)
        {
            return left.LerpTo(right, 0.5f).Rounded();
        }

        RootNode GetCurrentlySelectedRootNode()
        {
            foreach (RootNode root in RootNodes.Values)
                if (root.nodeState == NodeState.Press)
                    return root;
            return null;
        }

        // the center-right connector point of the parent node
        Vector2 GetParentConnectorPoint(Node parent)
        {
            return (parent is RootNode root) ? root.RightPoint : ((TreeNode)parent).RightPoint;
        }

        Vector2 GetBranchMidPoint(Node parent)
        {
            Vector2 parentNode = GetParentConnectorPoint(parent);

            // for the ROOT nodes, the midpoint is a bit closer
            if (parent is RootNode)
                return new(parentNode.X + (int)(GridWidth / 3), parentNode.Y);
            return new(parentNode.X + (int)(GridWidth / 2), parentNode.Y);
        }

        void DrawLinesFromParentToChild(SpriteBatch batch, Node parent, TechEntry child)
        {
            if (SubNodes.TryGetValue(child.UID, out TreeNode node))
            {
                Vector2 branchMidPoint = GetBranchMidPoint(parent);
                Vector2 verticalEnd = new(branchMidPoint.X, node.BaseRect.CenterY - 10);
                Vector2 endPos = new(node.BaseRect.X + 13f, verticalEnd.Y);

                // draw the vertical line which connects us from branch middle junction towards the child tech
                DrawResearchLineVertical(batch, branchMidPoint, verticalEnd, child.Unlocked);

                // draw the final horizontal connection from middle junction to endPos
                DrawResearchLineHorizontal(batch, verticalEnd, endPos, child.Unlocked, gradient: true);
            }
        }

        void DrawLineFromParentToBranchMiddle(SpriteBatch batch, Node parent, bool anyTechsComplete)
        {
            // from parent node to the middle of the branch junction
            Vector2 parentNode = GetParentConnectorPoint(parent);
            Vector2 branchMidPoint = GetBranchMidPoint(parent);
            DrawResearchLineHorizontal(batch, parentNode, branchMidPoint, anyTechsComplete, gradient:false);
        }

        void DrawConnectingLinesFromParentToChildren(SpriteBatch batch, Node parent)
        {
            bool anyTechsComplete = false;
            bool discoveredAny = false;
            foreach (TechEntry maybeUndiscovered in parent.Entry.Children)
            {
                // scan from `maybeUndiscovered` (inclusive) until we find a discovered tech
                // this would skip over any secret techs in the middle 
                TechEntry toTech = maybeUndiscovered.FindNextDiscoveredTech(Player);
                if (toTech != null)
                {
                    discoveredAny = true;
                    anyTechsComplete |= toTech.Unlocked;
                    DrawLinesFromParentToChild(batch, parent, toTech);
                }
            }

            // from parent tech to the middle of the branch junction
            if (discoveredAny)
            {
                DrawLineFromParentToBranchMiddle(batch, parent, anyTechsComplete);
            }
        }

        void DrawConnectingLines(SpriteBatch batch)
        {
            foreach (TreeNode from in SubNodes.Values)
            {
                DrawConnectingLinesFromParentToChildren(batch, from);
            }
        }

        static void DrawResearchLineHorizontal(SpriteBatch batch, Vector2 left, Vector2 right, bool complete, bool gradient)
        {
            if (left.X > right.X) // top must have lower X
                Vectors.Swap(ref left, ref right);

            SubTexture texture;
            if (gradient)
            {
                texture = ResourceManager.Texture(complete
                        ? "ResearchMenu/grid_horiz_gradient_complete"
                        : "ResearchMenu/grid_horiz_gradient");
            }
            else
            {
                texture = ResourceManager.Texture(complete
                        ? "ResearchMenu/grid_horiz_complete"
                        : "ResearchMenu/grid_horiz");
            }

            RectF r = new(left.X + 5, left.Y - 2, (right.X - left.X) - 5, 5);
            //batch.Draw(texture, r, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 1f);
            batch.Draw(texture, r, Color.White);

            // fill a small rectangle at the beginning of the research line
            // to cover up some stupid artifacts caused by XNA transparent sprite renderer
            batch.FillRectangle(new Rectangle((int)left.X, (int)left.Y, 5, 1), (complete ? new(110, 171, 227) : new(194, 194, 194)));
        }

        static void DrawResearchLineVertical(SpriteBatch batch, Vector2 top, Vector2 bottom, bool complete)
        {
            if (top.Y > bottom.Y) // top must have lower Y
                Vectors.Swap(ref top, ref bottom);

            SubTexture texture = ResourceManager.Texture(complete
                               ? "ResearchMenu/grid_vert_complete"
                               : "ResearchMenu/grid_vert");

            // shift the line down a bit to avoid overlapping transparency artifacts
            int offsetY = 1;
            RectF r = new(top.X - texture.CenterX, top.Y + offsetY, texture.Width, (bottom.Y - top.Y) - offsetY);
            //batch.Draw(texture, r, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 1f);
            batch.Draw(texture, r, Color.White);
        }


        public override void ExitScreen()
        {
            Search?.StopInput();
            Universe.UState.ResearchRootUIDToDisplay = GetCurrentlySelectedRootNode()?.Entry.UID ?? "";
            base.ExitScreen();
        }

        int FindDeepestYSubNodes()
        {
            int deepest = 0;
            foreach (TreeNode node in SubNodes.Values)
                if (node.NodePosition.Y > deepest)
                    deepest = (int)node.NodePosition.Y;
            return deepest;
        }

        public override bool HandleInput(InputState input)
        {
            if (!Visible || !Enabled || !IsActive) return false;
            bool wasTyping = Search.HandlingInput;
            if ((wasTyping || SearchResults.Visible) && input.Escaped)
            {
                ClearResearchSearch();
                return true;
            }
            if (Search.HandleInput(input)) return true;
            if (!SearchResults.Visible && TreeViewport.HitTest(input.CursorPosition))
            {
                if (input.MiddleMouseHeld())
                {
                    PanTree(input.CursorVelocity);
                    return true;
                }
                if (input.ScrollIn || input.ScrollOut)
                {
                    float delta = input.ScrollIn ? -90 : 90;
                    PanTree(input.IsShiftKeyDown ? new Vector2(delta, 0) : new Vector2(0, delta));
                    return true;
                }
                foreach (TreeNode node in SubNodes.Values)
                {
                    if (node.HandleInput(input, ScreenManager, camera, Universe))
                    {
                        if (input.LeftMouseClick && !input.RightMouseClick) OnTechNodeClicked(node.Entry);
                        return true;
                    }
                }
            }
            else
            {
                foreach (TreeNode node in SubNodes.Values) node.State = NodeState.Normal;
            }
            if (base.HandleInput(input)) return true;
            if (input.ResearchExitScreen || input.RightMouseClick)
            {
                GameAudio.EchoAffirmative();
                ExitScreen();
                return true;
            }

            return false;
        }

        void OnTechNodeClicked(TechEntry tech)
        {
            if (!tech.CanBeResearched)
            {
                // this tech cannot be researched
                GameAudio.NegativeClick();
                return;
            }
            
            bool added = false;
            
            if (!Player.Research.IsQueued(tech.UID))
            {
                GameAudio.ResearchSelect();
                Player.Research.AddTechToQueue(tech.UID);
                added = true;
            }
            
            // if ctrl is held down, move tech to top of queue ALWAYS, even if it was already in queue (imo good UX)
            if(GameBase.ScreenManager.input != null && GameBase.ScreenManager.input.IsCtrlKeyDown)
            {
                int index = Player.Research.IndexInQueue(tech.UID);
                int moved = Player.Research.MoveToTopWithPreReqs(index);
                if (moved == 0)
                {
                    GameAudio.NegativeClick();
                }
            }
            
            // if CTRL was not held down, and tech is in queue (but not added right now), remove it
            else
            {
                if (!added)
                {
                    Player.Research.RemoveTechFromQueue(tech.UID);
                }
            }
            
            Queue.ReloadResearchQueue();
        }

        Vector2 GridSize => new(GridWidth, GridHeight);

        Vector2 GetCurrentCursorOffset(in Vector2 cursorPos, float yOffset = 0)
        {
            // Category roots live in the sidebar/header, not in the research canvas.
            var cursor = new Vector2(cursorPos.X - 1, cursorPos.Y + yOffset);
            return (MainMenuOffset + cursor*GridSize).Rounded();
        }

        void PopulateNodesFromRoot(RootNode root)
        {
            foreach (RootNode node in RootNodes.Values)
                node.nodeState = (node == root) ? NodeState.Press : NodeState.Normal;

            int rows = 1;
            int cols = CalculateTreeDimensionsFromRoot(root.Entry, ref rows, 0, 0);
            GridHeight = Math.Clamp((int)(TreeViewport.H - 60) / Math.Max(1, rows), 125, 170);
            GridWidth = Math.Clamp((int)(TreeViewport.W - 60) / Math.Max(1, cols), 185, 245);
            MainMenuOffset = new Vector2(TreeViewport.X + 16, TreeViewport.Y + 40);
            root.RootRect = new Rectangle((int)MainMenuOffset.X, (int)TreeViewport.CenterY - 22, 112, 44);
            camera.Pos = GameBase.ScreenCenter;
            Universe.UState.ResearchRootUIDToDisplay = root.Entry.UID;

            BuildSubNodes(root);

            // the estimate counts merged-back branches and reused rows as new rows: rebuild at the rows laid out
            int wantRows = FindDeepestYSubNodes() + 1;
            if (wantRows != rows)
            {
                GridHeight = Math.Clamp((int)(TreeViewport.H - 60) / wantRows, 125, 170);
                BuildSubNodes(root);
            }
            LayoutResearchBands(root);
        }

        void BuildSubNodes(RootNode root)
        {
            SubNodes.Clear();
            ClaimedSpots.Clear();

            var nodePos = new Vector2(1f, 1f);
            bool first = true;

            foreach (TechEntry child in root.Entry.Children)
            {
                if (!child.Discovered)
                    continue;

                nodePos.X = root.NodePosition.X + 1f;
                // row 0, not the root's Y: that is its slot in the category list
                nodePos.Y = first ? 0 : FindFreeRowFor(child, 0, (int)nodePos.X);
                if (first) first = false;

                if (!SubNodes.ContainsKey(child.UID)) // only ever add unique entries
                {
                    var newNode = new TreeNode(GetCurrentCursorOffset(nodePos), child, this) { NodePosition = nodePos };
                    SubNodes[newNode.Entry.UID] = newNode;
                    PopulateNodesFromSubNode(newNode, ref nodePos);
                }
            }
        }

        void PopulateNodesFromSubNode(Node node, ref Vector2 nodePos)
        {
            UpdateCursorAndClaimedSpots(ref nodePos, node.Entry.Discovered);

            bool first = true;
            foreach (TechEntry child in node.Entry.Children)
            {
                nodePos.X = node.NodePosition.X + 1f;
                nodePos.Y = first ? node.NodePosition.Y
                                  : FindFreeRowFor(child, (int)node.NodePosition.Y, (int)nodePos.X);
                if (first) first = false;

                if (child.Discovered && !SubNodes.ContainsKey(child.UID))
                {
                    var newNode = new TreeNode(GetCurrentCursorOffset(nodePos), child, this) { NodePosition = nodePos };
                    SubNodes[newNode.Entry.UID] = newNode;
                    PopulateNodesFromSubNode(newNode, ref nodePos);
                }
            }
        }

        void UpdateCursorAndClaimedSpots(ref Vector2 nodePos, bool addToClaimed)
        {
            if (PositionIsClaimed(nodePos))
                nodePos.Y += 1f;
            else if (addToClaimed)
                ClaimedSpots.Add(((int)nodePos.X, (int)nodePos.Y));
        }
        
        bool PositionIsClaimed(Vector2 position) => ClaimedSpots.Contains(((int)position.X, (int)position.Y));

        int FindFreeRowFor(TechEntry branch, int parentY, int col)
        {
            int bRows = 1;
            int bCols = MeasureDiscoveredBranch(branch, ref bRows, 0, 0);
            int last = 0; // first row below everything claimed: always free
            foreach ((int X, int Y) spot in ClaimedSpots)
                last = Math.Max(last, spot.Y + 1);
            for (int y = parentY; y < last; ++y)
            {
                bool freeRect = true;
                for (int dy = 0; dy < bRows && freeRect; ++dy)
                    for (int dx = 0; dx < bCols && freeRect; ++dx)
                        if (PositionIsClaimed(new Vector2(col + dx, y + dy)))
                            freeRect = false;
                if (freeRect)
                    return y;
            }
            return Math.Max(parentY, last);
        }

        // discovered techs only, since placement never places undiscovered ones
        int MeasureDiscoveredBranch(TechEntry techEntry, ref int rows, int cols, int colmax)
        {
            cols++;
            if (cols > colmax)
                colmax = cols;

            TechEntry[] children = techEntry.Children;
            if (children.Length > 0)
            {
                int rowCount = 0;
                for (int i = 1; i < children.Length; i++)
                {
                    if (children[i].FindNextDiscoveredTech(Player) != null)
                        rowCount++;
                }
                rows += rowCount;
            }

            foreach (TechEntry child in children)
            {
                var discovered = child.FindNextDiscoveredTech(Player);
                if (discovered != null)
                {
                    int max = MeasureDiscoveredBranch(discovered, ref rows, cols, colmax);
                    if (max > colmax)
                        colmax = max;
                }
            }
            return colmax;
        }

        //Added by McShooterz: find size of tech tree before it is built
        int CalculateTreeDimensionsFromRoot(TechEntry techEntry, ref int rows, int cols, int colmax)
        {
            cols++;
            if (cols > colmax)
                colmax = cols;

            TechEntry[] children = techEntry.Children;

            // look for branches and make space for them
            if (children.Length > 0)
            {
                int rowCount = 0;
                // don't count the main branch. use the branch that starts here.
                for (int i = 1; i < children.Length; i++)
                {
                    var discovered = children[i].FindNextDiscoveredTech(Player);
                    if (discovered != null)
                        rowCount++;
                }
                rows += rowCount;
            }

            foreach (TechEntry maybeUndiscovered in children)
            {
                // TODO: not sure why this pattern is used here?
                // scan from `maybeUndiscovered` (inclusive) until we find a discovered tech
                var discovered = maybeUndiscovered.FindNextDiscoveredTech(Player);
                if (discovered != null)
                {
                    int max = CalculateTreeDimensionsFromRoot(discovered, ref rows, cols, colmax);
                    if (max > colmax)
                        colmax = max;
                }
                else
                {
                    CalculateTreeDimensionsFromRoot(maybeUndiscovered, ref rows, cols, colmax);
                }
            }
            return colmax;
        }
    }
}
