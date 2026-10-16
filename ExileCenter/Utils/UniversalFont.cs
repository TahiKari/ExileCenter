// <copyright file="UniversalFont.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using ClickableTransparentOverlay;
    using ImGuiNET;

    /// <summary>
    ///     Loads a bundled "universal" font into the overlay so text in ANY language renders across
    ///     the whole overlay (every plugin draws through the shared ImGui atlas, so they all benefit).
    ///
    ///     One merged font is built (first source that has a glyph wins; later sources only fill gaps):
    ///       1. DejaVuSans      — pretty Latin + Cyrillic + Greek (priority for those).
    ///       2. the configured GH font (<see cref="GameHelper.Settings.State.FontPathName"/>) — keeps
    ///          the user's language pretty (e.g. CJK), merged so it only fills DejaVu's gaps.
    ///       3. GNU Unifont     — fallback over the whole BMP (CJK/Arabic/Hebrew/Thai/Armenian/…).
    ///
    ///     Gated by <see cref="GameHelper.Settings.State.UniversalFont"/>; when off, the normal
    ///     configured font is loaded instead. Call <see cref="ApplyFromSettings"/> on startup and
    ///     whenever the font settings change.
    /// </summary>
    internal static class UniversalFont
    {
        // Whole Basic Multilingual Plane for the Unifont fallback. ImGui keeps this pointer until the
        // atlas is built (on the render thread), so the array must stay pinned for the app lifetime.
        private static readonly ushort[] FullBmpRange = { 0x0020, 0xFFFF, 0x0000 };
        private static GCHandle rangeHandle;

        /// <summary>
        ///     Loads the universal font when <see cref="GameHelper.Settings.State.UniversalFont"/> is
        ///     set, otherwise the normally-configured font. Safe to call before the overlay exists.
        /// </summary>
        public static void ApplyFromSettings()
        {
            if (Core.Overlay == null)
            {
                return;
            }

            if (Core.GHSettings.UniversalFont)
            {
                Apply();
            }
            else
            {
                ApplyConfigured();
            }
        }

        /// <summary>
        ///     Loads the normally-configured GH font (honouring the custom glyph range when set).
        /// </summary>
        public static void ApplyConfigured()
        {
            if (Core.Overlay == null)
            {
                return;
            }

            if (MiscHelper.TryConvertStringToImGuiGlyphRanges(Core.GHSettings.FontCustomGlyphRange, out var glyphRanges))
            {
                Core.Overlay.ReplaceFont(Core.GHSettings.FontPathName, Core.GHSettings.FontSize, glyphRanges);
            }
            else
            {
                Core.Overlay.ReplaceFont(Core.GHSettings.FontPathName, Core.GHSettings.FontSize, Core.GHSettings.FontLanguage);
            }
        }

        private static unsafe void Apply()
        {
            var fontsDir = Path.Combine(AppContext.BaseDirectory, "fonts");
            var dejaVu = Path.Combine(fontsDir, "DejaVuSans.ttf");
            var unifont = Path.Combine(fontsDir, "unifont.ttf");
            if (!File.Exists(unifont) && !File.Exists(dejaVu))
            {
                // Bundled fonts missing — fall back to the configured font rather than an empty atlas.
                ApplyConfigured();
                return;
            }

            if (!rangeHandle.IsAllocated)
            {
                rangeHandle = GCHandle.Alloc(FullBmpRange, GCHandleType.Pinned);
            }

            var fullBmpPtr = rangeHandle.AddrOfPinnedObject();
            float size = Core.GHSettings.FontSize;
            var userFont = Core.GHSettings.FontPathName;
            var userLang = Core.GHSettings.FontLanguage;

            Core.Overlay.ReplaceFont(cfgRaw =>
            {
                var io = ImGui.GetIO();
                var fonts = io.Fonts;
                fonts.Clear(); // start from a known-empty atlas regardless of prior state

                // The full-BMP Unifont fallback is ~65k glyphs; with ImGui's default oversampling the
                // packed atlas can exceed D3D11's 16384px max texture dimension, and CreateTexture2D
                // then fails with E_INVALIDARG. Unifont is a bitmap font so oversampling adds nothing —
                // force 1x and widen the atlas so the height stays well under the limit.
                fonts.TexDesiredWidth = 8192;

                var cfg = new ImFontConfigPtr(cfgRaw);
                cfg.SizePixels = size;
                cfg.OversampleH = 1;
                cfg.OversampleV = 1;

                bool haveBase = false;

                // 1) DejaVuSans — Latin + Cyrillic + Greek.
                if (File.Exists(dejaVu))
                {
                    cfg.MergeMode = false;
                    fonts.AddFontFromFileTTF(dejaVu, size, cfg, fonts.GetGlyphRangesCyrillic());
                    haveBase = true;
                }

                // 2) The configured GH font (keeps the user's language pretty), merged so it only fills
                //    glyphs DejaVu lacks. Skip if it's missing or the same file as DejaVu.
                if (!string.IsNullOrEmpty(userFont) && File.Exists(userFont) &&
                    !string.Equals(userFont, dejaVu, StringComparison.OrdinalIgnoreCase))
                {
                    cfg.MergeMode = haveBase;
                    fonts.AddFontFromFileTTF(userFont, size, cfg, RangeFor(fonts, userLang));
                    haveBase = true;
                }

                // 3) Unifont fallback over the whole BMP — fills everything still missing.
                if (File.Exists(unifont))
                {
                    cfg.MergeMode = haveBase;
                    fonts.AddFontFromFileTTF(unifont, size, cfg, fullBmpPtr);
                    haveBase = true;
                }

                if (!haveBase)
                {
                    fonts.AddFontDefault();
                }

                cfg.MergeMode = false;
            });
        }

        private static IntPtr RangeFor(ImFontAtlasPtr fonts, FontGlyphRangeType lang) => lang switch
        {
            FontGlyphRangeType.ChineseSimplifiedCommon => fonts.GetGlyphRangesChineseSimplifiedCommon(),
            FontGlyphRangeType.ChineseFull => fonts.GetGlyphRangesChineseFull(),
            FontGlyphRangeType.Japanese => fonts.GetGlyphRangesJapanese(),
            FontGlyphRangeType.Korean => fonts.GetGlyphRangesKorean(),
            FontGlyphRangeType.Thai => fonts.GetGlyphRangesThai(),
            FontGlyphRangeType.Vietnamese => fonts.GetGlyphRangesVietnamese(),
            FontGlyphRangeType.Cyrillic => fonts.GetGlyphRangesCyrillic(),
            _ => fonts.GetGlyphRangesDefault(),
        };
    }
}
