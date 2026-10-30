// Doğrama editörü: ölçü gir, tip seç, bölmeye tıkla. Ölçü ve maliyet sonuçları sunucudan gelir.
(() => {
  const form = document.getElementById("wd-form");
  if (!form) return;

  const systems = JSON.parse(document.getElementById("wd-systems").textContent);
  const svg = document.getElementById("wd-canvas");
  const field = (name) => form.elements[name];
  const MIN_SIZE = 100;
  const PREVIEW_SYS = { frame: 70, sash: 80, mullion: 80, overlap: 8 };

  let root = JSON.parse(field("LayoutJson").value || "{}");
  let selected = "";
  let nextId = 1;
  const undoStack = [], redoStack = [];

  const num = (name) => Number(field(name).value) || 0;
  const sys = () => systems.find((s) => s.id === num("ProfileSystemId")) || systems[0];
  const newId = () => `n${Date.now().toString(36)}${nextId++}`;
  const design = () => ({ widthMm: num("WidthMm"), heightMm: num("HeightMm"), shape: field("Shape").value, shapeParamMm: num("ShapeParamMm"), colorHex: field("ColorHex").value, root, sys: sys() });

  function snapshot() {
    undoStack.push(JSON.stringify(root));
    if (undoStack.length > 100) undoStack.shift();
    redoStack.length = 0;
  }

  function restore(from, to) {
    if (!from.length) return;
    to.push(JSON.stringify(root));
    root = JSON.parse(from.pop());
    selected = firstLeaf(root);
    refresh();
  }

  const nodeAt = (key) => (key === "" ? [] : key.split(".").map(Number)).reduce((node, i) => node.children[i], root);
  const firstLeaf = (node, key = "") => (node.split ? firstLeaf(node.children[0], key === "" ? "0" : `${key}.0`) : key);

  // Dış ölçü veya kayıt değişince bölmeleri orantılı olarak yeni açıklığa oturtur.
  function fit(node, w, h, s) {
    if (!node.split) return;
    const bar = node.sliding ? 0 : s.mullion, axis = node.split === "v" ? w : h;
    const available = axis - bar * (node.children.length - 1);
    const sum = node.sizes.reduce((a, b) => a + b, 0) || 1;
    let used = 0;
    node.sizes = node.sizes.map((size, i) => {
      if (i === node.sizes.length - 1) return Math.round((available - used) * 100) / 100;
      const scaled = Math.round(size * available / sum);
      used += scaled;
      return scaled;
    });
    node.children.forEach((child, i) => fit(child, node.split === "v" ? node.sizes[i] : w, node.split === "v" ? h : node.sizes[i], s));
  }

  const fitAll = () => { const s = sys(); fit(root, num("WidthMm") - 2 * s.frame, num("HeightMm") - 2 * s.frame, s); };

  function regionOf(key) {
    const s = sys();
    let w = num("WidthMm") - 2 * s.frame, h = num("HeightMm") - 2 * s.frame, node = root;
    (key === "" ? [] : key.split(".").map(Number)).forEach((i) => {
      if (node.split === "v") w = node.sizes[i]; else h = node.sizes[i];
      node = node.children[i];
    });
    return { w, h };
  }

  function split(direction, count, sliding) {
    const node = nodeAt(selected);
    if (node.split) return;
    snapshot();
    const s = sys(), { w, h } = regionOf(selected);
    const size = ((direction === "v" ? w : h) - (sliding ? 0 : s.mullion) * (count - 1)) / count;
    node.split = direction;
    node.sliding = !!sliding;
    node.sizes = Array.from({ length: count }, () => Math.round(size));
    node.children = Array.from({ length: count }, (_, i) => ({ id: newId(), fill: node.fill || "glass", opening: sliding ? (i % 2 === 0 ? "slideRight" : "slideLeft") : "fixed" }));
    delete node.opening;
    fit(node, w, h, s);
    selected = selected === "" ? "0" : `${selected}.0`;
    refresh();
  }

  function merge() {
    if (selected === "") return;
    snapshot();
    const parentKey = selected.split(".").slice(0, -1).join(".");
    const parent = nodeAt(parentKey);
    const fill = parent.children[0].fill || "glass";
    delete parent.split; delete parent.sliding; delete parent.sizes; delete parent.children;
    parent.opening = "fixed";
    parent.fill = fill;
    selected = parentKey;
    refresh();
  }

  const leaf = (opening) => ({ id: newId(), opening: opening || "fixed", fill: "glass" });
  const row = (direction, children, sliding, sizes) => ({ id: newId(), split: direction, sliding: !!sliding, sizes: sizes || children.map(() => 1), children });
  const TEMPLATES = {
    fixed: () => leaf(),
    single: () => leaf("tiltRight"),
    double: () => row("v", [leaf("tiltLeft"), leaf("tiltRight")]),
    triple: () => row("v", [leaf("tiltLeft"), leaf(), leaf("tiltRight")]),
    sliding: () => row("v", [leaf("slideRight"), leaf("slideLeft")], true),
    transom: () => row("h", [leaf("tilt"), row("v", [leaf(), leaf("tiltRight")])], false, [1, 3]),
    door: () => row("h", [leaf(), leaf("doorLeft")], false, [1, 4])
  };

  function applyTemplate(name) {
    snapshot();
    root = TEMPLATES[name]();
    root.id = "root";
    fitAll();
    selected = firstLeaf(root);
    refresh();
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
    fit(parent, w, h, sys());
  }

  function renderSelection() {
    const node = nodeAt(selected);
    document.querySelectorAll("[data-opening]").forEach((b) => b.classList.toggle("is-active", !node.split && (node.opening || "fixed") === b.dataset.opening));
    document.getElementById("wd-fill-panel").checked = !node.split && node.fill === "panel";
    document.getElementById("wd-merge").disabled = selected === "";
    const box = document.getElementById("wd-sizes");
    box.textContent = "";
    if (selected === "") return;
    const parentKey = selected.split(".").slice(0, -1).join(".");
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
      input.addEventListener("change", () => { snapshot(); setSize(parentKey, i, Number(input.value)); refresh(); });
      inputs.appendChild(input);
    });
    box.appendChild(inputs);
  }

  const draw = () => WindowDrawing.render(svg, design(), { interactive: true, selected });

  function refresh() {
    field("LayoutJson").value = JSON.stringify(root);
    const shape = field("Shape").value;
    document.getElementById("wd-param-row").hidden = shape === "rect";
    document.getElementById("wd-param-label").textContent = shape === "arch" ? "Kemer yüksekliği (mm)" : "Alçak kenar yüksekliği (mm)";
    draw();
    renderSelection();
    scheduleCalculation();
  }

  // Sonuçlar: içerik yalnızca textContent ile yazılır.
  const money = (value) => Number(value).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " ₺";
  const length = (value) => Units.format(value);

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
    const costs = [["Profil", money(data.profileCost)]];
    if (data.panelCost > 0) costs.push(["Panel", money(data.panelCost)]);
    costs.push(["Donanım", money(data.hardwareCost)], ["İşçilik", money(data.laborCost)], ["Birim maliyet", money(data.unitCost)], ["Birim satış", money(data.unitPrice)]);
    table(box, "Maliyet (birim, camlar hariç)", ["Kalem", "Tutar"], costs);
    table(box, "Kesim listesi (1 adet)", ["Profil", "Parça", Units.code, "Ad.", "Kesim"], data.cuts.map((c) => [c.profile, c.label, length(c.lengthMm), c.count, c.ends]));
    if (data.hardware.length) table(box, "Donanım (1 adet)", ["Donanım", "Ad."], data.hardware.map((h) => [h.name, h.count]));
  }

  let timer, requestNumber = 0;
  function scheduleCalculation() {
    clearTimeout(timer);
    timer = setTimeout(calculate, 250);
  }

  async function calculate() {
    const current = ++requestNumber;
    const body = {
      category: field("Category").value, profileSystemId: num("ProfileSystemId"), widthMm: num("WidthMm"), heightMm: num("HeightMm"),
      shape: field("Shape").value, shapeParamMm: num("ShapeParamMm"), quantity: num("Quantity"), layoutJson: JSON.stringify(root), name: "-"
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

  // Tuval: bölme seçimi ve kayıt sürükleme
  let drag = null;
  function toModel(event) {
    const point = svg.createSVGPoint();
    point.x = event.clientX; point.y = event.clientY;
    return point.matrixTransform(svg.getScreenCTM().inverse());
  }

  svg.addEventListener("pointerdown", (event) => {
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
    if (clicked !== undefined) { selected = clicked; draw(); renderSelection(); }
  });

  svg.addEventListener("pointermove", (event) => {
    if (!drag) return;
    const point = toModel(event);
    const delta = nodeAt(drag.key).split === "v" ? point.x - drag.startX : point.y - drag.startY;
    setSize(drag.key, drag.index, Math.round(drag.base + delta));
    draw();
  });

  const endDrag = () => { if (drag) { drag = null; refresh(); } };
  svg.addEventListener("pointerup", endDrag);
  svg.addEventListener("pointercancel", endDrag);

  // Düğmelerdeki küçük önizlemeler gerçek çizimle üretilir.
  function preview(target, node, width, height) {
    const small = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    small.setAttribute("aria-hidden", "true");
    fit(node, width - 2 * PREVIEW_SYS.frame, height - 2 * PREVIEW_SYS.frame, PREVIEW_SYS);
    WindowDrawing.render(small, { widthMm: width, heightMm: height, shape: "rect", shapeParamMm: 0, colorHex: "#ffffff", root: node, sys: PREVIEW_SYS }, { bare: true });
    target.prepend(small);
  }

  document.querySelectorAll("[data-template]").forEach((button) => {
    preview(button, TEMPLATES[button.dataset.template](), 1400, 1400);
    button.addEventListener("click", () => applyTemplate(button.dataset.template));
  });
  document.querySelectorAll("[data-opening]").forEach((button) => {
    preview(button, leaf(button.dataset.opening), 900, 1200);
    button.addEventListener("click", () => {
      const node = nodeAt(selected);
      if (node.split) return;
      snapshot();
      node.opening = button.dataset.opening;
      refresh();
    });
  });
  document.querySelectorAll("[data-split]").forEach((button) => button.addEventListener("click", () =>
    split(button.dataset.split, Number(button.dataset.count), button.dataset.sliding === "true")));
  document.getElementById("wd-merge").addEventListener("click", merge);
  document.getElementById("wd-undo").addEventListener("click", () => restore(undoStack, redoStack));
  document.getElementById("wd-redo").addEventListener("click", () => restore(redoStack, undoStack));
  document.getElementById("wd-fill-panel").addEventListener("change", (e) => {
    const node = nodeAt(selected);
    if (node.split) return;
    snapshot();
    node.fill = e.target.checked ? "panel" : "glass";
    refresh();
  });

  ["WidthMm", "HeightMm", "ProfileSystemId"].forEach((name) => field(name).addEventListener("change", () => { fitAll(); refresh(); }));
  field("Shape").addEventListener("change", () => {
    const shape = field("Shape").value;
    if (shape === "arch" && (num("ShapeParamMm") <= 0 || num("ShapeParamMm") > num("WidthMm") / 2)) field("ShapeParamMm").value = Math.round(num("WidthMm") / 4);
    if (shape.startsWith("slope") && (num("ShapeParamMm") <= 0 || num("ShapeParamMm") >= num("HeightMm"))) field("ShapeParamMm").value = Math.round(num("HeightMm") * 0.7);
    refresh();
  });
  ["ShapeParamMm", "ColorHex", "Quantity"].forEach((name) => field(name).addEventListener("change", refresh));
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => { field("ColorHex").value = button.dataset.color; refresh(); }));

  document.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || /INPUT|TEXTAREA|SELECT/.test(event.target.tagName)) return;
    if (event.key.toLowerCase() === "z") { event.preventDefault(); restore(undoStack, redoStack); }
    if (event.key.toLowerCase() === "y") { event.preventDefault(); restore(redoStack, undoStack); }
  });

  form.addEventListener("submit", () => { field("LayoutJson").value = JSON.stringify(root); });

  document.addEventListener("units:change", refresh);

  if (!root.id) root.id = "root";
  fitAll();
  selected = firstLeaf(root);
  refresh();
})();
