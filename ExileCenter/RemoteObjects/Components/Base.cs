// <copyright file="Base.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Base" /> component in the entity. Present on item entities, it
    ///     exposes the item's base type: the rendered (localized) display name and the
    ///     internal, locale-independent BaseItemTypes id.
    /// </summary>
    /// <remarks>
    ///     Reading the display name straight from memory means callers no longer need the
    ///     user to copy an item to the clipboard to learn its name (the old RitualHelper
    ///     workflow). It also disambiguates currency/rune/essence TIERS that share one art.
    /// </remarks>
    public class Base : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Base" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Base" /> component.</param>
        public Base(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the rendered, localized base-type display name (e.g. "Greater Orb of Augmentation").
        ///     The price-lookup key for non-unique items. Empty when unavailable.
        /// </summary>
        public string BaseItemName { get; private set; } = string.Empty;

        /// <summary>
        ///     Gets the internal, locale-independent BaseItemTypes id (e.g. "CurrencyAddModToMagic2").
        ///     A stable key for matching across languages/patches. Empty when unavailable.
        /// </summary>
        public string InternalName { get; private set; } = string.Empty;

        /// <summary>
        ///     Converts the <see cref="Base" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Base Item Name: {this.BaseItemName}");
            ImGui.Text($"Internal Name: {this.InternalName}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<BaseOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;

            this.BaseItemName = string.Empty;
            if (data.DisplayNameRowPtr != IntPtr.Zero)
            {
                var namePtr = reader.ReadMemory<IntPtr>(data.DisplayNameRowPtr + BaseOffsets.DisplayNameOffset);
                if (namePtr != IntPtr.Zero)
                {
                    this.BaseItemName = reader.ReadUnicodeString(namePtr);
                }
            }

            this.InternalName = string.Empty;
            if (data.BaseItemTypesRowPtr != IntPtr.Zero)
            {
                var idPtr = reader.ReadMemory<IntPtr>(data.BaseItemTypesRowPtr);
                if (idPtr != IntPtr.Zero)
                {
                    this.InternalName = reader.ReadUnicodeString(idPtr);
                }
            }
        }
    }
}
