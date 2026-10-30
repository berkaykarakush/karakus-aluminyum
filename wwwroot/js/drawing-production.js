// Üretim detayları açıkken editörde canlı kesim ve maliyet özeti. Hesap her zaman sunucuda yapılır.
(() => {
  const form = document.getElementById("wd-form") || document.getElementById("pd-form");
  const box = document.getElementById("wd-result");
  if (!form || !box) return;
  const field = (name) => form.elements[name];
  const alerts = document.getElementById("wd-alerts"), summary = document.getElementById("wd-summary");
  const money = (value) => `${Number(value).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₺`;
  let requestNumber = 0, timer = 0;

  function table(title, headers, rows) {
    const heading = document.createElement("h3");
    heading.textContent = title;
    const element = document.createElement("table");
    element.className = "admin-table wd-table";
    element.createTHead().insertRow().append(...headers.map((text) => Object.assign(document.createElement("th"), { textContent: text })));
    const body = element.createTBody();
    rows.forEach((row) => { const tr = body.insertRow(); row.forEach((value) => { tr.insertCell().textContent = value; }); });
    box.append(heading, element);
  }

  function notes(items, className) {
    if (!items.length) return;
    const list = document.createElement("ul");
    list.className = className;
    items.forEach((text) => { list.appendChild(document.createElement("li")).textContent = text; });
    alerts.appendChild(list);
  }

  function show(data) {
    alerts.textContent = "";
    box.textContent = "";
    notes(data.errors || [], "wd-errors");
    notes(data.warnings || [], "wd-warnings");
    if ((data.errors || []).length) { summary.textContent = "Çizimde hata var"; return; }
    summary.textContent = `Birim ${money(data.unitPrice)} · ${field("Quantity").value} adet ${money(data.totalPrice)} (camlar hariç)`;
    const costs = [["Profil", money(data.profileCost)]];
    if (data.panelCost > 0) costs.push(["Panel", money(data.panelCost)]);
    costs.push(["Donanım", money(data.hardwareCost)], ["İşçilik", money(data.laborCost)], ["Birim maliyet", money(data.unitCost)], ["Birim satış", money(data.unitPrice)]);
    table("Maliyet (birim, camlar hariç)", ["Kalem", "Tutar"], costs);
    table("Kesim listesi (1 adet)", ["Profil", "Parça", Units.code, "Ad.", "Kesim"], data.cuts.map((c) => [c.profile, c.label, Units.format(c.lengthMm), c.count, c.ends]));
    if (data.hardware.length) table("Donanım (1 adet)", ["Donanım", "Ad."], data.hardware.map((h) => [h.name, h.count]));
  }

  async function calculate() {
    const current = ++requestNumber;
    if (!field("ProfileSystemId").value) {
      alerts.textContent = ""; box.textContent = "";
      summary.textContent = "Kesim ve maliyet için profil sistemi seçin.";
      return;
    }
    const body = {
      category: field("Category").value, profileSystemId: Number(field("ProfileSystemId").value), widthMm: Number(field("WidthMm").value), heightMm: Number(field("HeightMm").value),
      shape: field("Shape")?.value || "rect", shapeParamMm: Number(field("ShapeParamMm")?.value) || 0, quantity: Number(field("Quantity").value),
      layoutJson: field("LayoutJson")?.value || "{}", paramsJson: field("ParamsJson")?.value || "{}", name: "-"
    };
    try {
      const response = await fetch("/yonetim/cizim/hesapla", {
        method: "POST",
        headers: { "Content-Type": "application/json", RequestVerificationToken: field("__RequestVerificationToken").value },
        body: JSON.stringify(body)
      });
      const data = response.ok || response.status === 400 ? await response.json() : { errors: ["Hesaplama yapılamadı."] };
      if (current === requestNumber) show(data);
    } catch {
      if (current === requestNumber) show({ errors: ["Sunucuya ulaşılamadı."] });
    }
  }

  const schedule = () => { clearTimeout(timer); timer = setTimeout(calculate, 250); };
  document.addEventListener("drawing:change", schedule);
  document.addEventListener("units:change", schedule);
  field("ProfileSystemId").addEventListener("change", schedule);
  field("Quantity").addEventListener("change", schedule);
  schedule();
})();
