// Panjur, korkuluk ve cam balkon editörü: birkaç seçim yeterlidir, çizim kendini kurar.
(() => {
  const form = document.getElementById("pd-form");
  if (!form) return;
  const systems = JSON.parse(document.getElementById("pd-systems").textContent);
  const svg = document.getElementById("pd-canvas");
  const field = (name) => form.elements[name];
  const num = (name) => Number(field(name).value) || 0;
  const system = () => systems.find((s) => s.id === num("ProfileSystemId")) || systems[0];
  const params = () => {
    const out = {};
    form.querySelectorAll("[data-param]").forEach((input) => { out[input.dataset.param] = input.type === "number" ? Number(input.value) || 0 : input.value; });
    return out;
  };
  const money = (value) => Number(value).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " ₺";
  const length = (value) => Units.format(value);

  function draw() {
    const s = system();
    WindowDrawing.render(svg, { category: field("Category").value, widthMm: num("WidthMm"), heightMm: num("HeightMm"), colorHex: field("ColorHex").value, params: params(), settings: s.settings, sys: s });
  }

  function table(parent, title, headers, rows) {
    const h = document.createElement("h2");
    h.textContent = title;
    parent.appendChild(h);
    const t = document.createElement("table");
    t.className = "admin-table wd-table";
    const head = t.createTHead().insertRow();
    headers.forEach((text) => { const th = document.createElement("th"); th.textContent = text; head.appendChild(th); });
    const body = t.createTBody();
    rows.forEach((cells) => { const r = body.insertRow(); cells.forEach((cell) => { r.insertCell().textContent = cell; }); });
    parent.appendChild(t);
  }

  function notes(parent, items, className) {
    if (!items.length) return;
    const list = document.createElement("ul");
    list.className = className;
    items.forEach((item) => { const li = document.createElement("li"); li.textContent = item; list.appendChild(li); });
    parent.appendChild(list);
  }

  // Eksik fiyat uyarıları ekranı doldurmasın diye tek satıra toplanır.
  function compactWarnings(items) {
    const suffix = " fiyatı girilmemiş.", missing = items.filter((w) => w.endsWith(suffix)).map((w) => w.slice(0, -suffix.length));
    const rest = items.filter((w) => !w.endsWith(suffix));
    return missing.length ? rest.concat(`Fiyatı girilmemiş: ${missing.join(", ")}. Sistem ayarlarından girebilirsiniz.`) : rest;
  }

  function renderResult(data) {
    const alerts = document.getElementById("wd-alerts");
    alerts.textContent = "";
    notes(alerts, data.errors || [], "wd-errors");
    notes(alerts, compactWarnings(data.warnings || []), "wd-warnings");
    const box = document.getElementById("wd-result");
    const summary = document.getElementById("wd-summary");
    box.textContent = "";
    if ((data.errors || []).length) { summary.textContent = "Çizimde hata var"; return; }
    summary.textContent = `Birim ${money(data.unitPrice)} · ${num("Quantity")} adet ${money(data.totalPrice)} (camlar hariç)`;
    document.getElementById("wd-glass-note").textContent = data.glassCount > 0
      ? `Bu çizimde ${data.glassCount} adet cam var. Kaydettikten sonra imalat föyünden cam listesine aktarabilirsiniz.` : "";
    table(box, "Maliyet (birim, camlar hariç)", ["Kalem", "Tutar"], [["Profil", money(data.profileCost)], ["Donanım", money(data.hardwareCost)], ["İşçilik", money(data.laborCost)], ["Birim maliyet", money(data.unitCost)], ["Birim satış", money(data.unitPrice)]]);
    table(box, "Kesim listesi (1 adet)", ["Parça", Units.code, "Ad."], data.cuts.map((c) => [c.label, length(c.lengthMm), c.count]));
    if (data.hardware.length) table(box, "Donanım (1 adet)", ["Donanım", "Ad."], data.hardware.map((h) => [h.name, h.count]));
  }

  let timer, requestNumber = 0;
  async function calculate() {
    const current = ++requestNumber;
    const body = {
      category: field("Category").value, profileSystemId: num("ProfileSystemId"), widthMm: num("WidthMm"), heightMm: num("HeightMm"),
      shape: "rect", shapeParamMm: 0, quantity: num("Quantity"), paramsJson: JSON.stringify(params()), name: "-"
    };
    try {
      const response = await fetch("/yonetim/cizim/hesapla", {
        method: "POST",
        headers: { "Content-Type": "application/json", RequestVerificationToken: field("__RequestVerificationToken").value },
        body: JSON.stringify(body)
      });
      const data = response.ok || response.status === 400 ? await response.json() : { errors: ["Hesaplama yapılamadı."] };
      if (current === requestNumber) renderResult(data);
    } catch {
      if (current === requestNumber) renderResult({ errors: ["Sunucuya ulaşılamadı."] });
    }
  }

  function refresh() {
    field("ParamsJson").value = JSON.stringify(params());
    draw();
    clearTimeout(timer);
    timer = setTimeout(calculate, 250);
  }

  document.addEventListener("units:change", refresh);

  // Kayıtlı seçimleri forma yükle
  const saved = JSON.parse(field("ParamsJson").value || "{}");
  form.querySelectorAll("[data-param]").forEach((input) => {
    if (saved[input.dataset.param] !== undefined && saved[input.dataset.param] !== null) input.value = saved[input.dataset.param];
  });
  form.addEventListener("input", refresh);
  form.addEventListener("change", refresh);
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => { field("ColorHex").value = button.dataset.color; refresh(); }));
  form.addEventListener("submit", () => { field("ParamsJson").value = JSON.stringify(params()); });
  refresh();
})();
