using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace TheAirline.Desktop
{
    /// <summary>
    ///     Page Up and Page Down scroll the vertical scrollbar under the mouse pointer (or around the control that has the focus),
    ///     in every list and page of the game.
    /// </summary>
    public static class PageScroll
    {
        private static Point _pointer;
        private static bool _hasPointer;

        public static void Attach(Window window)
        {
            window.AddHandler(
                InputElement.PointerMovedEvent,
                (object s, PointerEventArgs e) =>
                {
                    _pointer = e.GetPosition(window);
                    _hasPointer = true;
                },
                RoutingStrategies.Tunnel);

            // bubbling: a text box, combo box or list that uses the key itself has already handled it
            window.AddHandler(
                InputElement.KeyDownEvent,
                (object s, KeyEventArgs e) =>
                {
                    if (e.Handled) return;
                    if (e.Key != Key.PageDown && e.Key != Key.PageUp) return;
                    if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)) != 0) return;

                    bool down = e.Key == Key.PageDown;
                    var viewer = Find(window, down);
                    if (viewer == null) return;

                    double page = viewer.Viewport.Height*0.9;
                    double max = System.Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
                    double y = System.Math.Max(0, System.Math.Min(max, viewer.Offset.Y + (down ? page : -page)));
                    viewer.Offset = new Vector(viewer.Offset.X, y);
                    e.Handled = true;
                },
                RoutingStrategies.Bubble);
        }

        private static ScrollViewer Find(Window window, bool down)
        {
            // first the scrollbar under the pointer, then the one around the focused control
            if (_hasPointer)
            {
                var hit = window.InputHitTest(_pointer) as Visual;
                var viewer = CanScroll(hit, down);
                if (viewer != null) return viewer;
            }

            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Visual;
            return CanScroll(focused, down);
        }

        private static ScrollViewer CanScroll(Visual start, bool down)
        {
            for (var v = start; v != null; v = v.GetVisualParent())
            {
                var sv = v as ScrollViewer;
                if (sv == null) continue;
                double max = sv.Extent.Height - sv.Viewport.Height;
                if (max <= 1) continue;
                if (down ? sv.Offset.Y < max - 0.5 : sv.Offset.Y > 0.5) return sv;
            }

            return null;
        }
    }
}
