using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Data.Repositories;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Windows.WindowManagement;
using TaskbarGroups.Workspaces.WorkspaceService;
using Xunit;

namespace TaskbarGroups.WorkspaceTests
{
    /// <summary>A throwaway data directory per test.</summary>
    internal sealed class TestDirectory : IDisposable
    {
        public TestDirectory(string name)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "taskbargroups-workspace-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Turning anchors into rectangles.</summary>
    public class PlacementCalculatorTests
    {
        private static readonly Rectangle WorkArea = new Rectangle(0, 0, 1920, 1040);

        private static WorkspaceItem Item(PlacementAnchor anchor, int x = 0, int y = 0, int w = 0, int h = 0)
        {
            return new WorkspaceItem
            {
                NormalizedPlacement = anchor,
                X = x,
                Y = y,
                Width = w,
                Height = h
            };
        }

        [Fact]
        public void Left_half_takes_the_left_half_of_the_work_area()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.LeftHalf));

            Assert.Equal(0, r.X);
            Assert.Equal(0, r.Y);
            Assert.Equal(960, r.Width);
            Assert.Equal(1040, r.Height);
        }

        [Fact]
        public void Right_half_takes_the_right_half()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.RightHalf));

            Assert.Equal(960, r.X);
            Assert.Equal(960, r.Width);
        }

        [Fact]
        public void Top_half_takes_the_top_half()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.TopHalf));

            Assert.Equal(0, r.X);
            Assert.Equal(0, r.Y);
            Assert.Equal(1920, r.Width);
            Assert.Equal(520, r.Height);
        }

        [Fact]
        public void Bottom_half_takes_the_bottom_half()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.BottomHalf));

            Assert.Equal(520, r.Y);
            Assert.Equal(520, r.Height);
        }

        [Fact]
        public void Top_left_quarter()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.TopLeft));

            Assert.Equal(0, r.X);
            Assert.Equal(0, r.Y);
            Assert.Equal(960, r.Width);
            Assert.Equal(520, r.Height);
        }

        [Fact]
        public void Bottom_right_quarter()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.BottomRight));

            Assert.Equal(960, r.X);
            Assert.Equal(520, r.Y);
            Assert.Equal(960, r.Width);
            Assert.Equal(520, r.Height);
        }

        [Fact]
        public void Centre_places_the_item_in_the_middle()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.Center, w: 800, h: 600));

            // (1920-800)/2 = 560, (1040-600)/2 = 220
            Assert.Equal(560, r.X);
            Assert.Equal(220, r.Y);
            Assert.Equal(800, r.Width);
            Assert.Equal(600, r.Height);
        }

        [Fact]
        public void Explicit_coordinates_win_over_the_anchor()
        {
            // A saved layout must reproduce exactly, even if the anchor says otherwise.
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.Center, x: 100, y: 50, w: 640, h: 480));

            Assert.Equal(100, r.X);
            Assert.Equal(50, r.Y);
            Assert.Equal(640, r.Width);
            Assert.Equal(480, r.Height);
        }

        [Fact]
        public void An_item_larger_than_the_work_area_is_shrunk_to_fit()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.Center, w: 3000, h: 2000));

            Assert.Equal(1920, r.Width);
            Assert.Equal(1040, r.Height);
        }

        [Fact]
        public void An_item_off_the_right_edge_is_clamped_back()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.Center, x: 1900, y: 0, w: 400, h: 300));

            Assert.Equal(1520, r.X);  // 1920 - 400
            Assert.Equal(0, r.Y);
        }

        [Fact]
        public void An_item_off_the_left_edge_is_clamped_back()
        {
            Rectangle r = WindowPlacementCalculator.Resolve(WorkArea, Item(PlacementAnchor.Center, x: -200, y: 0, w: 400, h: 300));

            Assert.Equal(0, r.X);
        }

        [Fact]
        public void Clamp_keeps_a_rectangle_inside_the_work_area()
        {
            Rectangle workArea = new Rectangle(0, 0, 1920, 1040);
            Rectangle desired = new Rectangle(1800, 900, 400, 300);

            Rectangle clamped = WindowPlacementCalculator.Clamp(workArea, desired);

            Assert.Equal(1520, clamped.X);
            Assert.Equal(740, clamped.Y);
            Assert.Equal(400, clamped.Width);
            Assert.Equal(300, clamped.Height);
        }

        [Fact]
        public void A_portrait_work_area_still_produces_a_visible_rect()
        {
            // A portrait monitor has a work area taller than it is wide.
            Rectangle portrait = new Rectangle(0, 0, 1080, 1920);

            Rectangle r = WindowPlacementCalculator.Resolve(portrait, Item(PlacementAnchor.Center, w: 800, h: 600));

            Assert.True(r.Width <= portrait.Width);
            Assert.True(r.Height <= portrait.Height);
            Assert.True(r.X >= 0);
            Assert.True(r.Y >= 0);
        }

        [Fact]
        public void A_monitor_with_a_negative_origin_is_handled()
        {
            // A secondary monitor to the left of the primary has a negative origin.
            Rectangle negative = new Rectangle(-1920, 0, 1920, 1040);

            Rectangle r = WindowPlacementCalculator.Resolve(negative, Item(PlacementAnchor.LeftHalf));

            Assert.Equal(-1920, r.X);
            Assert.Equal(960, r.Width);
        }
    }

    /// <summary>Workspace persistence.</summary>
    public class WorkspaceRepositoryTests : IDisposable
    {
        private readonly TestDirectory _directory = new TestDirectory("workspaces");
        private readonly Database _database;
        private readonly WorkspaceRepository _workspaces;

        public WorkspaceRepositoryTests()
        {
            _database = new Database(System.IO.Path.Combine(_directory.Path, "TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _workspaces = new WorkspaceRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void A_workspace_round_trips_with_its_items()
        {
            var workspace = new Workspace
            {
                Name = "Development",
                Description = "Editors and terminals",
                LaunchDelayMs = 900
            };

            workspace.Items.Add(new WorkspaceItem
            {
                WorkspaceId = workspace.Id,
                Executable = @"C:\Windows\System32\cmd.exe",
                ProcessMatch = "cmd",
                MonitorDeviceName = @"\\.\DISPLAY1",
                X = 100,
                Y = 100,
                Width = 800,
                Height = 600,
                WindowState = WindowStatePreference.Maximized,
                NormalizedPlacement = PlacementAnchor.LeftHalf,
                LaunchDelayMs = 750,
                SortOrder = 0
            });

            _workspaces.Upsert(workspace);

            Workspace? loaded = _workspaces.GetById(workspace.Id);

            Assert.NotNull(loaded);
            Assert.Equal("Development", loaded!.Name);
            Assert.Equal("Editors and terminals", loaded.Description);
            Assert.Equal(900, loaded.LaunchDelayMs);
            Assert.Single(loaded.Items);
            Assert.Equal(PlacementAnchor.LeftHalf, loaded.Items[0].NormalizedPlacement);
            Assert.Equal(WindowStatePreference.Maximized, loaded.Items[0].WindowState);
            Assert.Equal(800, loaded.Items[0].Width);
        }

        [Fact]
        public void Two_workspaces_cannot_share_a_name()
        {
            _workspaces.Upsert(new Workspace { Name = "Dev" });

            Assert.ThrowsAny<Exception>(() => _workspaces.Upsert(new Workspace { Name = "dev" }));
        }

        [Fact]
        public void Deleting_a_workspace_removes_its_items()
        {
            var workspace = new Workspace { Name = "Doomed" };
            workspace.Items.Add(new WorkspaceItem { WorkspaceId = workspace.Id, Executable = "x.exe" });
            _workspaces.Upsert(workspace);

            _workspaces.Delete(workspace.Id);

            Assert.Empty(_workspaces.GetAll());
        }

        [Fact]
        public void An_empty_name_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => _workspaces.Upsert(new Workspace { Name = "  " }));
        }

        [Fact]
        public void Workspaces_come_back_in_name_order()
        {
            _workspaces.Upsert(new Workspace { Name = "Zulu" });
            _workspaces.Upsert(new Workspace { Name = "alpha" });
            _workspaces.Upsert(new Workspace { Name = "Mike" });

            List<string> names = _workspaces.GetAll().Select(w => w.Name).ToList();

            Assert.Equal(new[] { "alpha", "Mike", "Zulu" }, names);
        }

        [Fact]
        public void Placements_are_stored_and_loaded()
        {
            var workspace = new Workspace { Name = "Dev" };
            _workspaces.Upsert(workspace);

            _workspaces.ReplacePlacements(workspace.Id, new[]
            {
                new WindowPlacement
                {
                    WorkspaceId = workspace.Id,
                    Executable = "notepad.exe",
                    ProcessMatch = "Notepad",
                    MonitorId = @"\\.\DISPLAY1",
                    X = 50,
                    Y = 60,
                    Width = 1024,
                    Height = 768,
                    WindowState = WindowStatePreference.Normal,
                    CapturedAt = DateTimeOffset.UtcNow
                }
            });

            List<WindowPlacement> placements = _workspaces.GetPlacements(workspace.Id);

            Assert.Single(placements);
            Assert.Equal("notepad.exe", placements[0].Executable);
            Assert.Equal(1024, placements[0].Width);
        }
    }

    /// <summary>Placing windows through the service.</summary>
    public class BuildPlacementTests
    {
        [Fact]
        public void An_item_with_explicit_coordinates_passes_them_through()
        {
            var monitor = new MonitorInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                WorkingArea = new Rectangle(0, 0, 2560, 1440),
                ScaleFactor = 1.5
            };

            var item = new WorkspaceItem
            {
                Executable = "app.exe",
                ProcessMatch = "app",
                X = 200,
                Y = 150,
                Width = 800,
                Height = 600,
                WindowState = WindowStatePreference.Normal
            };

            WindowPlacement placement = WorkspaceService.BuildPlacement(item, monitor);

            Assert.Equal(200, placement.X);
            Assert.Equal(150, placement.Y);
            Assert.Equal(800, placement.Width);
            Assert.Equal(600, placement.Height);
            Assert.Equal(@"\\.\DISPLAY1", placement.MonitorId);
        }

        [Fact]
        public void An_item_without_coordinates_is_resolved_from_its_anchor()
        {
            var monitor = new MonitorInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                WorkingArea = new Rectangle(0, 0, 1920, 1040),
                ScaleFactor = 1.0
            };

            var item = new WorkspaceItem
            {
                Executable = "app.exe",
                ProcessMatch = "app",
                NormalizedPlacement = PlacementAnchor.LeftHalf
            };

            WindowPlacement placement = WorkspaceService.BuildPlacement(item, monitor);

            Assert.Equal(0, placement.X);
            Assert.Equal(960, placement.Width);
        }

        [Fact]
        public void A_null_monitor_falls_back_to_a_default_work_area()
        {
            var item = new WorkspaceItem
            {
                Executable = "app.exe",
                ProcessMatch = "app",
                NormalizedPlacement = PlacementAnchor.Center,
                Width = 640,
                Height = 480
            };

            WindowPlacement placement = WorkspaceService.BuildPlacement(item, null);

            Assert.True(placement.Width <= 1920);
            Assert.True(placement.Height <= 1040);
        }
    }
}