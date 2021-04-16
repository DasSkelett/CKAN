using System;
using System.Collections.Generic;

namespace CKAN.ConsoleUI.Toolkit
{

    /// <summary>
    /// Object displaying a long screen in a big box
    /// </summary>
    public class ConsoleTextBox : ScreenObject {

        /// <summary>
        /// Initialize the text box
        /// </summary>
        /// <param name="l">X coordinate of left edge</param>
        /// <param name="t">Y coordinate of top edge</param>
        /// <param name="r">X coordinate of right edge</param>
        /// <param name="b">Y coordinate of bottom edge</param>
        /// <param name="autoScroll">If true, keep the bottommost row visible, else keep the topmost row visible</param>
        /// <param name="ta">Alignment of the contents</param>
        /// <param name="bgFunc">Function returning the background color for the text</param>
        /// <param name="fgFunc">Function returning the foreground color for the text</param>
        public ConsoleTextBox(
                int l, int t, int r, int b,
                bool autoScroll = true,
                TextAlign ta = TextAlign.Left,
                Func<ConsoleTheme, ConsoleColor> bgFunc = null,
                Func<ConsoleTheme, ConsoleColor> fgFunc = null)
            : base(l, t, r, b)
        {
            scrollToBottom = autoScroll;
            align          = ta;
            getFgColor     = fgFunc;
            getBgColor     = bgFunc;
        }

        /// <summary>
        /// Add a line to the text box
        /// </summary>
        /// <param name="line">String to add</param>
        public void AddLine(string line)
        {
            // AddRange isn't thread-safe, it temporarily pads with nulls
            foreach (string subLine in Formatting.WordWrap(line, GetRight() - GetLeft() + 1)) {
                lines.Add(subLine);
            }
            if (scrollToBottom) {
                ScrollToBottom();
            } else {
                // No auto-scrolling
            }
        }

        /// <summary>
        /// Scroll the text box to the top
        /// </summary>
        public void ScrollToTop()
        {
            topLine = 0;
        }

        /// <summary>
        /// Scroll the text box to the bottom
        /// </summary>
        public void ScrollToBottom()
        {
            int h   = GetBottom() - GetTop() + 1;
            topLine = lines.Count - h;
        }

        /// <summary>
        /// Scroll the text box up one page
        /// </summary>
        public void ScrollUp(int? howFar = null)
        {
            topLine -= howFar ?? (GetBottom() - GetTop() + 1);
            if (topLine < 0) {
                topLine = 0;
            }
        }

        /// <summary>
        /// Scroll the text box down one page
        /// </summary>
        public void ScrollDown(int? howFar = null)
        {
            int h    = GetBottom() - GetTop() + 1;
            int diff = howFar ?? h;
            if (topLine +  diff <= lines.Count - h) {
                topLine += diff;
            } else {
                ScrollToBottom();
            }
        }

        /// <summary>
        /// Draw the text box
        /// </summary>
        /// <param name="theme">The visual theme to use to draw the dialog</param>
        /// <param name="focused">Framework parameter not relevant to this object</param>
        public override void Draw(ConsoleTheme theme, bool focused)
        {
            int l     = GetLeft();
            int h     = GetBottom() - GetTop() + 1;
            int index = lines.Count < h ? 0 : topLine;
            // Chop one col off the right if we need a scrollbar
            int w     = GetRight() - l + 1 + (lines.Count > h ? -1 : 0);

            if (getBgColor != null) {
                Console.BackgroundColor = getBgColor(theme);
            } else {
                Console.BackgroundColor = theme.TextBoxBg;
            }
            if (getFgColor != null) {
                Console.ForegroundColor = getFgColor(theme);
            } else {
                Console.ForegroundColor = theme.TextBoxFg;
            }
            for (int y = GetTop(); y <= GetBottom(); ++y, ++index) {
                Console.SetCursorPosition(l, y);
                if (index < lines.Count) {
                    switch (align) {
                        case TextAlign.Left:
                            Console.Write(lines[index].PadRight(w));
                            break;
                        case TextAlign.Center:
                            Console.Write(ScreenObject.PadCenter(lines[index], w));
                            break;
                        case TextAlign.Right:
                            Console.Write(lines[index].PadLeft(w));
                            break;
                    }
                } else {
                    Console.Write("".PadRight(w));
                }
            }

            // Scrollbar
            if (lines.Count > h) {
                DrawScrollbar(
                    theme,
                    GetRight(), GetTop(), GetBottom(),
                    GetTop() + 1 + (h - 3) * topLine / (lines.Count - h)
                );
            }
        }

