using System;
using System.Collections.Generic;
using System.Drawing;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Monitor;
using Xunit;

namespace TaskbarGroups.Monitor.Tests
{
    /// <summary>
    /// Builds monitor arrangements by hand.
    /// </summary>
    /// <remarks>
    /// The placement logic is tested against synthetic monitor sets rather than
    /// against whatever displays the test machine happens to have. That is what
    /// makes the mixed-DPI, portrait, ultrawide and negative-coordinate cases in
    /// the test matrix reachable from a single-monitor CI agent - and it is the
    /// whole reason <see cref="PopupPlacementCalculator"/> was separated from
    /// <see cref="MonitorService"/>, which owns the Win32 enumeration.
    /// </remarks>
    internal static class MonitorSet
    {
        internal static MonitorInfo Monitor(
            string name,
            int x,
            int y,
            int width,
            int height,
            TaskbarPosition taskbar = TaskbarPosition.Bottom,
            double scale = 1.0,
            bool primary = false,
            bool visibleTaskbar = true)
        {
            Rectangle bounds = new Rectangle(x, y, width, height);

            Rectangle work = bounds;
            switch (taskbar)
            {
                case TaskbarPosition.Bottom: work = new Rectangle(x, y, width, height - 48); break;
                case TaskbarPosition.Top: work = new Rectangle(x, y + 48, width, height - 48); break;
                case TaskbarPosition.Left: work = new Rectangle(x + 72, y, width - 72, height); break;
                case TaskbarPosition.Right: work = new Rectangle(x, y, width - 72, height); break;
            }

            return new MonitorInfo
            {
                Id = name.TrimStart('\\', '.').ToUpperInvariant(),
                DeviceName = @"\\.\" + name,
                Bounds = bounds,
                WorkingArea = work,
                DpiX = (int)(96 * scale),
                DpiY = (int)(96 * scale),
                ScaleFactor = scale,
                Primary = primary,
                Orientation = OrientationOf(width, height),
                TaskbarPosition = taskbar,
                TaskbarDetected = visibleTaskbar,
                TaskbarAutoHide = !visibleTaskbar
            };
        }

        /// <summary>
        /// Orientation from a rectangle, mirroring the production rule including the
        /// degenerate case. A 0x0 monitor has no orientation; without this the test
        /// helper would disagree with the code under test.
        /// </summary>
        private static ScreenOrientation OrientationOf(int width, int height)
        {
            if (width == 0 || height == 0) return ScreenOrientation.Unknown;
            if (width > height) return ScreenOrientation.Landscape;
            if (height > width) return ScreenOrientation.Portrait;
            return ScreenOrientation.Unknown;
        }

        internal static MonitorQueryResult Query(params MonitorInfo[] monitors)
        {
            var result = new MonitorQueryResult();
            result.Monitors.AddRange(monitors);

            foreach (MonitorInfo monitor in monitors)
            {
                if (monitor.Primary) result.Primary = monitor;
            }

            if (result.Primary == null && monitors.Length > 0)
            {
                monitors[0].Primary = true;
                result.Primary = monitors[0];
            }

            if (monitors.Length > 0)
            {
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                foreach (MonitorInfo monitor in monitors)
                {
                    left = Math.Min(left, monitor.Bounds.Left);
                    top = Math.Min(top, monitor.Bounds.Top);
                    right = Math.Max(right, monitor.Bounds.Right);
                    bottom = Math.Max(bottom, monitor.Bounds.Bottom);
                }
                result.VirtualBounds = new Rectangle(left, top, right - left, bottom - top);
            }

            return result;
        }

        internal static PopupPlacementRequest Request(
            Point anchor,
            int width = 320,
            int height = 180,
            int margin = 10,
            bool onTaskbar = false)
        {
            return new PopupPlacementRequest
            {
                AnchorPoint = anchor,
                DesiredSize = new Size(width, height),
                Margin = margin,
                AnchorOnTaskbar = onTaskbar
            };
        }
    }

