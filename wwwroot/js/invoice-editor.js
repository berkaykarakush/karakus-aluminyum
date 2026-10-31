// Fatura satırları ve canlı toplamlar. Kesin hesap sunucudadır; kural Services/Accounting/InvoiceRules ile aynıdır.
(() => {
  const form = document.getElementById("inv-form");
  if (!form) return;
  const products = JSON.parse(document.getElementById("inv-products").textContent);
  const body = document.getElementById("inv-rows");
  const rows = JSON.parse(form.elements.LinesJson.value || "[]");
  const money = (v) => Number(v).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const round = (v) => Math.round((v + Number.EPSILON) * 100) / 100;
  const WITHHOLDING = [[0, "Yok"], [20, "2/10"], [30, "3/10"], [40, "4/10"], [50, "5/10"], [70, "7/10"], [90, "9/10"], [100, "10/10"]];
  const VAT = [0, 1, 10, 20];
  const newRow = () => ({ productId: null, description: "", quantity: 1, unit: "Adet", unitPrice: 0, discountRate: 0, vatRate: 20, withholdingRate: 0 });

  function calc(r) {
    const gross = r.quantity * r.unitPrice;
    const discount = round(gross * r.discountRate / 100);
    const net = round(gross) - discount;
    const vat = round(net * r.vatRate / 100);
    return { discount, net, vat, withholding: round(vat * r.withholdingRate / 100) };
  }

  function totals() {
    const sum = rows.map(calc).reduce((a, c) => ({ discount: a.discount + c.discount, net: a.net + c.net, vat: a.vat + c.vat, withholding: a.withholding + c.withholding }), { discount: 0, net: 0, vat: 0, withholding: 0 });
    const currency = form.elements.Currency.value;
    const lines = [["İskonto", sum.discount], ["Matrah", sum.net], ["KDV", sum.vat]];
    if (sum.withholding) lines.push(["Tevkifat", -sum.withholding]);
    lines.push(["Ödenecek", sum.net + sum.vat - sum.withholding]);
    const box = document.getElementById("inv-totals");
    box.textContent = "";
    lines.forEach(([label, value]) => {
      box.append(Object.assign(document.createElement("dt"), { textContent: label }), Object.assign(document.createElement("dd"), { textContent: `${money(value)} ${currency}` }));
    });
  }

  function render() {
    body.textContent = "";
    rows.forEach((row, index) => {
      const tr = body.insertRow();
      const netCell = () => tr.querySelector(".inv-net");
      const refresh = () => { netCell().textContent = money(calc(row).net); totals(); };
      const add = (el, label) => { el.setAttribute("aria-label", `${index + 1}. satır ${label}`); tr.insertCell().appendChild(el); return el; };
      const select = (options, value, label, onChange) => {
        const el = document.createElement("select");
        options.forEach(([v, t]) => el.add(new Option(t, v, false, String(v) === String(value))));
        el.addEventListener("change", () => { onChange(el.value); refresh(); });
        return add(el, label);
      };
      const input = (key, type, label) => {
        const el = document.createElement("input");
        el.type = type; el.value = row[key] ?? "";
        if (type === "number") { el.min = "0"; el.step = "any"; }
        el.addEventListener("input", () => { row[key] = type === "number" ? Number(el.value) : el.value; refresh(); });
        return add(el, label);
      };
      select([["", "Serbest satır"], ...products.map((p) => [p.id, p.name])], row.productId ?? "", "ürün", (v) => {
        const product = products.find((p) => p.id === Number(v));
        row.productId = product ? product.id : null;
        if (product) Object.assign(row, { description: product.name, unit: product.unit, unitPrice: product.price, vatRate: product.vat });
        render();
      });
      input("description", "text", "açıklama");
      input("quantity", "number", "miktar");
      input("unit", "text", "birim");
      input("unitPrice", "number", "birim fiyat");
      input("discountRate", "number", "iskonto");
      select(VAT.map((v) => [v, `%${v}`]), row.vatRate, "KDV", (v) => { row.vatRate = Number(v); });
      select(WITHHOLDING, row.withholdingRate, "tevkifat", (v) => { row.withholdingRate = Number(v); });
      const net = tr.insertCell(); net.className = "inv-net";
      const remove = document.createElement("button");
      remove.type = "button"; remove.className = "doc-link"; remove.textContent = "Sil";
      remove.addEventListener("click", () => { rows.splice(index, 1); render(); });
      tr.insertCell().appendChild(remove);
      refresh();
    });
    totals();
  }

  function rateVisibility() {
    const isTry = form.elements.Currency.value === "TRY";
    document.getElementById("inv-rate-row").hidden = isTry;
    if (isTry) form.elements.ExchangeRate.value = 1;
    totals();
  }

  document.getElementById("inv-add").addEventListener("click", () => { rows.push(newRow()); render(); });
  form.elements.Currency.addEventListener("change", rateVisibility);
  form.addEventListener("submit", () => { form.elements.LinesJson.value = JSON.stringify(rows); });
  if (!rows.length) rows.push(newRow());
  render();
  rateVisibility();
})();
