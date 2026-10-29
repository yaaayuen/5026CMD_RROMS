using Microsoft.EntityFrameworkCore;
using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>Floor plan tables and seed layout (Alex, Part 20). Uses its own tables, so WL's DiningTable and DiningArea do not change.</summary>
public static class FloorPlanConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<TablePlacement>(placement =>
        {
            placement.HasKey(p => p.DiningTableId);
            placement.HasOne(p => p.DiningTable).WithOne().HasForeignKey<TablePlacement>(p => p.DiningTableId).OnDelete(DeleteBehavior.Cascade);
            placement.ToTable("TablePlacements", t =>
            {
                t.HasCheckConstraint("CK_TablePlacements_Position", "[X] >= 0 AND [Y] >= 0");
                t.HasCheckConstraint("CK_TablePlacements_Rotation", "[Rotation] IN (0, 90)");
            });
            // The seeded tables T01-T06 and P01-P02 (WL-1), laid out with one clear box between tables.
            placement.HasData(
                new TablePlacement { DiningTableId = 1, X = 3, Y = 11 },
                new TablePlacement { DiningTableId = 2, X = 7, Y = 11 },
                new TablePlacement { DiningTableId = 3, X = 4, Y = 2 },
                new TablePlacement { DiningTableId = 4, X = 9, Y = 2 },
                new TablePlacement { DiningTableId = 5, X = 12, Y = 7 },
                new TablePlacement { DiningTableId = 6, X = 18, Y = 11 },
                new TablePlacement { DiningTableId = 7, X = 3, Y = 3 },
                new TablePlacement { DiningTableId = 8, X = 8, Y = 3 });
        });

        builder.Entity<FloorArea>(area =>
        {
            area.HasKey(a => a.DiningAreaId);
            area.HasOne(a => a.DiningArea).WithOne().HasForeignKey<FloorArea>(a => a.DiningAreaId).OnDelete(DeleteBehavior.Cascade);
            area.ToTable("FloorAreas", t => t.HasCheckConstraint("CK_FloorAreas_Size", "[Width] BETWEEN 8 AND 60 AND [Height] BETWEEN 8 AND 60"));
            area.HasData(
                new FloorArea { DiningAreaId = 1, Width = 24, Height = 15 },
                new FloorArea { DiningAreaId = 2, Width = 16, Height = 10 });
        });

        builder.Entity<FloorFeature>(feature =>
        {
            feature.Property(f => f.Kind).HasMaxLength(20);
            feature.Property(f => f.Label).HasMaxLength(30);
            feature.HasOne(f => f.DiningArea).WithMany().HasForeignKey(f => f.DiningAreaId).OnDelete(DeleteBehavior.Cascade);
            feature.ToTable("FloorFeatures", t => t.HasCheckConstraint("CK_FloorFeatures_Size", "[X] >= 0 AND [Y] >= 0 AND [Width] >= 0 AND [Height] >= 0"));
            feature.HasData(
                new FloorFeature { Id = 1, DiningAreaId = 1, Kind = FloorFeatureKinds.Block, Label = "Bar", X = 0, Y = 0, Width = 3, Height = 8 },
                new FloorFeature { Id = 2, DiningAreaId = 1, Kind = FloorFeatureKinds.Block, Label = "Kitchen", X = 18, Y = 0, Width = 6, Height = 5 },
                new FloorFeature { Id = 3, DiningAreaId = 1, Kind = FloorFeatureKinds.Window, Label = "Windows", X = 3, Y = 0, Width = 14, Height = 0 },
                new FloorFeature { Id = 4, DiningAreaId = 1, Kind = FloorFeatureKinds.Door, Label = "Entrance", X = 10, Y = 15, Width = 4, Height = 0 },
                new FloorFeature { Id = 5, DiningAreaId = 1, Kind = FloorFeatureKinds.Door, Label = "To patio", X = 24, Y = 9, Width = 0, Height = 3 },
                new FloorFeature { Id = 6, DiningAreaId = 2, Kind = FloorFeatureKinds.Block, Label = "Planters", X = 0, Y = 0, Width = 16, Height = 1 },
                new FloorFeature { Id = 7, DiningAreaId = 2, Kind = FloorFeatureKinds.Door, Label = "From hall", X = 0, Y = 4, Width = 0, Height = 3 });
        });
    }
}
