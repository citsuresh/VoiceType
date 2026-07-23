using System;
using System.Windows.Media;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI
{
    /// <summary>
    /// Shared helper for building the floating waveform pill's background brush from the
    /// configured <see cref="VoiceTypeSettings.PillColor"/> and <see cref="VoiceTypeSettings.PillOpacity"/>.
    /// Used by both the overlay windows and the Settings appearance preview.
    /// </summary>
    public static class PillAppearance
    {
        // Fallback color/opacity matching the app's original hardcoded pill look.
        private const string DefaultColorHex = "#283593";
        private const double DefaultOpacity = 0.9;

        /// <summary>
        /// Builds a frozen <see cref="SolidColorBrush"/> from the settings' pill color and
        /// opacity. Falls back to the original default look if the color string is invalid.
        /// </summary>
        public static SolidColorBrush CreateBrush(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return CreateBrush(settings.PillColor, settings.PillOpacity);
        }

        /// <summary>
        /// Builds a frozen <see cref="SolidColorBrush"/> from an explicit color hex string and
        /// opacity (0.0-1.0). Falls back to the original default look if the color is invalid.
        /// </summary>
        public static SolidColorBrush CreateBrush(string? colorHex, double opacity)
        {
            var color = ParseColor(colorHex) ?? ParseColor(DefaultColorHex)!.Value;
            color.A = ToAlphaByte(opacity);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Parses a "#RRGGBB" or "#AARRGGBB" color string. Returns null when parsing fails.
        /// </summary>
        public static Color? ParseColor(string? colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
                return null;

            try
            {
                var converted = ColorConverter.ConvertFromString(colorHex.Trim());
                return converted is Color color ? color : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Clamps an opacity (0.0-1.0) and converts it to a 0-255 alpha byte.</summary>
        public static byte ToAlphaByte(double opacity)
        {
            var clamped = Math.Clamp(opacity, 0.0, 1.0);
            return (byte)Math.Round(clamped * 255.0);
        }

        /// <summary>Default color hex string used when settings contain an invalid value.</summary>
        public static string DefaultColor => DefaultColorHex;

        /// <summary>Default opacity used when settings contain an invalid value.</summary>
        public static double DefaultOpacityValue => DefaultOpacity;
    }
}
