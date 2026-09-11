using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>What a badge on a save row is telling you.</summary>
    internal enum BadgeKind
    {
        None,
        Empty,
        Migrate,
        Newer,
        Broken
    }

    /// <summary>
    /// The colours and styles the save window draws with.
    /// </summary>
    /// <remarks>
    /// Two rules hold this together.
    /// <para>
    /// <b>Colour only where it means something.</b> Four semantics, each saying one thing: blue is
    /// the primary action, red destroys, amber wants attention, green is what is live. Everything
    /// else stays grey. A window where seven buttons look alike is a window where the one that
    /// deletes a player's save looks like the one that exports it.
    /// </para>
    /// <para>
    /// <b>The editor's own font, always.</b> Unity draws the editor in its own typeface; a GUIStyle
    /// carrying a different one stands out against the whole IDE. Hierarchy comes from size and
    /// weight instead.
    /// </para>
    /// </remarks>
    internal static class SaveEditorStyles
    {
        private static bool _built;
        private static bool _builtForProSkin;

        // ---- surfaces ----
        internal static Color Window;
        internal static Color Panel;
        internal static Color Toolbar;
        internal static Color Border;
        internal static Color Selection;
        internal static Color SelectionEdge;
        internal static Color TextDim;
        internal static Color RowAlternate;

        // ---- semantics: one meaning each ----
        //
        // Two variants per meaning, and the split matters. The ACCENT paints a shape of its own -
        // an outline, a banner edge, a dot - so it stays saturated in both skins. The TEXT variant
        // sits on the surface, so on a dark skin it has to be lighter than the accent and on a
        // light skin darker. Using one value for both is what made the danger button unreadable in
        // the light theme.
        internal static Color Primary;
        internal static Color Danger;
        internal static Color Warning;
        internal static Color Active;

        internal static Color PrimaryText;
        internal static Color DangerText;
        internal static Color WarningText;
        internal static Color ActiveText;

        /// <summary>How much a destructive button's interior is warmed. Never a red fill.</summary>
        internal static Color DangerTint;

        /// <summary>The tint the one primary button gets.</summary>
        internal static Color PrimaryTint;

        // ---- styles ----
        internal static GUIStyle SaveTitle;
        internal static GUIStyle SchemaChip;
        internal static GUIStyle SectionHeader;
        internal static GUIStyle Badge;
        internal static GUIStyle RowLabel;
        internal static GUIStyle RowLabelSelected;
        internal static GUIStyle PrimaryButton;
        internal static GUIStyle DangerButton;
        internal static GUIStyle BannerText;
        internal static GUIStyle Footnote;

        /// <summary>The asterisk marking a field edited but not yet applied.</summary>
        internal static GUIStyle ChangeMark;

        private static Texture2D _pill;

        /// <summary>Builds the styles once, and again when the user switches skin.</summary>
        internal static void EnsureBuilt()
        {
            if (_built && _builtForProSkin == EditorGUIUtility.isProSkin && _pill != null)
            {
                return;
            }

            _builtForProSkin = EditorGUIUtility.isProSkin;
            _built = true;

            if (_builtForProSkin)
            {
                Window = Hex(0x383838);
                Panel = Hex(0x333333);
                Toolbar = Hex(0x3C3C3C);
                Border = Hex(0x232323);
                Selection = Hex(0x2C5D87);
                SelectionEdge = Hex(0x5B9BD5);
                TextDim = Hex(0x9C9C9C);
                RowAlternate = new Color(1f, 1f, 1f, 0.018f);

                Primary = Hex(0x5288BF);
                Danger = Hex(0xD9635F);
                Warning = Hex(0xE0A030);
                Active = Hex(0x6FCF7F);

                PrimaryText = Hex(0xA8CBEA);
                DangerText = Hex(0xE08B87);
                WarningText = Hex(0xF0BE6D);
                ActiveText = Hex(0x8FDD9C);

                PrimaryTint = new Color(0.52f, 0.94f, 1.55f);

                // Darker than the surrounding buttons, not lighter: the destructive one should sit
                // back from the row, not jump out of it.
                DangerTint = new Color(0.98f, 0.60f, 0.58f);
            }
            else
            {
                // The package cannot assume a skin. The semantics stay put; only the surfaces move.
                Window = Hex(0xC2C2C2);
                Panel = Hex(0xCBCBCB);
                Toolbar = Hex(0xCCCCCC);
                Border = Hex(0x999999);
                Selection = Hex(0x3A72B0);
                SelectionEdge = Hex(0x2C5D87);
                TextDim = Hex(0x545454);
                RowAlternate = new Color(0f, 0f, 0f, 0.022f);

                // Accents darken a little so an outline still reads against a light surface.
                Primary = Hex(0x2F6296);
                Danger = Hex(0xB4403B);
                Warning = Hex(0x905E14);
                Active = Hex(0x287739);

                // Text goes darker still - the opposite direction from the dark skin.
                PrimaryText = Hex(0x1F4E7A);
                DangerText = Hex(0x9C2A25);
                WarningText = Hex(0x764B07);
                ActiveText = Hex(0x1C612C);

                PrimaryTint = new Color(0.62f, 0.86f, 1.25f);

                // Same 13% wash on a light button: it warms by taking green and blue down a little,
                // never by adding red.
                DangerTint = new Color(1.00f, 0.93f, 0.93f);
            }

            // Radius 6 in a 14px texture leaves a 2px stretchable middle for the nine-slice. Making
            // the border equal the full half-size collapses that middle and the pill draws squashed.
            _pill = RoundedRect(6, Color.white, Color.white, 1f);

            SaveTitle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };

            SchemaChip = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 0, 0)
            };
            SchemaChip.normal.textColor = TextDim;

            SectionHeader = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 6, 2)
            };
            SectionHeader.normal.textColor = TextDim;

            Badge = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 9,
                padding = new RectOffset(7, 7, 0, 0),
                border = new RectOffset(6, 6, 6, 6),
                normal = { background = _pill }
            };

            RowLabel = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(9, 4, 0, 0),
                alignment = TextAnchor.MiddleLeft
            };

            RowLabelSelected = new GUIStyle(RowLabel) { fontStyle = FontStyle.Bold };
            RowLabelSelected.normal.textColor = Color.white;

            // Buttons keep Unity's own texture and just get tinted. Its corners are already right
            // for the skin, it carries the pressed and hover shading, and a hand-drawn flat rect
            // beside the untinted Export/Import buttons reads as a different widget.
            PrimaryButton = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(14, 14, 2, 2)
            };
            PrimaryButton.normal.textColor = Color.white;
            PrimaryButton.hover.textColor = Color.white;
            PrimaryButton.active.textColor = Color.white;
            PrimaryButton.focused.textColor = Color.white;

            DangerButton = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(10, 10, 2, 2)
            };
            DangerButton.normal.textColor = DangerText;
            DangerButton.hover.textColor = DangerText;
            DangerButton.active.textColor = DangerText;
            DangerButton.focused.textColor = DangerText;

            BannerText = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

            Footnote = new GUIStyle(EditorStyles.miniLabel);
            Footnote.normal.textColor = TextDim;

            // Amber, the same colour the header uses for unapplied changes. One meaning per colour.
            ChangeMark = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };
            ChangeMark.normal.textColor = WarningText;
        }

        /// <summary>Colour and caption for a save's state, or <see cref="BadgeKind.None"/>.</summary>
        internal static BadgeKind BadgeFor(SaveFileState state)
        {
            switch (state)
            {
                case SaveFileState.NotSavedYet: return BadgeKind.Empty;
                case SaveFileState.NeedsMigration: return BadgeKind.Migrate;
                case SaveFileState.FromNewerVersion: return BadgeKind.Newer;
                case SaveFileState.Unreadable: return BadgeKind.Broken;
                default: return BadgeKind.None;
            }
        }

        /// <summary>
        /// Draws a badge. A word carries its meaning without anyone learning a glyph first.
        /// </summary>
        /// <param name="rect">Where to draw, usually the right edge of a list row.</param>
        /// <param name="kind">Which badge. <see cref="BadgeKind.None"/> draws nothing.</param>
        /// <param name="tooltip">Shown on hover, to say why the badge is there.</param>
        /// <param name="onSelection">
        /// Whether the badge sits on a selected row. The selection bar is a strong colour of its
        /// own, so the calibrated text tone - which was measured against the LIST background -
        /// stops being readable on top of it. On a light skin the amber badge all but vanished.
        /// </param>
        internal static void DrawBadge(Rect rect, BadgeKind kind, string tooltip, bool onSelection = false)
        {
            if (kind == BadgeKind.None)
            {
                return;
            }

            string text;
            Color tint;

            switch (kind)
            {
                case BadgeKind.Migrate:
                    text = "MIGRATE";
                    tint = Warning;
                    break;
                case BadgeKind.Newer:
                    text = "NEWER";
                    tint = Danger;
                    break;
                case BadgeKind.Broken:
                    text = "BROKEN";
                    tint = Danger;
                    break;
                default:
                    text = "EMPTY";
                    tint = new Color(0.62f, 0.62f, 0.62f);
                    break;
            }

            var previous = GUI.color;

            // On the selection bar the pill carries the colour instead of the text: a solid tint
            // behind near-white letters reads on any selection colour, on either skin.
            GUI.color = onSelection
                ? new Color(tint.r, tint.g, tint.b, 0.92f)
                : new Color(tint.r, tint.g, tint.b, 0.22f);
            GUI.Label(rect, GUIContent.none, Badge);
            GUI.color = previous;

            var textStyle = new GUIStyle(Badge) { normal = { background = null } };
            textStyle.normal.textColor = onSelection ? new Color(1f, 1f, 1f, 0.96f) : TextFor(kind);

            GUI.Label(rect, new GUIContent(text, tooltip), textStyle);
        }

        /// <summary>The readable text colour for a badge on the current skin.</summary>
        private static Color TextFor(BadgeKind kind)
        {
            switch (kind)
            {
                case BadgeKind.Migrate: return WarningText;
                case BadgeKind.Newer:
                case BadgeKind.Broken: return DangerText;
                default: return TextDim;
            }
        }

        /// <summary>Width a badge needs, so the row can reserve it.</summary>
        internal static float BadgeWidth(BadgeKind kind)
        {
            switch (kind)
            {
                case BadgeKind.None: return 0f;
                case BadgeKind.Migrate: return 56f;
                case BadgeKind.Newer: return 46f;
                case BadgeKind.Broken: return 52f;
                default: return 46f;
            }
        }

        /// <summary>
        /// A message with a coloured edge. Says what happened and what it means for the file, which
        /// is the part a plain HelpBox leaves out.
        /// </summary>
        internal static void DrawBanner(string message, Color accent)
        {
            var content = new GUIContent(message);
            var height = Mathf.Max(30f, BannerText.CalcHeight(content, EditorGUIUtility.currentViewWidth - 60f) + 12f);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height));
            rect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, rect.height - 4f);

            EditorGUI.DrawRect(rect, new Color(accent.r, accent.g, accent.b, 0.09f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);

            var textRect = new Rect(rect.x + 11f, rect.y + 5f, rect.width - 16f, rect.height - 10f);
            var style = new GUIStyle(BannerText);
            style.normal.textColor = TextForAccent(accent);
            GUI.Label(textRect, content, style);
        }

        /// <summary>
        /// A destructive button: the surface stays the surface, and the red arrives as an outline
        /// and the label.
        /// </summary>
        /// <remarks>
        /// Tinting the whole button was the mistake this replaces. On a dark skin it happened to
        /// look right; on a light one the interior turned pink and light-red text vanished into it.
        /// An outline plus a label works on any surface because neither depends on the surface's
        /// brightness.
        /// </remarks>
        internal static bool DangerButtonField(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            bool clicked = false;

            WithBackground(DangerTint, () => clicked = GUILayout.Button(content, style, options));

            if (Event.current.type == EventType.Repaint)
            {
                // 45% like the mockup, not full strength: the line marks the button, it does not
                // fence it off.
                DrawOutline(GUILayoutUtility.GetLastRect(), new Color(Danger.r, Danger.g, Danger.b, 0.45f));
            }

            return clicked;
        }

        /// <summary>Draws a one-pixel frame just inside a rect.</summary>
        internal static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        /// <summary>Runs the body with the button background tinted, then puts it back.</summary>
        internal static void WithBackground(Color color, System.Action body)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = color;
            body();
            GUI.backgroundColor = previous;
        }

        /// <summary>Runs the body with the content colour changed, then puts it back.</summary>
        internal static void WithContentColor(Color color, System.Action body)
        {
            var previous = GUI.contentColor;
            GUI.contentColor = color;
            body();
            GUI.contentColor = previous;
        }

        /// <summary>Fills a rect, for panel depth between the toolbar, the list and the detail.</summary>
        internal static void Fill(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        /// <summary>The readable text colour that goes with an accent on the current skin.</summary>
        private static Color TextForAccent(Color accent)
        {
            if (accent == Warning)
            {
                return WarningText;
            }

            if (accent == Danger)
            {
                return DangerText;
            }

            if (accent == Active)
            {
                return ActiveText;
            }

            return accent == Primary ? PrimaryText : TextDim;
        }

        private static Color Hex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f);
        }

        /// <summary>
        /// Builds a rounded rectangle to nine-slice as a background.
        /// </summary>
        /// <remarks>
        /// IMGUI has no corner radius, so the shape has to be a texture. Drawn white and tinted at
        /// draw time with <c>GUI.color</c>, so one texture serves every badge colour.
        /// </remarks>
        private static Texture2D RoundedRect(int radius, Color fill, Color border, float borderWidth)
        {
            var size = radius * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Distance to the rounded outline: inside the straight bands this measures the
                    // edge, and near a corner it becomes a radius.
                    var cx = x < radius ? radius - 0.5f : (x > size - radius - 1 ? size - radius - 0.5f : x + 0.5f);
                    var cy = y < radius ? radius - 0.5f : (y > size - radius - 1 ? size - radius - 0.5f : y + 0.5f);
                    var dx = x + 0.5f - cx;
                    var dy = y + 0.5f - cy;
                    var distance = Mathf.Sqrt((dx * dx) + (dy * dy));

                    // Coverage instead of a hard cut: a binary threshold leaves the curve stepped,
                    // which at this size just reads as a square corner.
                    var coverage = Mathf.Clamp01(radius - distance + 0.5f);

                    Color color;
                    if (coverage <= 0f)
                    {
                        color = Color.clear;
                    }
                    else
                    {
                        color = distance > radius - borderWidth ? border : fill;
                        color.a *= coverage;
                    }

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
