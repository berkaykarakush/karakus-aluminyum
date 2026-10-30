// Çizim listesi: her satır bir çizimdir. Excel'den yapıştırılabilir; ayrıntı için satırdaki çizim açılır.
(() => {
  const form = document.getElementById("dl-form");
  if (!form) return;
  const WINDOW = [["fixed", "Sabit"], ["single", "Tek kanat"], ["double", "Çift kanat"], ["triple", "Üçlü"], ["sliding", "Sürme"], ["transom", "Üst vasistas"], ["door", "Kapı"]];
  const CATEGORIES = {
    aluminum: { label: "Alüminyum Doğrama", types: WINDOW, width: 1500, height: 1400 },
    pvc: { label: "Plastik Doğrama", types: WINDOW, width: 1500, height: 1400 },
    shutter: { label: "Panjur", types: [["motor", "Motorlu"], ["manual", "Manuel"]], width: 1500, height: 1400, key: "drive" },
    railing: { label: "Korkuluk", types: [["glass", "Camlı"], ["bars", "Çubuklu"]], width: 4000, height: 1100, key: "infill", extra: { postSpacingMm: 0 } },
    balcony: { label: "Cam Balkon", types: [["sliding", "Sürme"], ["folding", "Katlanır"]], width: 3000, height: 2200, key: "openType", extra: { panelCount: 4 } }
  };
  const brands = JSON.parse(document.getElementById("dl-brands").textContent);
  const body = document.getElementById("dl-rows");
  const defaultCategory = document.getElementById("dl-category"), defaultType = document.getElementById("dl-type"), defaultBrand = document.getElementById("dl-brand");
  const pasteUnit = document.getElementById("dl-paste-unit");
  const color = () => form.elements.ColorHex.value;
  const isWindow = (row) => CATEGORIES[row.category].types === WINDOW;
  const parseJson = (text) => { try { return JSON.parse(text || "{}") || {}; } catch { return {}; } };

  // Kayıtlı satırın tipi: ürünlerde seçimden okunur, doğramada editörde değişmiş olabileceği için "özel"dir.
  function typeOf(row) {
    if (row.type) return row.type;
    if (isWindow(row)) return row.layoutJson ? "custom" : "fixed";
    const definition = CATEGORIES[row.category];
    return parseJson(row.paramsJson)[definition.key] || definition.types[0][0];
  }
  const rows = JSON.parse(form.elements.ItemsJson.value || "[]").map((row) => ({ ...row, type: typeOf(row) }));

  function fillTypes(select, category, value) {
    select.textContent = "";
    if (value === "custom") select.add(new Option("Özel çizim", "custom", false, true));
    CATEGORIES[category].types.forEach(([key, text]) => select.add(new Option(text, key, false, key === value)));
  }
  function fillBrands(select, category, value) {
    select.textContent = "";
    select.add(new Option("Marka yok", ""));
    brands.filter((b) => b.category === category && (b.active || b.id === value)).forEach((b) => select.add(new Option(b.name, b.id, false, b.id === value)));
  }
  const newRow = (values) => {
    const category = defaultCategory.value, definition = CATEGORIES[category];
    return { category, type: defaultType.value, brandId: Number(defaultBrand.value) || null, name: "", widthMm: definition.width, heightMm: definition.height, quantity: 1, ...values };
  };

  function layoutOf(row) {
    const root = row.layoutJson ? parseJson(row.layoutJson) : WindowDrawing.templates[row.type]();
    root.id ||= "root";
    WindowDrawing.fit(root, row.widthMm, row.heightMm);
    return root;
  }
  function paramsOf(row) {
    const definition = CATEGORIES[row.category];
    return { ...definition.extra, ...parseJson(row.paramsJson), [definition.key]: row.type };
  }

  function sketch(svg, row) {
    svg.textContent = "";
    if (!(row.widthMm > 0 && row.heightMm > 0)) return;
    WindowDrawing.render(svg, {
      category: row.category, widthMm: row.widthMm, heightMm: row.heightMm, shape: row.shape || "rect", shapeParamMm: row.shapeParamMm || 0, colorHex: row.colorHex || color(),
      root: isWindow(row) ? layoutOf(row) : null, params: isWindow(row) ? null : paramsOf(row)
    }, { bare: true });
  }

  function totals() {
    const pieces = rows.reduce((sum, r) => sum + (Number(r.quantity) || 0), 0);
    document.getElementById("dl-totals").textContent = `${rows.length} çizim · ${pieces} adet`;
  }

  function render() {
    body.textContent = "";
    rows.forEach((row, index) => {
      const tr = body.insertRow();
      tr.addEventListener("input", () => { delete row.untouched; });
      tr.addEventListener("change", () => { delete row.untouched; });
      const cell = () => tr.insertCell();
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      svg.setAttribute("class", "gl-sketch"); svg.setAttribute("aria-hidden", "true");
      cell().appendChild(svg);

      const select = (label) => {
        const el = document.createElement("select");
        el.setAttribute("aria-label", `${index + 1}. satır ${label}`);
        cell().appendChild(el);
        return el;
      };
      const category = select("tür"), type = select("tip"), brand = select("marka");
      Object.entries(CATEGORIES).forEach(([key, c]) => category.add(new Option(c.label, key, false, key === row.category)));
      category.disabled = !!row.id; // kayıtlı çizimin türü değişmez
      fillTypes(type, row.category, row.type);
      fillBrands(brand, row.category, row.brandId);
      category.addEventListener("change", () => {
        Object.assign(row, { category: category.value, type: CATEGORIES[category.value].types[0][0], brandId: null, layoutJson: null, paramsJson: null, profileSystemId: null });
        fillTypes(type, row.category, row.type);
        fillBrands(brand, row.category, null);
        sketch(svg, row);
      });
      type.addEventListener("change", () => {
        row.type = type.value;
        if (isWindow(row)) row.layoutJson = null;
        sketch(svg, row);
      });
      brand.addEventListener("change", () => { row.brandId = Number(brand.value) || null; });

      const input = (key, inputType, label) => {
        const el = document.createElement("input");
        el.type = inputType; el.value = row[key] ?? ""; el.setAttribute("aria-label", `${index + 1}. satır ${label}`);
        if (inputType === "number") el.min = "0";
        if (key === "widthMm" || key === "heightMm") el.dataset.mm = "listSize";
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
      if (row.id) {
        const open = document.createElement("a");
        open.className = "doc-link"; open.href = `/yonetim/cizim/poz/${row.id}`; open.textContent = "Çizimi aç";
        actions.append(open, " ");
      }
      const copy = document.createElement("button");
      copy.type = "button"; copy.className = "doc-link"; copy.textContent = "Çoğalt";
      copy.addEventListener("click", () => {
        delete row.untouched;
        rows.splice(index + 1, 0, { ...row, id: undefined, name: "", layoutJson: isWindow(row) ? JSON.stringify(layoutOf(row)) : null });
        render();
      });
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

  defaultCategory.addEventListener("change", () => {
    fillTypes(defaultType, defaultCategory.value);
    fillBrands(defaultBrand, defaultCategory.value, null);
  });
  document.getElementById("dl-add").addEventListener("click", () => {
    rows.push(newRow());
    render();
    body.lastElementChild?.querySelector("input")?.focus();
  });
  document.getElementById("dl-paste-add").addEventListener("click", () => {
    const area = document.getElementById("dl-paste");
    const { parsed, errors } = parse(area.value, pasteUnit.value);
    // Sayfa açılışındaki dokunulmamış satır yapıştırılanlarla birlikte kaydedilmesin.
    if (parsed.length) for (let i = rows.length - 1; i >= 0; i--) if (rows[i].untouched) rows.splice(i, 1);
    rows.push(...parsed);
    area.value = errors.join("\n");
    document.getElementById("dl-paste-note").textContent = errors.length ? `${errors.length} satır okunamadı; kutuda bırakıldı.` : `${parsed.length} çizim eklendi.`;
    render();
  });
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => { form.elements.ColorHex.value = button.dataset.color; render(); }));
  form.elements.ColorHex.addEventListener("change", render);
  document.addEventListener("units:change", () => { pasteUnit.value = Units.code; });

  form.addEventListener("submit", () => {
    form.elements.ItemsJson.value = JSON.stringify(rows.map(({ untouched, ...row }) => ({
      ...row, widthMm: Math.round(row.widthMm), heightMm: Math.round(row.heightMm),
      colorHex: row.colorHex || color(),
      layoutJson: isWindow(row) ? JSON.stringify(layoutOf(row)) : null,
      paramsJson: isWindow(row) ? null : JSON.stringify(paramsOf(row))
    })));
  });

  pasteUnit.value = Units.code;
  fillTypes(defaultType, defaultCategory.value);
  fillBrands(defaultBrand, defaultCategory.value, null);
  if (!rows.length) rows.push(newRow({ untouched: true }));
  render();
})();
