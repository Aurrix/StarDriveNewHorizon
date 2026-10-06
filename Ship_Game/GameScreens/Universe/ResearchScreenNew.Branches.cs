using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Data.Serialization;
using Ship_Game.Data.Yaml;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game;

[StarDataType]
public sealed class ResearchBranchStyle
{
    [StarData] public string Root;
    [StarData] public string Label;
    [StarData] public string Texture;
    [StarData] public string[] Seeds = Array.Empty<string>();

    // Multi-source traversal: explicitly mapped seeds start new themes. Hidden
    // nodes are never introduced, and shared descendants receive one owner only.
    public static Dictionary<string, int> Assign(string root, TechEntry[] visible, ResearchBranchStyle[] styles)
    {
        var nodes = visible.ToDictionary(t => t.UID, StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        for (int i = 0; i < styles.Length; ++i)
        {
            var style = styles[i];
            if (style == null || !string.Equals(style.Root, root, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string seed in style.Seeds ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(seed) && nodes.ContainsKey(seed) && owners.TryAdd(seed, i))
                    pending.Enqueue(seed);
        }
        while (pending.Count > 0)
        {
            string uid = pending.Dequeue();
            foreach (TechEntry child in nodes[uid].Children)
                if (nodes.ContainsKey(child.UID) && owners.TryAdd(child.UID, owners[uid]))
                    pending.Enqueue(child.UID);
        }
        return owners;
    }
}

public sealed partial class ResearchScreenNew
{
    ResearchBranchStyle[] BranchStyles = Array.Empty<ResearchBranchStyle>();
    readonly List<ResearchBand> BranchBands = new();
    sealed class ResearchBand
    {
        public string Label;
        public SubTexture Texture;
        public RectF Bounds;
    }

    void LoadResearchBranchStyles()
    {
        BranchStyles = Array.Empty<ResearchBranchStyle>();
        BranchBands.Clear();
        var file = ResourceManager.GetModOrVanillaFile("ResearchBranchStyles.yaml");
        if (file == null) return;
        try { BranchStyles = YamlParser.DeserializeArray<ResearchBranchStyle>(file).ToArray(); }
        catch (Exception e) { Log.Warning($"Research branch artwork disabled: {e.Message}"); }
    }

    void LayoutResearchBands(RootNode root)
    {
        BranchBands.Clear();
        var nodes = SubNodes.Values.ToArray();
        var owners = ResearchBranchStyle.Assign(root.Entry.UID, nodes.Select(n => n.Entry).ToArray(), BranchStyles);
        if (owners.Count == 0) return; // unknown root or mod tree: retain original layout
        float y = TreeViewport.Y;
        float width = Math.Max(TreeViewport.W, nodes.Max(n => n.BaseRect.Right + 90) - TreeViewport.X);
        for (int group = 0; group <= BranchStyles.Length; ++group)
        {
            int id = group == BranchStyles.Length ? -1 : group;
            var members = nodes.Where(n => (owners.TryGetValue(n.Entry.UID, out int owner) ? owner : -1) == id).ToArray();
            if (members.Length == 0) continue;
            var rows = members.Select(n => n.NodePosition.Y).Distinct().OrderBy(row => row).ToArray();
            float height = Math.Max(180, rows.Length * GridHeight + 58);
            ResearchBranchStyle style = id < 0 ? null : BranchStyles[id];
            var band = new ResearchBand
            {
                Label = style?.Label ?? "Other technologies",
                Texture = string.IsNullOrWhiteSpace(style?.Texture) ? null : ResourceManager.TextureOrNull(style.Texture),
                Bounds = new RectF(TreeViewport.X, y, width, height)
            };
            BranchBands.Add(band);
            foreach (TreeNode node in members)
            {
                int row = Array.IndexOf(rows, node.NodePosition.Y);
                node.SetPos(new Vector2(node.BaseRect.X, y + 58 + row * GridHeight));
            }
            y += height + 28;
        }
        root.RootRect.Y = (int)(TreeViewport.Y + 86);
    }

    void DrawResearchBands(SpriteBatch batch)
    {
        foreach (var band in BranchBands)
        {
            if (band.Texture != null)
            {
                var texture = band.Texture;
                float aspect = band.Bounds.W / band.Bounds.H;
                int sourceW = texture.Width, sourceH = texture.Height;
                if ((float)sourceW / sourceH > aspect) sourceW = Math.Max(1, (int)(sourceH * aspect));
                else sourceH = Math.Max(1, (int)(sourceW / aspect));
                var source = new Microsoft.Xna.Framework.Rectangle(texture.X + (texture.Width - sourceW) / 2,
                    texture.Y + (texture.Height - sourceH) / 2, sourceW, sourceH);
                var destination = new Microsoft.Xna.Framework.Rectangle((int)band.Bounds.X, (int)band.Bounds.Y,
                    (int)band.Bounds.W, (int)band.Bounds.H);
                batch.Draw(texture.Texture, destination, source, Color.White);
                batch.FillRectangle(band.Bounds, new Color(3, 13, 25).Alpha(.46f));
                // Gently recede toward the later research columns.
                const int fadeSteps = 64;
                float stripWidth = band.Bounds.W / fadeSteps;
                for (int step = 0; step < fadeSteps; ++step)
                    batch.FillRectangle(new RectF(band.Bounds.X + step * stripWidth, band.Bounds.Y,
                        stripWidth + 1, band.Bounds.H),
                        new Color(3, 13, 25).Alpha(.45f * step / (fadeSteps - 1)));
            }
            else batch.FillRectangle(band.Bounds, new Color(5, 17, 29).Alpha(.8f));
            // Dark label strip plus soft edge shading keeps imagery behind the controls.
            batch.FillRectangle(new RectF(band.Bounds.X, band.Bounds.Y, band.Bounds.W, 30), new Color(3, 13, 25).Alpha(.8f));
            for (int i = 0; i < 24; ++i)
                batch.FillRectangle(new RectF(band.Bounds.X, band.Bounds.Bottom - 24 + i, band.Bounds.W, 1),
                    new Color(3, 13, 25).Alpha(i / 30f));
            batch.DrawString(Fonts.Arial12Bold, (band.Label ?? "Research").ToUpperInvariant(),
                new Vector2(band.Bounds.X + 14, band.Bounds.Y + 7), ResearchCyan);
            batch.DrawRectangle(band.Bounds, ResearchGold.Alpha(.3f));
        }
    }
}
