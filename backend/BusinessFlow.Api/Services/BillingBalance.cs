using BusinessFlow.Api.Models;

namespace BusinessFlow.Api.Services;

public static class BillingBalance
{
    public static decimal NetAmount(SalesInvoice invoice) =>
        invoice.Amount - invoice.CreditedAmount;

    public static decimal Outstanding(SalesInvoice invoice) =>
        Math.Max(0, NetAmount(invoice) - invoice.PaidAmount);

    public static decimal RefundDue(SalesInvoice invoice) =>
        Math.Max(0, invoice.PaidAmount - NetAmount(invoice));

    public static string Status(SalesInvoice invoice)
    {
        var net = NetAmount(invoice);
        if (invoice.PaidAmount > net) return "RefundDue";
        if (net == 0) return "Credited";
        if (invoice.PaidAmount == net) return "Paid";
        return invoice.PaidAmount > 0 ? "PartiallyPaid" : "Issued";
    }
}
