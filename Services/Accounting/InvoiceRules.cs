using KarakusAluminyum.Web.Models.Accounting;

namespace KarakusAluminyum.Web.Services.Accounting;

public static class InvoiceRules
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    // KDV satır bazında hesaplanır ve yuvarlanır; toplamlar satırların toplamıdır.
    public static void Calculate(Invoice invoice)
    {
        foreach (var line in invoice.Lines)
        {
            var gross = line.Quantity * line.UnitPrice;
            line.Discount = Money(gross * line.DiscountRate / 100m);
            line.Net = Money(gross) - line.Discount;
            line.Vat = Money(line.Net * line.VatRate / 100m);
            line.Withholding = Money(line.Vat * line.WithholdingRate / 100m);
        }
        invoice.DiscountTotal = invoice.Lines.Sum(x => x.Discount);
        invoice.Net = invoice.Lines.Sum(x => x.Net);
        invoice.VatTotal = invoice.Lines.Sum(x => x.Vat);
        invoice.WithholdingTotal = invoice.Lines.Sum(x => x.Withholding);
        invoice.GrandTotal = invoice.Net + invoice.VatTotal;
        invoice.Payable = invoice.GrandTotal - invoice.WithholdingTotal;
        invoice.PayableTry = Money(invoice.Payable * invoice.ExchangeRate);
    }

    public static List<string> Validate(IReadOnlyList<InvoiceLineInput> lines, decimal exchangeRate, string currency, DateOnly date, DateOnly dueDate)
    {
        var errors = new List<string>();
        if (lines.Count == 0) errors.Add("En az bir fatura satırı girin.");
        if (lines.Count > 500) errors.Add("Bir faturada en fazla 500 satır olabilir.");
        if (!AccountingLabels.Currencies.Contains(currency)) errors.Add("Geçersiz para birimi.");
        else if (currency == "TRY" ? exchangeRate != 1m : exchangeRate is <= 0 or > 100000) errors.Add("Geçerli bir kur girin.");
        if (dueDate < date) errors.Add("Vade tarihi fatura tarihinden önce olamaz.");
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"{i + 1}. satır: ";
            if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 300) errors.Add(prefix + "Açıklama yazın (en fazla 300 karakter).");
            if (line.Quantity is <= 0 or > 100_000_000) errors.Add(prefix + "Miktar sıfırdan büyük olmalıdır.");
            if (line.UnitPrice is < 0 or > 1_000_000_000) errors.Add(prefix + "Birim fiyat geçersiz.");
            if (line.DiscountRate is < 0 or > 100) errors.Add(prefix + "İskonto 0-100 arasında olmalıdır.");
            if (line.VatRate is < 0 or > 100) errors.Add(prefix + "KDV oranı 0-100 arasında olmalıdır.");
            if (line.WithholdingRate is < 0 or > 100) errors.Add(prefix + "Tevkifat oranı 0-100 arasında olmalıdır.");
        }
        return errors;
    }

    public static bool CanTransition(ChequeDirection direction, ChequeStatus current, ChequeStatus next) => (direction, current, next) switch
    {
        (ChequeDirection.Received, ChequeStatus.Portfolio, ChequeStatus.Deposited or ChequeStatus.Collected or ChequeStatus.Endorsed or ChequeStatus.Bounced or ChequeStatus.Cancelled) => true,
        (ChequeDirection.Received, ChequeStatus.Deposited, ChequeStatus.Collected or ChequeStatus.Bounced or ChequeStatus.Portfolio) => true,
        (ChequeDirection.Received, ChequeStatus.Endorsed, ChequeStatus.Bounced) => true,
        (ChequeDirection.Given, ChequeStatus.Portfolio, ChequeStatus.Paid or ChequeStatus.Bounced or ChequeStatus.Cancelled) => true,
        _ => false
    };
}
