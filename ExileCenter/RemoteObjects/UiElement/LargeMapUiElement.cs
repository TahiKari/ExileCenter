// <copyright file="LargeMapUiElement.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.UiElement
{
    using System;
    using System.Numerics;
    using GameHelper.Cache;
    using ImGuiNET;

    /// <summary>
    ///     Points to the LargeMap UiElement.
    ///     It is exactly like any other element, except its in-memory position is its center
    /// </summary>
    public class LargeMapUiElement : MapUiElement
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="LargeMapUiElement" /> class.
        /// </summary>
        /// <param name="address">address to the Map Ui Element of the game.</param>
        /// <param name="parents">parent cache to use for this Ui Element.</param>
        internal LargeMapUiElement(IntPtr address, UiElementParents parents)
            : base(address, parents) { }

        /// <inheritdoc />
        public override Vector2 Position => new(Core.GameCull.Value, 0f);

        /// <inheritdoc />
        public override Vector2 Size => new(Core.Process.WindowArea.Width - (Core.GameCull.Value * 2), Core.Process.WindowArea.Height);

        /// <summary>
        ///     Gets the center of the map.
        /// </summary>
        public Vector2 Center => base.Position;

        /// <summary>
        ///     Converts the <see cref="LargeMapUiElement" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Center (without shift/default-shift) {this.Center}");
        }
    }
}