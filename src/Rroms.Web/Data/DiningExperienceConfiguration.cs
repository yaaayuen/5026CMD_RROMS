using Microsoft.EntityFrameworkCore;
using Rroms.Web.Models;
namespace Rroms.Web.Data;

/// <summary>Alex's database constraints. Does not change teammates' entities.</summary>
public static class DiningExperienceConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        // A stale cancellation must not overwrite another staff member seating the customer.
        builder.Entity<Reservation>().Property(r => r.Status).IsConcurrencyToken();
        builder.Entity<Order>().Property(o => o.Status).IsConcurrencyToken();
        builder.Entity<Announcement>().ToTable("Announcements", table =>
            table.HasCheckConstraint("CK_Announcements_Dates", "[EndUtc] > [StartUtc]"));
        builder.Entity<Feedback>().HasIndex(f => f.ReservationId).IsUnique();
        builder.Entity<Feedback>().HasOne(f => f.Reservation).WithMany()
            .HasForeignKey(f => f.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Feedback>().ToTable("Feedback", table =>
            table.HasCheckConstraint("CK_Feedback_Rating", "[Rating] BETWEEN 1 AND 5"));
    }
}
