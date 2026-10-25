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
        // Deleting a dish keeps the announcement; it simply loses its menu link.
        builder.Entity<Announcement>().HasOne(a => a.MenuItem).WithMany()
            .HasForeignKey(a => a.MenuItemId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Feedback>().HasIndex(f => f.ReservationId).IsUnique();
        builder.Entity<Feedback>().HasOne(f => f.Reservation).WithMany()
            .HasForeignKey(f => f.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Feedback>().ToTable("Feedback", table =>
            table.HasCheckConstraint("CK_Feedback_Rating", "[Rating] BETWEEN 1 AND 5"));
        builder.Entity<BlockedWord>().HasIndex(w => w.Word).IsUnique();
        // One countdown per order; it goes when the order is deleted.
        builder.Entity<KitchenTicket>().HasIndex(t => t.OrderId).IsUnique();
        builder.Entity<KitchenTicket>().HasOne(t => t.Order).WithMany()
            .HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
        // The pause switch is one fixed row, so it never needs to be created at run time.
        builder.Entity<KitchenStatus>().Property(k => k.Id).ValueGeneratedNever();
        builder.Entity<KitchenStatus>().HasData(new KitchenStatus { Id = KitchenStatus.SingletonId, IsPaused = false,
            ChangedAtUtc = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) });
        builder.Entity<ChatQuestionLog>().HasIndex(l => l.AskedAtUtc);
    }
}
