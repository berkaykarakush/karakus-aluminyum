// Cam listesi: çoklu ölçü girişi, Excel'den yapıştırma ve canlı toplamlar.
(() => {
  const form = document.getElementById("gl-form");
  if (!form) return;
  const types = JSON.parse(document.getElementById("gl-types").textContent);
  const active = types.filter((t) => t.active);
  const body = document.getElementById("gl-rows");
  const rows = JSON.parse(form.elements.ItemsJson.value || "[]");
  const number = (value, digits) => Number(value).toLocaleString("tr-TR", { minimumFractionDigits: digits, maximumFractionDigits: digits });
  const typeOf = (id) => types.find((t) => t.id === id);
  const newRow = (values) => ({ glassTypeId: Number(document.getElementById("gl-default-type").value) || active[0]?.id, position: "", widthMm: 0, heightMm: 0, quantity: 1, shaped: false, note: "", ...values });

  function totals() {
    const byType = new Map();
    let pieces = 0;
    rows.forEach((r) => {
      const area = (Number(r.widthMm) * Number(r.heightMm) / 1e6) * (Number(r.quantity) || 0);
      pieces += Number(r.quantity) || 0;
      byType.set(r.glassTypeId, (byType.get(r.glassTypeId) || 0) + area);
    });
    const box = document.getElementById("gl-totals");
    box.textContent = "";
    let price = 0;
    byType.forEach((area, id) => {
      const type = typeOf(id);
      price += area * (type?.price || 0);
      const line = document.createElement("div");
      line.textContent = `${type?.name || "Cam"}: ${number(area, 2)} m²`;
      box.appendChild(line);
    });
    const sum = document.createElement("strong");
    sum.textContent = `${rows.length} ölçü · ${pieces} adet · ${number(price, 2)} ₺`;
    box.appendChild(sum);
  }

  // Satırdaki küçük cam çizimi, gerçek oranı gösterir.
  function sketch(svg, row) {
    const w = Number(row.widthMm), h = Number(row.heightMm);
    svg.textContent = "";
    if (w <= 0 || h <= 0) return;
    const scale = 34 / Math.max(w, h), rect = document.createElementNS("http://www.w3.org/2000/svg", "rect");
    const rw = w * scale, rh = h * scale;
    rect.setAttribute("x", (36 - rw) / 2); rect.setAttribute("y", (36 - rh) / 2);
    rect.setAttribute("width", rw); rect.setAttribute("height", rh);
    rect.setAttribute("fill", "#cfe4f0"); rect.setAttribute("stroke", "#26313a");
    svg.appendChild(rect);
  }

  function render() {
    body.textContent = "";
    rows.forEach((row, index) => {
      const tr = body.insertRow();
      const cell = () => tr.insertCell();
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      svg.setAttribute("viewBox", "0 0 36 36"); svg.setAttribute("class", "gl-sketch"); svg.setAttribute("aria-hidden", "true");
      cell().appendChild(svg);
      sketch(svg, row);
      const input = (key, type, label) => {
        const el = document.createElement("input");
        el.type = type; el.value = row[key] ?? ""; el.setAttribute("aria-label", `${index + 1}. satır ${label}`);
        if (type === "number") el.min = "0";
        if (key === "widthMm" || key === "heightMm") el.dataset.mm = "glassSize";
        el.addEventListener("input", () => {
          row[key] = type === "number" ? Number(el.value) : el.value;
          sketch(svg, row);
          totals();
        });
        cell().appendChild(el);
        return el;
      };
      input("position", "text", "poz");
      input("widthMm", "number", "genişlik");
      input("heightMm", "number", "yükseklik");
      input("quantity", "number", "adet");
      const select = document.createElement("select");
      select.setAttribute("aria-label", `${index + 1}. satır cam tipi`);
      types.filter((t) => t.active || t.id === row.glassTypeId).forEach((t) => select.add(new Option(t.name, t.id, false, t.id === row.glassTypeId)));
      select.addEventListener("change", () => { row.glassTypeId = Number(select.value); totals(); });
      cell().appendChild(select);
      input("note", "text", "not");
      const remove = document.createElement("button");
      remove.type = "button"; remove.className = "doc-link"; remove.textContent = "Sil";
      remove.addEventListener("click", () => { rows.splice(index, 1); render(); });
      cell().appendChild(remove);
    });
    totals();
  }

  // Satırlar: "100 120 3" veya sekmeli Excel kopyası. Ölçüler seçili birimdedir, milimetreye çevrilir.
  // Adet yazılmazsa 1, başta metin varsa poz sayılır.
  function parse(text, unit) {
    const parsed = [], errors = [];
    text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).forEach((line) => {
      const parts = line.replace(/[xX×*]/g, " ").split(/[\t;]+|\s+/).filter(Boolean);
      const position = parts.length && !/^\d+([.,]\d+)?$/.test(parts[0]) ? parts.shift() : "";
      const values = parts.map((p) => Number(p.replace(",", ".")));
      if (values.length < 2 || values.slice(0, 3).some((v) => !Number.isFinite(v) || v <= 0)) { errors.push(line); return; }
      parsed.push(newRow({ position, widthMm: Math.round(Units.toMm(values[0], unit) * 10) / 10, heightMm: Math.round(Units.toMm(values[1], unit) * 10) / 10, quantity: Math.round(values[2]) || 1 }));
    });
    return { parsed, errors };
  }

  document.getElementById("gl-add").addEventListener("click", () => { rows.push(newRow()); render(); body.lastElementChild?.querySelector("input[type=number]")?.focus(); });
  document.getElementById("gl-paste-add").addEventListener("click", () => {
    const area = document.getElementById("gl-paste");
    const { parsed, errors } = parse(area.value, document.getElementById("gl-paste-unit").value);
    // Ölçüsü girilmemiş boş satırlar toplu eklemede temizlenir.
    if (parsed.length) for (let i = rows.length - 1; i >= 0; i--) if (!rows[i].widthMm && !rows[i].heightMm) rows.splice(i, 1);
    rows.push(...parsed);
    area.value = errors.join("\n");
    document.getElementById("gl-paste-note").textContent = errors.length ? `${errors.length} satır okunamadı; kutuda bırakıldı.` : `${parsed.length} ölçü eklendi.`;
    render();
  });
  form.addEventListener("submit", () => { form.elements.ItemsJson.value = JSON.stringify(rows); });
  const pasteUnit = document.getElementById("gl-paste-unit");
  pasteUnit.value = Units.code;
  document.addEventListener("units:change", () => { pasteUnit.value = Units.code; });
  if (!rows.length) rows.push(newRow());
  render();
})();