    /// <summary>
    /// Regression tests for the reported P0: <c>frmMain.SetLocation</c> throwing
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    /// <remarks>
    /// The legacy code indexed a taskbar rectangle list with the
    /// <c>Screen.AllScreens</c> loop counter while <c>FindDockedTaskBars</c>
    /// skipped screens that had no docked taskbar. Any two-monitor setup where the
    /// taskbar is on one monitor desynchronised the two lists. Every case below
    /// is a shape that used to produce that crash.
    /// </remarks>
    public class PopupPlacementRegressionTests
    {
        [Fact]
        public void A_popup_opened_from_a_taskbar_without_a_taskbar_on_a_second_monitor_works()
        {
            // Primary has a taskbar; the secondary does not. This is the exact
            // arrangement that made the legacy list index run past its end.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("DISPLAY1", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("DISPLAY2", 1920, 0, 1280, 1024, TaskbarPosition.None, visibleTaskbar: false));

            Point anchor = new Point(2400, 1000);
            bool onTaskbar = PlacementAssert.IsOnTaskbar(monitors, anchor);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: onTaskbar), monitors, anchor);

            Assert.Equal("DISPLAY2", placement.MonitorId);
            PlacementAssert.InsideWorkArea(placement, monitors.ById("DISPLAY2")!.WorkingArea);
        }

        [Fact]
        public void Three_monitors_where_only_one_has_a_taskbar_work()
        {
            // The legacy FindDockedTaskBars returned a list of length one for this
            // arrangement and the loop indexed it up to two.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("DISPLAY1", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("DISPLAY2", -1280, 0, 1280, 1024, TaskbarPosition.None, visibleTaskbar: false),
                MonitorSet.Monitor("DISPLAY3", 1920, 0, 2560, 1440, TaskbarPosition.None, visibleTaskbar: false));

            foreach (Point anchor in new[]
                     {
                         new Point(400, 1000),
                         new Point(-640, 900),
                         new Point(3000, 1400)
                     })
            {
                bool onTaskbar = PlacementAssert.IsOnTaskbar(monitors, anchor);

                PopupPlacement placement = PopupPlacementCalculator.Calculate(
                    MonitorSet.Request(anchor, onTaskbar: onTaskbar), monitors, anchor);

                MonitorInfo? resolved = monitors.ById(placement.MonitorId);
                Assert.NotNull(resolved);
                PlacementAssert.InsideWorkArea(placement, resolved!.WorkingArea);
            }
        }

        [Fact]
        public void A_monitor_index_is_never_used_to_index_a_list()
        {
            // Whatever the arrangement, every placement resolves to a real monitor
            // and lands inside it. This is the invariant the legacy code violated.
            var arrangements = new List<MonitorInfo[]>
            {
                new[] { MonitorSet.Monitor("A", 0, 0, 1920, 1080, primary: true) },
                new[]
                {
                    MonitorSet.Monitor("A", 0, 0, 1920, 1080, primary: true),
                    MonitorSet.Monitor("B", 1920, 0, 1280, 1024, TaskbarPosition.None, visibleTaskbar: false)
                },
                new[]
                {
                    MonitorSet.Monitor("A", 1920, 0, 1920, 1080),
                    MonitorSet.Monitor("B", 0, 0, 1280, 1024, primary: true),
                    MonitorSet.Monitor("C", -1920, 0, 1920, 1080, TaskbarPosition.None, visibleTaskbar: false)
                }
            };

            foreach (MonitorInfo[] arrangement in arrangements)
            {
                MonitorQueryResult monitors = MonitorSet.Query(arrangement);
                Rectangle virtualBounds = monitors.VirtualBounds;

                for (int x = virtualBounds.Left; x < virtualBounds.Right; x += 137)
                {
                    for (int y = virtualBounds.Top; y < virtualBounds.Bottom; y += 113)
                    {
                        Point anchor = new Point(x, y);

                        // This is the line that used to throw.
                        PopupPlacement placement = PopupPlacementCalculator.Calculate(
                            MonitorSet.Request(anchor), monitors, anchor);

                        MonitorInfo? resolved = monitors.ById(placement.MonitorId);
                        Assert.True(resolved != null, "no monitor resolved for " + anchor);
                        PlacementAssert.InsideWorkArea(placement, resolved!.WorkingArea);
                    }
                }
            }
        }

        [Fact]
        public void An_anchor_outside_every_monitor_still_resolves()
        {
            // A point in the gap between two misaligned monitors, or produced by a
            // stale cursor position.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, primary: true),
                MonitorSet.Monitor("B", 2000, 0, 1280, 1024, TaskbarPosition.None, visibleTaskbar: false));

            Point anchor = new Point(1960, 500);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor), monitors, anchor);

            Assert.NotNull(monitors.ById(placement.MonitorId));
        }

        [Fact]
        public void No_monitors_at_all_does_not_throw()
        {
            var empty = new MonitorQueryResult();

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(new Point(0, 0)), empty, new Point(0, 0));

            Assert.Equal(new Point(0, 0), placement.Location);
        }
    }

    /// <summary>Placement matrix: the monitor arrangements users actually have.</summary>
    public class PopupPlacementCalculatorTests
    {
        [Theory]
        [InlineData(TaskbarPosition.Bottom)]
        [InlineData(TaskbarPosition.Top)]
        [InlineData(TaskbarPosition.Left)]
        [InlineData(TaskbarPosition.Right)]
        public void A_taskbar_click_opens_the_popup_inwards_from_that_edge(TaskbarPosition position)
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, position, primary: true));

            // A point on the taskbar strip, wherever that strip is.
            Point anchor = position == TaskbarPosition.Bottom ? new Point(960, 1060)
                : position == TaskbarPosition.Top ? new Point(960, 20)
                : position == TaskbarPosition.Left ? new Point(30, 540)
                : new Point(1900, 540);

            Assert.True(PlacementAssert.IsOnTaskbar(monitors, anchor), "the anchor should be on the taskbar");

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

            PlacementAssert.InsideWorkArea(placement, monitors.Primary!.WorkingArea);

            switch (position)
            {
                case TaskbarPosition.Top:
                    // Just below the top taskbar.
                    Assert.True(placement.Location.Y > 0);
                    break;
                case TaskbarPosition.Left:
                    Assert.True(placement.Location.X > 0);
                    break;
                case TaskbarPosition.Right:
                    Assert.True(placement.Right <= monitors.Primary!.WorkingArea.Right);
                    break;
                default:
                    // Above the bottom taskbar, and horizontally centred on the click.
                    Assert.Equal(anchor.X - (placement.Size.Width / 2), placement.Location.X);
                    break;
            }
        }

        [Fact]
        public void An_auto_hidden_taskbar_is_treated_as_a_full_monitor()
        {
            // The legacy hidden-taskbar branch compared the cursor's virtual Y
            // against PrimaryScreen.Bounds.Height, which is wrong on any secondary
            // monitor and on a monitor placed below the primary.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.AutoHide, visibleTaskbar: false, primary: true),
                MonitorSet.Monitor("B", 0, 1080, 1920, 1080, TaskbarPosition.AutoHide, visibleTaskbar: false));

            Point anchor = new Point(400, 1600); // on the monitor below the primary

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor), monitors, anchor);

            Assert.Equal("B", placement.MonitorId);

            // The auto-hidden monitor reserves nothing, so its whole bounds are usable.
            Assert.True(placement.Location.Y >= monitors.ById("B")!.Bounds.Top);
            Assert.True(placement.Bottom <= monitors.ById("B")!.Bounds.Bottom);
        }

        [Fact]
        public void Negative_virtual_screen_coordinates_are_handled()
        {
            // A secondary monitor to the left of the primary is the arrangement
            // where the legacy arithmetic went negative and clamped wrongly.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("B", -2560, 0, 2560, 1440, TaskbarPosition.Bottom));

            Point anchor = new Point(-1280, 1400);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

            Assert.Equal("B", placement.MonitorId);
            Assert.True(placement.Location.X >= -2560, "x was " + placement.Location.X);
            PlacementAssert.InsideWorkArea(placement, monitors.ById("B")!.WorkingArea);
        }

        [Fact]
        public void A_primary_monitor_on_the_right_is_handled()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1280, 1024, TaskbarPosition.Bottom),
                MonitorSet.Monitor("B", 1280, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true));

            Point anchor = new Point(2000, 1050);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

            Assert.Equal("B", placement.MonitorId);
            PlacementAssert.InsideWorkArea(placement, monitors.ById("B")!.WorkingArea);
        }

        [Theory]
        [InlineData(1.00)]
        [InlineData(1.25)]
        [InlineData(1.50)]
        [InlineData(1.75)]
        [InlineData(2.00)]
        public void The_popup_is_scaled_by_the_monitors_dpi(double scale)
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, (int)(1920 * scale), (int)(1080 * scale),
                    TaskbarPosition.Bottom, scale, primary: true));

            // 100 logical pixels asked for.
            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(new Point(500, 900), 100, 100, margin: 10), monitors, new Point(500, 900));

            int expected = (int)Math.Round(100 * scale);
            Assert.Equal(expected, placement.Size.Width);
            Assert.Equal(expected, placement.Size.Height);
        }

        [Fact]
        public void Mixed_scaling_places_the_popup_using_the_chosen_monitors_scale()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 3840, 2160, TaskbarPosition.Bottom, 2.0, primary: true),
                MonitorSet.Monitor("B", 3840, 0, 1920, 1080, TaskbarPosition.Bottom, 1.0));

            PopupPlacement onSecondary = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(new Point(4800, 1000), 200, 100), monitors, new Point(4800, 1000));

            Assert.Equal("B", onSecondary.MonitorId);
            Assert.Equal(200, onSecondary.Size.Width);

            PopupPlacement onPrimary = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(new Point(1000, 2000), 200, 100), monitors, new Point(1000, 2000));

            Assert.Equal("A", onPrimary.MonitorId);
            Assert.Equal(400, onPrimary.Size.Width);
        }

        [Fact]
        public void An_ultrawide_monitor_is_handled()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 3440, 1440, TaskbarPosition.Bottom, primary: true));

            Point anchor = new Point(3400, 1400);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

            PlacementAssert.InsideWorkArea(placement, monitors.Primary!.WorkingArea);
        }

        [Fact]
        public void A_portrait_monitor_is_handled()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1080, 1920, TaskbarPosition.Bottom, primary: true));

            Point anchor = new Point(540, 1900);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

            Assert.Equal(ScreenOrientation.Portrait, monitors.Primary!.Orientation);
            PlacementAssert.InsideWorkArea(placement, monitors.Primary!.WorkingArea);
        }

        [Fact]
        public void A_popup_larger_than_the_work_area_is_shrunk_to_fit()
        {
            // A lower-resolution monitor with a group of twenty shortcuts: the
            // legacy form sized the popup from its contents and let it run off
            // the screen.
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1280, 720, TaskbarPosition.Bottom, primary: true));

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(new Point(640, 700), 4000, 3000, onTaskbar: true), monitors, new Point(640, 700));

            Assert.True(placement.WasClamped);
            Assert.True(placement.Size.Width <= monitors.Primary!.WorkingArea.Width);
            Assert.True(placement.Size.Height <= monitors.Primary!.WorkingArea.Height);
            PlacementAssert.InsideWorkArea(placement, monitors.Primary!.WorkingArea);
        }

        [Fact]
        public void A_popup_clicks_near_a_screen_edge_stays_on_screen()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true));

            foreach (Point anchor in new[]
                     {
                         new Point(0, 1000),
                         new Point(1919, 1000),
                         new Point(5, 1040),
                         new Point(1900, 1060)
                     })
            {
                PopupPlacement placement = PopupPlacementCalculator.Calculate(
                    MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

                PlacementAssert.InsideWorkArea(placement, monitors.Primary!.WorkingArea);
            }
        }

        [Fact]
        public void A_stacked_vertical_arrangement_keeps_each_popup_on_its_own_monitor()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("B", 0, 1080, 1920, 1080, TaskbarPosition.Bottom),
                MonitorSet.Monitor("C", 0, 2160, 1920, 1080, TaskbarPosition.Bottom));

            foreach (string id in new[] { "A", "B", "C" })
            {
                MonitorInfo monitor = monitors.ById(id)!;
                Point anchor = new Point(monitor.Bounds.Left + 200, monitor.Bounds.Bottom - 20);

                PopupPlacement placement = PopupPlacementCalculator.Calculate(
                    MonitorSet.Request(anchor, onTaskbar: true), monitors, anchor);

                Assert.Equal(id, placement.MonitorId);
                PlacementAssert.InsideWorkArea(placement, monitor.WorkingArea);
            }
        }

        [Fact]
        public void A_named_monitor_preference_overrides_the_cursor()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("B", 1920, 0, 2560, 1440, TaskbarPosition.Bottom));

            PopupPlacementRequest request = MonitorSet.Request(new Point(300, 1000));
            request.MonitorPreference = MonitorPreference.Specific;
            request.PreferredMonitorDeviceName = @"\\.\B";

            PopupPlacement placement = PopupPlacementCalculator.Calculate(request, monitors, new Point(300, 1000));

            Assert.Equal("B", placement.MonitorId);
        }

        [Fact]
        public void A_named_monitor_that_is_not_connected_falls_back_instead_of_failing()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true));

            PopupPlacementRequest request = MonitorSet.Request(new Point(300, 1000));
            request.MonitorPreference = MonitorPreference.Specific;
            request.PreferredMonitorDeviceName = @"\\.\DISPLAY9"; // unplugged

            PopupPlacement placement = PopupPlacementCalculator.Calculate(request, monitors, new Point(300, 1000));

            Assert.Equal("A", placement.MonitorId);
        }

        [Fact]
        public void The_primary_preference_ignores_the_cursor()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("B", 1920, 0, 1280, 1024, TaskbarPosition.Bottom));

            PopupPlacementRequest request = MonitorSet.Request(new Point(2400, 500));
            request.MonitorPreference = MonitorPreference.Primary;

            PopupPlacement placement = PopupPlacementCalculator.Calculate(request, monitors, new Point(2400, 500));

            Assert.Equal("A", placement.MonitorId);
        }

        [Fact]
        public void A_cursor_away_from_the_taskbar_opens_the_popup_above_it()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true));

            Point anchor = new Point(500, 400);

            PopupPlacement placement = PopupPlacementCalculator.Calculate(
                MonitorSet.Request(anchor, 200, 100, margin: 10), monitors, anchor);

            // Above the anchor, horizontally centred on it.
            Assert.True(placement.Location.Y < anchor.Y);
            Assert.Equal(anchor.X - (placement.Size.Width / 2), placement.Location.X);
        }

    }

    /// <summary>Monitor lookup by geometry rather than by index.</summary>
    public class MonitorQueryResultTests
    {
        [Fact]
        public void A_point_resolves_to_the_monitor_containing_it()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, primary: true),
                MonitorSet.Monitor("B", 1920, 0, 1280, 1024));

            Assert.Equal("A", monitors.ByPoint(new Point(100, 100))!.Id);
            Assert.Equal("B", monitors.ByPoint(new Point(2500, 100))!.Id);
        }

        [Fact]
        public void A_point_in_a_gap_resolves_to_the_overlapping_monitor()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, primary: true),
                MonitorSet.Monitor("B", 2000, 0, 1280, 1024));

            // Inside neither: 1920..2000 is empty space.
            MonitorInfo? resolved = monitors.ByPoint(new Point(1960, 500));

            Assert.NotNull(resolved);
            Assert.True(resolved!.Bounds.Contains(new Point(1960, 500)) || resolved.Bounds.X <= 1960);
        }

        [Fact]
        public void A_device_name_lookup_is_case_insensitive()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("DISPLAY1", 0, 0, 1920, 1080, primary: true));

            Assert.NotNull(monitors.ByDeviceName(@"\\.\display1"));
            Assert.Null(monitors.ByDeviceName(@"\\.\DISPLAY7"));
            Assert.Null(monitors.ByDeviceName(""));
            Assert.Null(monitors.ByDeviceName(null));
        }

        [Fact]
        public void The_virtual_bounds_cover_a_negative_origin_layout()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", -1920, 0, 1920, 1080, primary: true),
                MonitorSet.Monitor("B", 0, 0, 1920, 1080));

            Assert.Equal(-1920, monitors.VirtualBounds.Left);
            Assert.Equal(0, monitors.VirtualBounds.Top);
            Assert.Equal(1920, monitors.VirtualBounds.Right);
            Assert.Equal(1080, monitors.VirtualBounds.Bottom);

            // The union spans both monitors: 1920 to the left of the origin plus
            // 1920 to the right of it.
            Assert.Equal(3840, monitors.VirtualBounds.Width);
        }

        [Fact]
        public void A_point_on_a_work_area_resolves_through_it_first()
        {
            MonitorQueryResult monitors = MonitorSet.Query(
                MonitorSet.Monitor("A", 0, 0, 1920, 1080, TaskbarPosition.Bottom, primary: true),
                MonitorSet.Monitor("B", 1920, 0, 1280, 1024, TaskbarPosition.Bottom));

            // On B's taskbar, outside its work area.
            Assert.Equal("B", monitors.ByWorkAreaPoint(new Point(2400, 1010))!.Id);
            // Inside B's work area.
            Assert.Equal("B", monitors.ByWorkAreaPoint(new Point(2400, 500))!.Id);
        }

        [Fact]
        public void The_orientation_is_derived_from_the_rectangle()
        {
            Assert.Equal(ScreenOrientation.Landscape, MonitorSet.Monitor("A", 0, 0, 1920, 1080).Orientation);
            Assert.Equal(ScreenOrientation.Portrait, MonitorSet.Monitor("A", 0, 0, 1080, 1920).Orientation);
            Assert.Equal(ScreenOrientation.Unknown, MonitorSet.Monitor("A", 0, 0, 0, 0).Orientation);
        }

        [Fact]
        public void Monitor_info_describes_itself_usefully()
        {
            string text = MonitorSet.Monitor("DISPLAY1", 0, 0, 2560, 1440, TaskbarPosition.Bottom, 1.5, primary: true).ToString();

            Assert.Contains("DISPLAY1", text);
            Assert.Contains("2560x1440", text);
            Assert.Contains("150%", text);
            Assert.Contains("primary", text);
        }
    }
    /// <summary>Assertions shared by the placement tests.</summary>
    internal static class PlacementAssert
    {
        /// <summary>
        /// True when the point is inside a docked taskbar, using the same geometry
        /// <c>MonitorService</c> uses: on a monitor with a taskbar, and outside its
        /// work area.
        /// </summary>
        internal static bool IsOnTaskbar(MonitorQueryResult monitors, Point point)
        {
            foreach (MonitorInfo monitor in monitors.Monitors)
            {
                if (!monitor.TaskbarDetected) continue;
                if (!monitor.Bounds.Contains(point)) continue;
                return !monitor.WorkingArea.Contains(point);
            }
            return false;
        }

        /// <summary>
        /// The central invariant: a popup is always fully inside its work area.
        /// This is what the legacy placement failed to guarantee.
        /// </summary>
        internal static void InsideWorkArea(PopupPlacement placement, Rectangle workArea)
        {
            Assert.True(placement.Location.X >= workArea.Left,
                "left " + placement.Location.X + " < " + workArea.Left);
            Assert.True(placement.Location.Y >= workArea.Top,
                "top " + placement.Location.Y + " < " + workArea.Top);
            Assert.True(placement.Right <= workArea.Right,
                "right " + placement.Right + " > " + workArea.Right);
            Assert.True(placement.Bottom <= workArea.Bottom,
                "bottom " + placement.Bottom + " > " + workArea.Bottom);
        }
    }
}
