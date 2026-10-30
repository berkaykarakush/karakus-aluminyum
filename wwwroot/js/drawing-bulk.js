// Toplu çizim: her satır tek çizimdir. Excel'den yapıştırılabilir, satırlar sonra editörde açılabilir.
(() => {
  const form = document.getElementById("db-form");
  if (!form) return;
  const WINDOW = [["fixed", "Sabit"], ["single", "Tek kanat"], ["double", "Çift kanat"], ["triple", "Üçlü"], ["sliding", "Sürme"], ["transom", "Üst vasistas"], ["door", "Kapı"]];
  const CATEGORIES = {
    aluminum: { label: "Alüminyum Doğrama", types: WINDOW, width: 1500, height: 1400 },
    pvc: { label: "Plastik Doğrama", types: WINDOW, width: 1500, height: 1400 },
    shutter: { label: "Panjur", types: [["motor", "Motorlu"], ["manual", "Manuel"]], width: 1500, height: 1400, params: (type) => ({ drive: type }) },
    railing: { label: "Korkuluk", types: [["glass", "Camlı"], ["bars", "Çubuklu"]], width: 4000, height: 1100, params: (type) => ({ infill: type, postSpacingMm: 0 }) },
    balcony: { label: "Cam Balkon", types: [["sliding", "Sürme"], ["folding", "Katlanır"]], width: 3000, height: 2200, params: (type) => ({ openType: type, panelCount: 4 }) }
  };

  const body = document.getElementById("db-rows");
  const defaultCategory = document.getElementById("db-category"), defaultType = document.getElementById("db-type");
  const pasteUnit = document.getElementById("db-paste-unit");
  const rows = JSON.parse(form.elements.ItemsJson.value || "[]").map((r) => ({ ...r, type: r.type || CATEGORIES[r.category]?.types[0][0] }));
  const color = () => form.elements.ColorHex.value;

  const fillTypes = (select, category, value) => {
    select.textContent = "";
    CATEGORIES[category].types.forEach(([key, text]) => select.add(new Option(text, key, false, key === value)));
  };
  const newRow = (values) => {
    const category = defaultCategory.value;
    return { category, type: defaultType.value, name: "", widthMm: CATEGORIES[category].width, heightMm: CATEGORIES[category].height, quantity: 1, ...values };
  };

  function layoutOf(row) {
    const root = WindowDrawing.templates[row.type]();
    root.id = "root";
    WindowDrawing.fit(root, row.widthMm, row.heightMm);
    return root;
  }
  const paramsOf = (row) => CATEGORIES[row.category].params?.(row.type) ?? null;
  const isWindow = (row) => CATEGORIES[row.category].types === WINDOW;

  function sketch(svg, row) {
    svg.textContent = "";
    if (!(row.widthMm > 0 && row.heightMm > 0)) return;
    WindowDrawing.render(svg, {
      category: row.category, widthMm: row.widthMm, heightMm: row.heightMm, shape: "rect", shapeParamMm: 0, colorHex: color(),
      root: isWindow(row) ? layoutOf(row) : null, params: paramsOf(row)
    }, { bare: true });
  }

  function totals() {
    const pieces = rows.reduce((sum, r) => sum + (Number(r.quantity) || 0), 0);
    document.getElementById("db-totals").textContent = `${rows.length} çizim · ${pieces} adet`;
  }

  function render() {
    body.textContent = "";
    rows.forEach((row, index) => {
      const tr = body.insertRow();
      const cell = () => tr.insertCell();
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      svg.setAttribute("class", "gl-sketch"); svg.setAttribute("aria-hidden", "true");
      cell().appendChild(svg);

      const category = document.createElement("select");
      category.setAttribute("aria-label", `${index + 1}. satır tür`);
      Object.entries(CATEGORIES).forEach(([key, c]) => category.add(new Option(c.label, key, false, key === row.category)));
      cell().appendChild(category);
      const type = document.createElement("select");
      tr.addEventListener("input", () => { delete row.untouched; });
      tr.addEventListener("change", () => { delete row.untouched; });
      type.setAttribute("aria-label", `${index + 1}. satır tip`);
      fillTypes(type, row.category, row.type);
      cell().appendChild(type);
      category.addEventListener("change", () => {
        const defaults = CATEGORIES[category.value];
        row.category = category.value; row.type = defaults.types[0][0];
        fillTypes(type, row.category, row.type);
        sketch(svg, row);
      });
      type.addEventListener("change", () => { row.type = type.value; sketch(svg, row); });

      const input = (key, inputType, label) => {
        const el = document.createElement("input");
        el.type = inputType; el.value = row[key] ?? ""; el.setAttribute("aria-label", `${index + 1}. satır ${label}`);
        if (inputType === "number") el.min = "0";
        if (key === "widthMm" || key === "heightMm") el.dataset.mm = "bulkSize";
        el.addEventListener("input", () => {
          row[key] = inputType === "number" ? Number(el.value) : el.value;
          sketch(svg, row);
          totals();
        });
        cell().appendChild(el);
      };
      input("name", "text", "ad");
      input("widthMm", "number", "genişlik");
      input("heightMm", "number", "yükseklik");
      input("quantity", "number", "adet");
      sketch(svg, row);

      const actions = cell();
      const copy = document.createElement("button");
      copy.type = "button"; copy.className = "doc-link"; copy.textContent = "Çoğalt";
      copy.addEventListener("click", () => { delete row.untouched; rows.splice(index + 1, 0, { ...row, name: "" }); render(); });
      const remove = document.createElement("button");
      remove.type = "button"; remove.className = "doc-link"; remove.textContent = "Sil";
      remove.addEventListener("click", () => { rows.splice(index, 1); render(); });
      actions.append(copy, " ", remove);
    });
    totals();
  }

  // Satırlar: "Salon 150 140 2" veya Excel sütunları (ad, genişlik, yükseklik, adet). Ad ve adet yazılmayabilir.
  function parse(text, unit) {
    const parsed = [], errors = [];
    const isNumber = (value) => /^\d+([.,]\d+)?$/.test(value);
    text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).forEach((line) => {
      let name = "", parts;
      if (line.includes("\t") || line.includes(";")) {
        parts = line.split(/[\t;]+/).map((p) => p.trim()).filter(Boolean);
        if (parts.length && !isNumber(parts[0])) name = parts.shift();
      } else {
        parts = line.replace(/(\d)\s*[xX×*]\s*(?=\d)/g, "$1 ").split(/\s+/);
        const words = [];
        while (parts.length && !isNumber(parts[0])) words.push(parts.shift());
        name = words.join(" ");
      }
      const values = parts.map((p) => Number(p.replace(",", ".")));
      const width = Units.toMm(values[0], unit), height = Units.toMm(values[1], unit);
      // En küçük çizim 20 cm: "Salon 2 150 140" gibi okunamayan satırlar kutuda kalır.
      if (values.length < 2 || values.length > 3 || values.some((v) => !Number.isFinite(v) || v <= 0) || width < 200 || height < 200) { errors.push(line); return; }
      parsed.push(newRow({ name, widthMm: Math.round(width), heightMm: Math.round(height), quantity: Math.max(1, Math.round(values[2] || 1)) }));
    });
    return { parsed, errors };
  }

  defaultCategory.addEventListener("change", () => fillTypes(defaultType, defaultCategory.value));
  document.getElementById("db-add").addEventListener("click", () => {
    rows.push(newRow());
    render();
    body.lastElementChild?.querySelector("input")?.focus();
  });
  document.getElementById("db-paste-add").addEventListener("click", () => {
    const area = document.getElementById("db-paste");
    const { parsed, errors } = parse(area.value, pasteUnit.value);
    // Sayfa açılışındaki dokunulmamış satır yapıştırılanlarla birlikte kaydedilmesin.
    if (parsed.length) for (let i = rows.length - 1; i >= 0; i--) if (rows[i].untouched) rows.splice(i, 1);
    rows.push(...parsed);
    area.value = errors.join("\n");
    document.getElementById("db-paste-note").textContent = errors.length ? `${errors.length} satır okunamadı; kutuda bırakıldı.` : `${parsed.length} çizim eklendi.`;
    render();
  });
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => { form.elements.ColorHex.value = button.dataset.color; render(); }));
  form.elements.ColorHex.addEventListener("change", render);
  document.addEventListener("units:change", () => { pasteUnit.value = Units.code; });

  form.addEventListener("submit", () => {
    form.elements.ItemsJson.value = JSON.stringify(rows.map((row) => ({
      category: row.category, type: row.type, name: row.name, quantity: row.quantity, widthMm: Math.round(row.widthMm), heightMm: Math.round(row.heightMm),
      layoutJson: isWindow(row) ? JSON.stringify(layoutOf(row)) : null,
      paramsJson: isWindow(row) ? null : JSON.stringify(paramsOf(row))
    })));
  });

  pasteUnit.value = Units.code;
  fillTypes(defaultType, defaultCategory.value);
  if (!rows.length) rows.push(newRow({ untouched: true }));
  render();
})();
