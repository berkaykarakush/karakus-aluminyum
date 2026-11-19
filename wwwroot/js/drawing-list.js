// Çizim listesi editörü (Ercom'daki sipariş ekranı): pozlar tek listededir, seçili poz aynı ekranda çizilir.
// Bölme ölçülerinin toplamı dış ölçüye eşittir. Kesim ve maliyet özeti sunucuda hesaplanır (/yonetim/cizim/hesapla).
(() => {
  const form = document.getElementById("dl-form");
  if (!form) return;
  const NS = "http://www.w3.org/2000/svg";
  const MIN_SIZE = 100;
  const CATEGORIES = {
    aluminum: { label: "Alüminyum Doğrama", short: "Alüminyum", width: 1500, height: 1400, window: true },
    pvc: { label: "Plastik Doğrama", short: "PVC", width: 1500, height: 1400, window: true },
    shutter: { label: "Panjur", short: "Panjur", width: 1500, height: 1400, params: { drive: "motor" } },
    railing: { label: "Korkuluk", short: "Korkuluk", width: 4000, height: 1100, params: { infill: "glass", postSpacingMm: 0 } },
    balcony: { label: "Cam Balkon", short: "Cam balkon", width: 3000, height: 2200, params: { openType: "sliding", panelCount: 4 } }
  };
  const DEFAULT_COLOR = "#f4f5f5", DEFAULT_GLASS = "4+16+4";
  const brands = JSON.parse(document.getElementById("dl-brands").textContent);
  const systems = JSON.parse(document.getElementById("dl-systems").textContent);
  const $ = (id) => document.getElementById(id);
  const body = $("dl-rows"), svg = $("wd-canvas"), categorySelect = $("dl-category");
  const parseJson = (text, fallback) => { try { return JSON.parse(text || "") ?? fallback; } catch { return fallback; } };
  const isWindow = (row) => !!CATEGORIES[row.category]?.window;
  const systemOf = (row) => systems.find((s) => s.id === row.profileSystemId);
  const firstSystem = (category) => systems.find((s) => s.category === category && s.active);
  const mm = (input) => Math.round(Number(input.value) || 0);

  // Sunucudan gelen satırlar: layoutJson ve paramsJson nesneye çevrilir.
  const rows = parseJson(form.elements.ItemsJson.value, []).map((item) => {
    const row = { ...item, shape: item.shape || "rect", shapeParamMm: item.shapeParamMm || 0, colorHex: item.colorHex || DEFAULT_COLOR };
    if (isWindow(row)) row.root = parseJson(item.layoutJson, null) || WindowDrawing.templates.fixed();
    else row.params = { ...CATEGORIES[row.category].params, ...parseJson(item.paramsJson, {}) };
    if (row.profileSystemId === undefined) row.profileSystemId = null;
    delete row.layoutJson; delete row.paramsJson;
    return row;
  });
  let current = 0, selectedLeaf = "", dirty = false;
  const undoStack = [], redoStack = [];
  const row = () => rows[current];

  function fitRoot(r) {
    if (!isWindow(r)) return;
    r.root.id ||= "root";
    WindowDrawing.fit(r.root, r.widthMm, r.heightMm);
  }
  const drawingOf = (r) => ({
    category: r.category, widthMm: r.widthMm, heightMm: r.heightMm, shape: r.shape || "rect", shapeParamMm: r.shapeParamMm || 0, colorHex: r.colorHex,
    root: isWindow(r) ? r.root : null, params: isWindow(r) ? null : { ...r.params }, profile: isWindow(r) ? systemOf(r)?.profile : null, glass: r.glassSpec
  });

  function newRow(category, values) {
    const definition = CATEGORIES[category], base = row();
    const r = {
      category, name: "", widthMm: definition.width, heightMm: definition.height, quantity: 1, shape: "rect", shapeParamMm: 0,
      colorHex: base?.colorHex || DEFAULT_COLOR, brandId: base && base.category === category ? base.brandId : null,
      profileSystemId: base && base.category === category ? base.profileSystemId : firstSystem(category)?.id ?? null,
      glassSpec: definition.window ? (base && isWindow(base) ? base.glassSpec : DEFAULT_GLASS) : null, notes: "", ...values
    };
    if (definition.window) r.root = r.root || WindowDrawing.templates.single();
    else r.params = { ...definition.params };
    fitRoot(r);
    return r;
  }

  // Geri al / yinele: seçili pozun anlık görüntüsü tutulur.
  function snapshot() {
    undoStack.push({ index: current, data: JSON.stringify(row()) });
    if (undoStack.length > 200) undoStack.shift();
    redoStack.length = 0;
  }
  function restore(from, to) {
    const step = from.pop();
    if (!step || !rows[step.index]) return;
    to.push({ index: step.index, data: JSON.stringify(rows[step.index]) });
    rows[step.index] = JSON.parse(step.data);
    current = step.index;
    selectedLeaf = firstLeaf(row().root);
    changed();
    renderAll();
  }

  function changed() {
    if (row()) delete row().untouched;
    dirty = true;
    $("dl-dirty").textContent = "Kaydedilmemiş değişiklik var.";
  }

  // ---- Poz tablosu ----
  function sketch(target, r) {
    target.textContent = "";
    if (r.widthMm > 0 && r.heightMm > 0) WindowDrawing.render(target, drawingOf(r), { bare: true });
  }
  const systemName = (r) => systemOf(r)?.name || "-";
  const area = (r) => (r.widthMm * r.heightMm / 1e6);
  const fmtArea = (value) => value.toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  function renderGrid() {
    body.textContent = "";
    rows.forEach((r, index) => {
      const tr = body.insertRow();
      tr.className = index === current ? "is-current" : "";
      tr.tabIndex = 0;
      tr.setAttribute("aria-selected", index === current);
      const cells = [String(index + 1), null, r.name || `Poz ${index + 1}`, CATEGORIES[r.category].short, Units.format(r.widthMm), Units.format(r.heightMm), String(r.quantity), systemName(r), isWindow(r) ? (r.glassSpec || "-") : "-", fmtArea(area(r) * r.quantity)];
      cells.forEach((text, i) => {
        const td = tr.insertCell();
        if (i === 1) { const s = document.createElementNS(NS, "svg"); s.setAttribute("class", "gl-sketch"); s.setAttribute("aria-hidden", "true"); td.appendChild(s); sketch(s, r); }
        else td.textContent = text;
      });
      const pick = () => { if (current !== index) { current = index; selectedLeaf = firstLeaf(r.root); renderAll(); } };
      tr.addEventListener("click", pick);
      tr.addEventListener("keydown", (event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); pick(); } });
    });
    const pieces = rows.reduce((sum, r) => sum + (Number(r.quantity) || 0), 0);
    $("dl-totals").textContent = `${rows.length} poz`;
    $("dl-total-qty").textContent = pieces;
    $("dl-total-area").textContent = fmtArea(rows.reduce((sum, r) => sum + area(r) * (Number(r.quantity) || 0), 0));
    $("dl-current").textContent = rows.length ? `Poz ${current + 1} / ${rows.length}` : "";
    ["dl-prev", "dl-up"].forEach((id) => { $(id).disabled = current <= 0; });
    ["dl-next", "dl-down"].forEach((id) => { $(id).disabled = current >= rows.length - 1; });
    $("dl-remove").disabled = rows.length <= 1;
  }

  function refreshGridRow() {
    const tr = body.rows[current];
    if (!tr) return renderGrid();
    const r = row();
    const values = [null, null, r.name || `Poz ${current + 1}`, CATEGORIES[r.category].short, Units.format(r.widthMm), Units.format(r.heightMm), String(r.quantity), systemName(r), isWindow(r) ? (r.glassSpec || "-") : "-", fmtArea(area(r) * r.quantity)];
    values.forEach((text, i) => { if (text !== null) tr.cells[i].textContent = text; });
    sketch(tr.cells[1].firstChild, r);
    const pieces = rows.reduce((sum, x) => sum + (Number(x.quantity) || 0), 0);
    $("dl-total-qty").textContent = pieces;
    $("dl-total-area").textContent = fmtArea(rows.reduce((sum, x) => sum + area(x) * (Number(x.quantity) || 0), 0));
  }

  // ---- Seçili pozun editörü ----
  const nodeAt = (key) => (key === "" ? [] : key.split(".").map(Number)).reduce((node, i) => node.children[i], row().root);
  const firstLeaf = (node, key = "") => (!node ? "" : node.split ? firstLeaf(node.children[0], key === "" ? "0" : `${key}.0`) : key);

  function regionOf(key) {
    let w = row().widthMm, h = row().heightMm, node = row().root;
    (key === "" ? [] : key.split(".").map(Number)).forEach((i) => {
      if (node.split === "v") w = node.sizes[i]; else h = node.sizes[i];
      node = node.children[i];
    });
    return { w, h };
  }

  function fillSelect(select, items, value, empty) {
    select.textContent = "";
    select.add(new Option(empty, ""));
    items.forEach((item) => select.add(new Option(item.name, item.id, false, item.id === value)));
    select.value = value ?? "";
  }

  function fillEditor() {
    const r = row(), win = isWindow(r);
    $("dl-editor-title").textContent = `Poz ${current + 1} · ${CATEGORIES[r.category].label}`;
    $("pz-name").value = r.name || "";
    $("pz-name").placeholder = `Poz ${current + 1}`;
    $("pz-width").value = r.widthMm;
    $("pz-height").value = r.heightMm;
    $("pz-width-label").textContent = r.category === "railing" ? "Uzunluk" : "Genişlik";
    $("pz-qty").value = r.quantity;
    $("pz-glass").value = r.glassSpec || "";
    $("pz-color").value = r.colorHex;
    $("pz-notes").value = r.notes || "";
    $("pz-shape").value = r.shape || "rect";
    $("pz-param").value = r.shapeParamMm || 0;
    fillSelect($("pz-system"), systems.filter((s) => s.category === r.category && (s.active || s.id === r.profileSystemId)), r.profileSystemId, "Seçilmedi");
    fillSelect($("pz-brand"), brands.filter((b) => b.category === r.category && (b.active || b.id === r.brandId)), r.brandId, "Marka yok");
    document.querySelectorAll("#dl-editor [data-for]").forEach((panel) => {
      const target = panel.dataset.for;
      panel.hidden = target === "window" ? !win : target !== r.category;
    });
    if (!win) document.querySelectorAll("[data-param]").forEach((input) => {
      const value = r.params?.[input.dataset.param];
      if (value !== undefined && value !== null && input.closest("[data-for]").dataset.for === r.category) input.value = value;
    });
    showBrand();
  }

  function showBrand() {
    const mark = $("wd-brand-mark"), brand = brands.find((b) => b.id === row().brandId);
    mark.textContent = "";
    mark.hidden = !brand;
    if (!brand) return;
    if (brand.logo) { const image = document.createElement("img"); image.src = brand.logo; image.alt = brand.name; mark.appendChild(image); }
    else mark.textContent = brand.name;
  }

  function renderSelection() {
    const r = row();
    if (!isWindow(r)) return;
    const node = nodeAt(selectedLeaf);
    document.querySelectorAll("[data-opening]").forEach((b) => b.classList.toggle("is-active", !node.split && (node.opening || "fixed") === b.dataset.opening));
    $("wd-fill-panel").checked = !node.split && node.fill === "panel";
    $("wd-merge").disabled = selectedLeaf === "";
    const box = $("wd-sizes");
    box.textContent = "";
    $("pz-param-row").hidden = (r.shape || "rect") === "rect";
    $("pz-param-label").textContent = r.shape === "arch" ? "Kemer yüksekliği" : "Alçak kenar yüksekliği";
    if (selectedLeaf === "") return;
    const parentKey = selectedLeaf.split(".").slice(0, -1).join(".");
    const parent = nodeAt(parentKey);
    const title = document.createElement("label");
    title.textContent = parent.split === "v" ? "Bölme genişlikleri" : "Bölme yükseklikleri";
    box.appendChild(title);
    const inputs = document.createElement("div");
    inputs.className = "wd-size-inputs";
    parent.sizes.forEach((size, i) => {
      const input = document.createElement("input");
      input.type = "number"; input.value = size; input.dataset.mm = "partSize";
      input.setAttribute("aria-label", `${i + 1}. bölme`);
      input.addEventListener("change", () => { snapshot(); setSize(parentKey, i, Number(input.value)); update(); });
      inputs.appendChild(input);
    });
    box.appendChild(inputs);
  }

  const draw = () => WindowDrawing.render(svg, drawingOf(row()), { interactive: isWindow(row()), selected: selectedLeaf });

  // Çizim değişti: tuval, seçim, tablo satırı ve maliyet özeti yenilenir.
  function update() {
    draw();
    renderSelection();
    refreshGridRow();
    schedule();
    changed();
  }

  function renderAll() {
    if (!rows.length) return;
    // Önceki pozun özeti yenisi gelene kadar görünmesin.
    requestNumber++;
    alerts.textContent = ""; result.textContent = ""; summary.textContent = "";
    current = Math.min(Math.max(current, 0), rows.length - 1);
    fitRoot(row());
    if (isWindow(row()) && selectedLeaf && !leafExists(selectedLeaf)) selectedLeaf = firstLeaf(row().root);
    renderGrid();
    fillEditor();
    draw();
    renderSelection();
    schedule();
  }
  function leafExists(key) {
    try { const node = nodeAt(key); return !!node && !node.split; } catch { return false; }
  }

  function setSize(parentKey, index, value) {
    const parent = nodeAt(parentKey);
    const neighbour = index < parent.sizes.length - 1 ? index + 1 : index - 1;
    const total = parent.sizes[index] + parent.sizes[neighbour];
    const size = Math.min(Math.max(value, MIN_SIZE), total - MIN_SIZE);
    if (!Number.isFinite(size) || total < 2 * MIN_SIZE) return;
    parent.sizes[index] = Math.round(size * 100) / 100;
    parent.sizes[neighbour] = Math.round((total - size) * 100) / 100;
    const { w, h } = regionOf(parentKey);
    WindowDrawing.fit(parent, w, h);
  }

  let idCounter = 1;
  const newId = () => `n${Date.now().toString(36)}${idCounter++}`;

  function split(direction, count, sliding) {
    const node = nodeAt(selectedLeaf);
    if (node.split) return;
    snapshot();
    const { w, h } = regionOf(selectedLeaf);
    node.split = direction;
    node.sliding = !!sliding;
    node.sizes = Array.from({ length: count }, () => 1);
    node.children = Array.from({ length: count }, (_, i) => ({ id: newId(), fill: node.fill || "glass", opening: sliding ? (i % 2 === 0 ? "slideRight" : "slideLeft") : "fixed" }));
    delete node.opening;
    WindowDrawing.fit(node, w, h);
    selectedLeaf = selectedLeaf === "" ? "0" : `${selectedLeaf}.0`;
    update();
  }

  function merge() {
    if (selectedLeaf === "") return;
    snapshot();
    const parentKey = selectedLeaf.split(".").slice(0, -1).join(".");
    const parent = nodeAt(parentKey);
    const fill = parent.children[0].fill || "glass";
    delete parent.split; delete parent.sliding; delete parent.sizes; delete parent.children;
    parent.opening = "fixed";
    parent.fill = fill;
    selectedLeaf = parentKey;
    update();
  }

  // ---- Tuval: bölme seçimi ve kayıt sürükleme ----
  let drag = null;
  function toModel(event) {
    const point = svg.createSVGPoint();
    point.x = event.clientX; point.y = event.clientY;
    return point.matrixTransform(svg.getScreenCTM().inverse());
  }
  svg.addEventListener("pointerdown", (event) => {
    if (!isWindow(row())) return;
    const bar = event.target.dataset?.bar;
    if (bar !== undefined) {
      const point = toModel(event), index = Number(event.target.dataset.index);
      snapshot();
      drag = { key: bar, index, startX: point.x, startY: point.y, base: nodeAt(bar).sizes[index] };
      svg.setPointerCapture(event.pointerId);
      event.preventDefault();
      return;
    }
    const clicked = event.target.dataset?.leaf;
    if (clicked !== undefined) { selectedLeaf = clicked; draw(); renderSelection(); }
  });
  svg.addEventListener("pointermove", (event) => {
    if (!drag) return;
    const point = toModel(event);
    const delta = nodeAt(drag.key).split === "v" ? point.x - drag.startX : point.y - drag.startY;
    setSize(drag.key, drag.index, Math.round(drag.base + delta));
    draw();
  });
  const endDrag = () => { if (drag) { drag = null; update(); } };
  svg.addEventListener("pointerup", endDrag);
  svg.addEventListener("pointercancel", endDrag);

  // Düğmelerdeki küçük önizlemeler gerçek çizimle üretilir.
  function preview(target, node, width, height) {
    const small = document.createElementNS(NS, "svg");
    small.setAttribute("aria-hidden", "true");
    WindowDrawing.fit(node, width, height);
    WindowDrawing.render(small, { widthMm: width, heightMm: height, shape: "rect", shapeParamMm: 0, colorHex: "#ffffff", root: node, profile: { frame: 60, sash: 70, mullion: 70, overlap: 8, clearance: 5 } }, { bare: true });
    target.prepend(small);
  }
  document.querySelectorAll("[data-template]").forEach((button) => {
    preview(button, WindowDrawing.templates[button.dataset.template](), 1400, 1400);
    button.addEventListener("click", () => {
      snapshot();
      row().root = WindowDrawing.templates[button.dataset.template]();
      fitRoot(row());
      selectedLeaf = firstLeaf(row().root);
      update();
    });
  });
  document.querySelectorAll("[data-opening]").forEach((button) => {
    preview(button, { id: newId(), opening: button.dataset.opening, fill: "glass" }, 900, 1200);
    button.addEventListener("click", () => {
      const node = nodeAt(selectedLeaf);
      if (node.split) return;
      snapshot();
      node.opening = button.dataset.opening;
      update();
    });
  });
  document.querySelectorAll("[data-split]").forEach((button) => button.addEventListener("click", () =>
    split(button.dataset.split, Number(button.dataset.count), button.dataset.sliding === "true")));
  $("wd-merge").addEventListener("click", merge);
  $("wd-undo").addEventListener("click", () => restore(undoStack, redoStack));
  $("wd-redo").addEventListener("click", () => restore(redoStack, undoStack));
  $("wd-fill-panel").addEventListener("change", (event) => {
    const node = nodeAt(selectedLeaf);
    if (node.split) return;
    snapshot();
    node.fill = event.target.checked ? "panel" : "glass";
    update();
  });

  // ---- Alanlar ----
  const on = (id, events, handler) => events.split(" ").forEach((name) => $(id).addEventListener(name, handler));
  on("pz-name", "input", () => { row().name = $("pz-name").value; refreshGridRow(); changed(); });
  on("pz-qty", "input", () => { row().quantity = Math.max(0, Math.round(Number($("pz-qty").value) || 0)); refreshGridRow(); schedule(); changed(); });
  on("pz-glass", "input", () => { row().glassSpec = $("pz-glass").value; update(); });
  on("pz-notes", "input", () => { row().notes = $("pz-notes").value; changed(); });
  ["pz-width", "pz-height"].forEach((id) => on(id, "change", () => {
    snapshot();
    row().widthMm = mm($("pz-width"));
    row().heightMm = mm($("pz-height"));
    fitRoot(row());
    update();
  }));
  on("pz-system", "change", () => { row().profileSystemId = Number($("pz-system").value) || 0; update(); });
  on("pz-brand", "change", () => { row().brandId = Number($("pz-brand").value) || null; showBrand(); changed(); });
  const setColor = (hex) => { row().colorHex = hex; $("pz-color").value = hex; update(); };
  on("pz-color", "change", () => setColor($("pz-color").value));
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => setColor(button.dataset.color)));
  on("pz-shape", "change", () => {
    const r = row(), shape = $("pz-shape").value;
    snapshot();
    r.shape = shape;
    if (shape === "arch" && (r.shapeParamMm <= 0 || r.shapeParamMm > r.widthMm / 2)) r.shapeParamMm = Math.round(r.widthMm / 4);
    if (shape.startsWith("slope") && (r.shapeParamMm <= 0 || r.shapeParamMm >= r.heightMm)) r.shapeParamMm = Math.round(r.heightMm * 0.7);
    if (shape === "rect") r.shapeParamMm = 0;
    $("pz-param").value = r.shapeParamMm;
    update();
  });
  on("pz-param", "change", () => { snapshot(); row().shapeParamMm = mm($("pz-param")); update(); });
  document.querySelectorAll("[data-param]").forEach((input) => input.addEventListener("change", () => {
    const r = row();
    if (isWindow(r) || input.closest("[data-for]").dataset.for !== r.category) return;
    r.params[input.dataset.param] = input.type === "number" ? Math.round(Number(input.value) || 0) : input.value;
    update();
  }));

  // ---- Poz işlemleri ----
  function select(index) {
    current = index;
    selectedLeaf = firstLeaf(row().root);
    renderAll();
  }
  $("dl-add").addEventListener("click", () => {
    rows.splice(current + 1, 0, newRow(categorySelect.value));
    changed();
    select(current + 1);
    $("pz-width").focus();
  });
  $("dl-copy").addEventListener("click", () => {
    const copy = JSON.parse(JSON.stringify(row()));
    delete copy.id;
    copy.name = "";
    rows.splice(current + 1, 0, copy);
    changed();
    select(current + 1);
  });
  $("dl-remove").addEventListener("click", () => {
    if (rows.length <= 1) return;
    const name = row().name || `Poz ${current + 1}`;
    if (!confirm(`${name} listeden silinsin mi? Liste kaydedilince kalıcı olur.`)) return;
    rows.splice(current, 1);
    undoStack.length = 0; redoStack.length = 0;
    changed();
    select(Math.min(current, rows.length - 1));
  });
  const move = (offset) => {
    const target = current + offset;
    if (target < 0 || target >= rows.length) return;
    [rows[current], rows[target]] = [rows[target], rows[current]];
    undoStack.length = 0; redoStack.length = 0;
    changed();
    select(target);
  };
  $("dl-up").addEventListener("click", () => move(-1));
  $("dl-down").addEventListener("click", () => move(1));
  $("dl-prev").addEventListener("click", () => current > 0 && select(current - 1));
  $("dl-next").addEventListener("click", () => current < rows.length - 1 && select(current + 1));

  // ---- Toplu ekleme: "Salon 150 140 2" veya Excel sütunları (ad, genişlik, yükseklik, adet) ----
  function parse(text) {
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
      const [width, height] = values; // ölçüler mm
      // En küçük poz 200 mm: "Salon 2 150 140" gibi okunamayan satırlar kutuda kalır.
      if (values.length < 2 || values.length > 3 || values.some((v) => !Number.isFinite(v) || v <= 0) || width < 200 || height < 200) { errors.push(line); return; }
      parsed.push({ name, widthMm: Math.round(width), heightMm: Math.round(height), quantity: Math.max(1, Math.round(values[2] || 1)) });
    });
    return { parsed, errors };
  }
  $("dl-paste-add").addEventListener("click", () => {
    const area = $("dl-paste");
    const { parsed, errors } = parse(area.value);
    // Sayfa açılışındaki dokunulmamış boş poz yapıştırılanlarla birlikte kaydedilmesin.
    if (parsed.length && rows.length === 1 && rows[0].untouched) rows.splice(0, 1);
    const start = rows.length;
    parsed.forEach((values) => rows.push(newRow(categorySelect.value, values)));
    area.value = errors.join("\n");
    $("dl-paste-note").textContent = errors.length ? `${errors.length} satır okunamadı; kutuda bırakıldı.` : `${parsed.length} poz eklendi.`;
    if (parsed.length) { changed(); select(start); }
  });

  // ---- Canlı kesim ve maliyet özeti ----
  const alerts = $("wd-alerts"), summary = $("wd-summary"), result = $("wd-result");
  const money = (value) => `${Number(value).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₺`;
  let requestNumber = 0, timer = 0;
  function table(title, headers, data) {
    const heading = document.createElement("h3");
    heading.textContent = title;
    const element = document.createElement("table");
    element.className = "admin-table wd-table";
    element.createTHead().insertRow().append(...headers.map((text) => Object.assign(document.createElement("th"), { textContent: text })));
    const tbody = element.createTBody();
    data.forEach((cells) => { const tr = tbody.insertRow(); cells.forEach((value) => { tr.insertCell().textContent = value; }); });
    result.append(heading, element);
  }
  function notes(items, className) {
    if (!items.length) return;
    const list = document.createElement("ul");
    list.className = className;
    items.forEach((text) => { list.appendChild(document.createElement("li")).textContent = text; });
    alerts.appendChild(list);
  }
  function show(data) {
    alerts.textContent = ""; result.textContent = "";
    notes(data.errors || [], "wd-errors");
    notes(data.warnings || [], "wd-warnings");
    if ((data.errors || []).length) { summary.textContent = "Pozda hata var"; return; }
    summary.textContent = `Birim ${money(data.unitPrice)} · ${row().quantity} adet ${money(data.totalPrice)} (camlar hariç)`;
    const costs = [["Profil", money(data.profileCost)]];
    if (data.panelCost > 0) costs.push(["Panel", money(data.panelCost)]);
    costs.push(["Donanım", money(data.hardwareCost)], ["İşçilik", money(data.laborCost)], ["Birim maliyet", money(data.unitCost)], ["Birim satış", money(data.unitPrice)]);
    table("Maliyet (birim, camlar hariç)", ["Kalem", "Tutar"], costs);
    table("Kesim listesi (1 adet)", ["Profil", "Parça", Units.code, "Ad.", "Kesim"], data.cuts.map((c) => [c.profile, c.label, Units.format(c.lengthMm), c.count, c.ends]));
    if (data.hardware.length) table("Donanım (1 adet)", ["Donanım", "Ad."], data.hardware.map((h) => [h.name, h.count]));
  }
  async function calculate() {
    const number = ++requestNumber, r = row();
    if (!r.profileSystemId) {
      alerts.textContent = ""; result.textContent = "";
      summary.textContent = "Kesim ve maliyet için profil sistemi seçin.";
      return;
    }
    const payload = {
      category: r.category, profileSystemId: r.profileSystemId, widthMm: r.widthMm, heightMm: r.heightMm, shape: r.shape || "rect", shapeParamMm: r.shapeParamMm || 0,
      quantity: r.quantity, layoutJson: isWindow(r) ? JSON.stringify(r.root) : "{}", paramsJson: isWindow(r) ? "{}" : JSON.stringify(r.params), name: "-"
    };
    try {
      const response = await fetch("/yonetim/cizim/hesapla", {
        method: "POST",
        headers: { "Content-Type": "application/json", RequestVerificationToken: form.elements.__RequestVerificationToken.value },
        body: JSON.stringify(payload)
      });
      const data = response.ok || response.status === 400 ? await response.json() : { errors: ["Hesaplama yapılamadı."] };
      if (number === requestNumber) show(data);
    } catch {
      if (number === requestNumber) show({ errors: ["Sunucuya ulaşılamadı."] });
    }
  }
  function schedule() { clearTimeout(timer); timer = setTimeout(calculate, 250); }

  document.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || /INPUT|TEXTAREA|SELECT/.test(event.target.tagName)) return;
    if (event.key.toLowerCase() === "z") { event.preventDefault(); restore(undoStack, redoStack); }
    if (event.key.toLowerCase() === "y") { event.preventDefault(); restore(redoStack, undoStack); }
  });
  addEventListener("beforeunload", (event) => { if (dirty) { event.preventDefault(); event.returnValue = ""; } });

  form.addEventListener("submit", () => {
    dirty = false;
    form.elements.ItemsJson.value = JSON.stringify(rows.map(({ untouched, root, params, ...r }) => {
      if (isWindow(r)) fitRoot({ ...r, root });
      return {
        ...r, widthMm: Math.round(r.widthMm), heightMm: Math.round(r.heightMm), quantity: Math.round(r.quantity),
        layoutJson: isWindow(r) ? JSON.stringify(root) : null, paramsJson: isWindow(r) ? null : JSON.stringify(params),
        glassSpec: isWindow(r) ? (r.glassSpec || "") : null, notes: r.notes ?? ""
      };
    }));
  });

  if (!rows.length) rows.push({ ...newRow(form.dataset.category in CATEGORIES ? form.dataset.category : "aluminum"), untouched: true });
  const wanted = Number(form.dataset.selectedPoz);
  if (wanted) current = Math.max(0, rows.findIndex((r) => r.id === wanted));
  selectedLeaf = firstLeaf(row().root);
  rows.forEach(fitRoot);
  renderAll();
  if (wanted) document.getElementById("dl-editor").scrollIntoView();
})();
