// <copyright file="HealthBarsSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SimpleBars
{
    using System.Collections.Generic;
    using ExileCenter.Plugin;

    /// <summary>
    ///     <see cref="SimpleBars" /> plugin settings class.
    /// </summary>
    public sealed class SimpleBarsSettings : IPSettings
    {
        /// <summary>
        ///     Use gradient textures globally for all bars (overrides per-config).
        /// </summary>
        public bool UseGradientBarsGlobal = false;
        /// <summary>
        ///     Draw Healthbars when in town.
        /// </summary>
        public bool DrawInTown = true;

        /// <summary>
        ///     Draw Healthbars when in hideout.
        /// </summary>
        public bool DrawInHideout = true;

        /// <summary>
        ///     Draw Healthbars when game is not foreground.
        /// </summary>
        public bool DrawWhenGameInBackground = false;

        /// <summary>
        ///     % health after which monster is cullable per rarity
        /// </summary>
        public int[] CullingStrikeRangePerRarity = [30, 20, 10, 5];

        /// <summary>
        ///     Interpolate entity position to minimize flicker effect.
        /// </summary>
        public bool InterpolatePosition = true;

        /// <summary>
        ///     Interpolate entity position rate to minimize flickering effect.
        /// </summary>
        public int InterpolationRate = 400;

        /// <summary>
        ///     Healthbar config for monsters.
        /// </summary>
        public Dictionary<string, Config> Monster = new()
        {
            { "white",    new(new(0.5f, 0f, 0f, 1f)) },
            { "magic",    new(new(0f, 0.5f, 1f, 1f), 10f) },
            { "rare",     new(new(1f, 1f, 0f, 1f), 9, true, 16f) },
            { "unique",   new(new(1f, 0.5f, 0f, 1f), 9, true, 16f) },
            { "friendly", new(new(0f, 1f, 0f, 1f)) { Scale = new(64f, 8f), HalfOfScale = new(32f, 4f) } },
        };

        /// <summary>
        ///     Healthbar config for POI monsters.
        /// </summary>
        public Dictionary<int, Config> POIMonster = new()
        {
            { -1, new(new(0.5f, 0.5f, 0.5f, 0.5f)) },
        };

        /// <summary>
        ///     Healthbar config for player.
        /// </summary>
        public Dictionary<string, Config> Player = new()
        {
            { "self", new(new(1f, 0f, 0f, 1f))
                {
                    ShowManaBar = true,
                    UseIndividualBarScale = true,
                    HealthScale = new(128f, 6f),
                    ESScale = new(128f, 4f),
                    ManaScale = new(128f, 4f),
                }
            },
            { "leader", new(new(1f, 0f, 1f, 1f)) },
            { "member", new(new(1f, 0f, 0f, 1f)) },
        };
    }
}
