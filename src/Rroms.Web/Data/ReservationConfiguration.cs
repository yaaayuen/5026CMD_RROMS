using Microsoft.EntityFrameworkCore;

using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>
/// Database rules (relationships, indexes, check constraints) for the WL-2 reservation tables.
/// </summary>
/// <remarks>
/// Kept in its own file so <see cref="ApplicationDbContext"/>, which all five members edit,
/// needs only one line for this module. The unique index on ReservationSlots is the core of
/// double-booking prevention: it is enforced by SQL Server itself, so it still holds when two
/// requests arrive at the same moment and both pass the service's own availability check.
/// </remarks>
public static class ReservationConfiguration
{
    /// <summary>Name of the unique index that blocks double booking. The service recognises it in errors.</summary>
    public const string SlotUniqueIndexName = "UX_ReservationSlots_Table_SlotStart";

    /// <summary>
    /// Applies every reservation rule. Called once from ApplicationDbContext.OnModelCreating.
    /// </summary>
    /// <param name="builder">The model builder passed to OnModelCreating.</param>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Reservation>(reservation =>
        {
            // Restrict: a customer or table with bookings cannot be deleted by accident.
            // Customers are deactivated and tables made inactive instead, so history is kept.
            reservation.HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            reservation.HasOne(r => r.DiningTable)
                .WithMany()
                .HasForeignKey(r => r.DiningTableId)
                .OnDelete(DeleteBehavior.Restrict);

            // "My reservations" lists one customer's bookings by date.
            reservation.HasIndex(r => new { r.CustomerId, r.StartUtc });

            // Staff daily view and analytics look up bookings by date and status.
            reservation.HasIndex(r => new { r.StartUtc, r.Status });

            // A visit must end after it starts.
            reservation.ToTable(table => table.HasCheckConstraint(
                "CK_Reservations_EndAfterStart",
                "[EndUtc] > [StartUtc]"));
        });

        builder.Entity<ReservationSlot>(slot =>
        {
            // Cascade: deleting a reservation's slots is how a cancelled table is released,
            // and slots have no meaning without their reservation.
            slot.HasOne(s => s.Reservation)
                .WithMany(r => r.Slots)
                .HasForeignKey(s => s.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            slot.HasOne<DiningTable>()
                .WithMany()
                .HasForeignKey(s => s.DiningTableId)
                .OnDelete(DeleteBehavior.Restrict);

            // THE double-booking rule: one table cannot be held twice for the same time slot.
            slot.HasIndex(s => new { s.DiningTableId, s.SlotStartUtc })
                .IsUnique()
                .HasDatabaseName(SlotUniqueIndexName);
        });
    }
}