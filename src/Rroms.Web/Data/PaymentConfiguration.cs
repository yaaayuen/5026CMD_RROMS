using Microsoft.EntityFrameworkCore;
using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>Relationships and database safeguards for Darren's payment records.</summary>
public static class PaymentConfiguration
{
    /// <summary>The unique index used to recognise a simultaneous duplicate payment.</summary>
    public const string PaidOrderIndex = "UX_Payments_OnePaidPerOrder";
    /// <summary>Enforces one Paid record per order while allowing future unpaid or voided history.</summary>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Payment>(payment =>
        {
            payment.Property(p => p.RefundRevision).IsConcurrencyToken();
            payment.Property(p => p.Status).IsConcurrencyToken();
            payment.Property(p => p.CashReceived).HasPrecision(18, 2);
            payment.HasIndex(p => p.ReceiptBatchId);
            payment.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            payment.HasOne(p => p.Staff).WithMany().HasForeignKey(p => p.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            // A pre-save check improves the message, but only this index closes the race between requests.
            payment.HasIndex(p => p.OrderId).IsUnique()
                .HasDatabaseName(PaidOrderIndex).HasFilter("[Status] = 'Paid'");
            payment.HasIndex(p => new { p.Status, p.PaidAtUtc });
            payment.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Payments_AmountNotNegative", "[Amount] >= 0");
                table.HasCheckConstraint("CK_Payments_Status", "[Status] IN ('Unpaid', 'Paid', 'Voided')");
                table.HasCheckConstraint("CK_Payments_Method", "[Method] IN ('Cash', 'CardTerminal', 'EWallet', 'SimulatedOnline')");
            });
        });
    }
}
