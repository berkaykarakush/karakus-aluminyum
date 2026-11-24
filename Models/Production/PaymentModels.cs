using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KarakusAluminyum.Web.Models.Production;

// README 7.19: siparişin ödeme planı (kapora, ara ödeme, teslimde kalan). Tahsilatlar muhasebede siparişe bağlanarak girilir;
// ödenen tutar taksitlere vade sırasıyla dağıtılır.
public sealed class OrderInstallment
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public int SortOrder { get; set; }
    [Required, StringLength(60)] public string Label { get; set; } = "Ödeme";
    public DateOnly? DueOn { get; set; } // boş: tarihi belli değil (ör. teslimde)
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
    [StringLength(200)] public string? Note { get; set; }
}

public sealed class InstallmentInput
{
    [StringLength(60)] public string? Label { get; set; }
    public DateOnly? DueOn { get; set; }
    public decimal Amount { get; set; }
    [StringLength(200)] public string? Note { get; set; }
}

public enum InstallmentState { Paid, Partial, Waiting, Late }

public sealed record InstallmentStatus(OrderInstallment Row, decimal Paid, decimal Remaining, InstallmentState State);

// Planlanan, tahsil edilen ve kalan; plandan fazla tahsilat "fazla ödeme" olarak görünür.
public sealed record PaymentSummary(decimal Planned, decimal Collected, decimal Remaining, decimal Overpaid, List<InstallmentStatus> Rows)
{
    public static readonly PaymentSummary Empty = new(0, 0, 0, 0, new());
    public bool HasPlan => Rows.Count > 0;
    public int LateCount => Rows.Count(r => r.State == InstallmentState.Late);
}

public static class InstallmentLabels
{
    public static readonly string[] Suggestions = { "Kapora", "Ara ödeme", "Teslimde kalan", "Montaj sonrası", "Taksit" };

    public static string Label(InstallmentState state) => state switch
    {
        InstallmentState.Paid => "Ödendi",
        InstallmentState.Partial => "Kısmen ödendi",
        InstallmentState.Late => "Gecikti",
        _ => "Bekliyor"
    };
}