        /// <summary>
        /// Set up key bindings to scroll the box in a given screen container
        /// </summary>
        /// <param name="cont">Container within which to create the bindings</param>
        /// <param name="drawMore">If true, force a redraw of the text box after scrolling, otherwise rely on the main event loop to do it</param>
        public void AddScrollBindings(ScreenContainer cont, bool drawMore = false)
        {
            if (drawMore) {
                cont.AddBinding(Keys.Home,      (object sender, ConsoleTheme theme) => {
                    ScrollToTop();
                    Draw(theme, false);
                    return true;
                });
                cont.AddBinding(Keys.End,       (object sender, ConsoleTheme theme) => {
                    ScrollToBottom();
                    Draw(theme, false);
                    return true;
                });
                cont.AddBinding(Keys.PageUp,    (object sender, ConsoleTheme theme) => {
                    ScrollUp();
                    Draw(theme, false);
                    return true;
                });
                cont.AddBinding(Keys.PageDown,  (object sender, ConsoleTheme theme) => {
                    ScrollDown();
                    Draw(theme, false);
                    return true;
                });
                cont.AddBinding(Keys.UpArrow,   (object sender, ConsoleTheme theme) => {
                    ScrollUp(1);
                    Draw(theme, false);
                    return true;
                });
                cont.AddBinding(Keys.DownArrow, (object sender, ConsoleTheme theme) => {
                    ScrollDown(1);
                    Draw(theme, false);
                    return true;
                });
            } else {
                cont.AddBinding(Keys.Home,      (object sender, ConsoleTheme theme) => {
                    ScrollToTop();
                    return true;
                });
                cont.AddBinding(Keys.End,       (object sender, ConsoleTheme theme) => {
                    ScrollToBottom();
                    return true;
                });
                cont.AddBinding(Keys.PageUp,    (object sender, ConsoleTheme theme) => {
                    ScrollUp();
                    return true;
                });
                cont.AddBinding(Keys.PageDown,  (object sender, ConsoleTheme theme) => {
                    ScrollDown();
                    return true;
                });
                cont.AddBinding(Keys.UpArrow,   (object sender, ConsoleTheme theme) => {
                    ScrollUp(1);
                    return true;
                });
                cont.AddBinding(Keys.DownArrow, (object sender, ConsoleTheme theme) => {
                    ScrollDown(1);
                    return true;
                });
            }
        }

        /// <summary>
        /// Tell the container we can't receive focus
        /// </summary>
        public override bool Focusable() { return false; }

        private bool         scrollToBottom;
        private int          topLine;
        private TextAlign    align;
        private SynchronizedCollection<string> lines = new SynchronizedCollection<string>();
        private Func<ConsoleTheme, ConsoleColor> getBgColor;
        private Func<ConsoleTheme, ConsoleColor> getFgColor;
    }

    /// <summary>
    /// Alignment of text box
    /// </summary>
    public enum TextAlign {
        /// <summary>
        /// Left aligned, padding on right
        /// </summary>
        Left,
        /// <summary>
        /// Centered, padding on both left and right
        /// </summary>
        Center,
        /// <summary>
        /// Right aligned, padding on left
        /// </summary>
        Right
    }

}
