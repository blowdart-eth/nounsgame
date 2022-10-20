﻿using ImGuiNET;
using Microsoft.Xna.Framework;
using Nouns.Editor;
using Nouns.Engine.Pixel2D.Caching;

namespace Nouns.Engine.Pixel2D.Snaps
{
    // ReSharper disable once UnusedMember.Global
    public sealed class LevelBehaviorWindow : IEditorWindow
    {
        public bool Enabled => true;
        public ImGuiWindowFlags Flags => ImGuiWindowFlags.AlwaysAutoResize;
        public string? Label => "Level Behaviors";
        public string? Shortcut => null;
        public int Width => 0;
        public int Height => 0;

        public LevelBehaviorWindow(IServiceProvider serviceProvider)
        {
            
        }

        public void DrawLayout(IEditingContext context, GameTime gameTime, ref bool opened)
        {
            foreach (var levelBehaviorName in LevelBehaviorCache.LevelBehaviors)
            {
                ImGui.Text(levelBehaviorName);
            }
        }
    }
}