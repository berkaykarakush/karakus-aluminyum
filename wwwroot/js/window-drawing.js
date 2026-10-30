// Ölçülü şekil çizimi. Editör, çizim sayfası, teklif ve sözleşme ekleri aynı çizimi kullanır.
// Kasa ve kayıt kalınlıkları yalnızca görseldir; ölçüler dış ölçüdür.
window.WindowDrawing = (() => {
  const NS = "http://www.w3.org/2000/svg";
  const DARK = "#26313a", GLASS = "#cfe4f0", PANEL = "#d4d8db";
  let clipCounter = 0;

  function el(parent, name, attrs, text) {
    const node = document.createElementNS(NS, name);
    Object.entries(attrs || {}).forEach(([key, value]) => node.setAttribute(key, value));
    if (text !== undefined) node.textContent = text;
    if (parent) parent.appendChild(node);
    return node;
  }

  const label = (mm) => (window.Units ? Units.format(mm) : String(Math.round(mm * 10) / 10));
  const frameOf = (d) => Math.min(60, Math.min(d.widthMm, d.heightMm) * 0.08);
  const archRadius = (d) => (d.widthMm * d.widthMm / 4 + d.shapeParamMm * d.shapeParamMm) / (2 * d.shapeParamMm);

  function topAt(d, x) {
    const w = d.widthMm, h = d.heightMm, p = d.shapeParamMm;
    if (d.shape === "arch") {
      const r = archRadius(d), dx = x - w / 2;
      return p - (Math.sqrt(Math.max(0, r * r - dx * dx)) - (r - p));
    }
    if (d.shape === "slopeL") return (h - p) * (1 - x / w);
    if (d.shape === "slopeR") return (h - p) * (x / w);
    return 0;
  }

  function outerPath(d) {
    const w = d.widthMm, h = d.heightMm, p = d.shapeParamMm;
    if (d.shape === "arch") { const r = archRadius(d); return `M0,${h} L${w},${h} L${w},${p} A${r},${r} 0 0 0 0,${p} Z`; }
    if (d.shape === "slopeL") return `M0,${h} L${w},${h} L${w},0 L0,${h - p} Z`;
    if (d.shape === "slopeR") return `M0,${h} L${w},${h} L${w},${h - p} L0,0 Z`;
    return `M0,0 L${w},0 L${w},${h} L0,${h} Z`;
  }

  function innerPath(d, f) {
    const w = d.widthMm, h = d.heightMm;
    if (d.shape === "arch") {
      const r = archRadius(d), ri = Math.max(r - f, 1), dx = w / 2 - f;
      const y = r - Math.sqrt(Math.max(0, ri * ri - dx * dx));
      return `M${f},${h - f} L${w - f},${h - f} L${w - f},${y} A${ri},${ri} 0 0 0 ${f},${y} Z`;
    }
    if (d.shape === "slopeL" || d.shape === "slopeR")
      return `M${f},${h - f} L${w - f},${h - f} L${w - f},${topAt(d, w - f) + f} L${f},${topAt(d, f) + f} Z`;
    return `M${f},${f} L${w - f},${f} L${w - f},${h - f} L${f},${h - f} Z`;
  }

  // Bölmeler dış ölçü üzerinden oranlanır; eski kayıtlar da aynı şekilde çizilir.
  function walk(node, x, y, w, h, path, onLeaf, onBar) {
    if (!node.split) { onLeaf(node, x, y, w, h, path); return; }
    const sum = node.sizes.reduce((a, b) => a + b, 0) || 1;
    let offset = 0;
    node.children.forEach((child, i) => {
      const share = node.sizes[i] / sum;
      const next = path.concat(i);
      if (node.split === "v") {
        const size = w * share;
        walk(child, x + offset, y, size, h, next, onLeaf, onBar);
        offset += size;
        if (i < node.children.length - 1) onBar(node, x + offset, y, 0, h, path, i);
      } else {
        const size = h * share;
        walk(child, x, y + offset, w, size, next, onLeaf, onBar);
        offset += size;
        if (i < node.children.length - 1) onBar(node, x, y + offset, w, 0, path, i);
      }
    });
  }

  function symbol(g, opening, x, y, w, h, unit) {
    const line = { fill: "none", stroke: DARK, "stroke-width": unit * 0.22 };
    const dashed = { ...line, "stroke-dasharray": `${unit} ${unit * 0.7}` };
    const cx = x + w / 2, cy = y + h / 2, r = x + w, b = y + h;
    const left = `M${r},${y} L${x},${cy} L${r},${b}`, right = `M${x},${y} L${r},${cy} L${x},${b}`, tilt = `M${x},${y} L${cx},${b} L${r},${y}`;
    const arrow = (dir) => {
      const span = Math.min(w * 0.3, unit * 16), head = unit * 3;
      el(g, "path", { ...line, d: `M${cx - dir * span},${cy} L${cx + dir * span},${cy} M${cx + dir * (span - head)},${cy - head} L${cx + dir * span},${cy} L${cx + dir * (span - head)},${cy + head}` });
    };
    switch (opening) {
      case "left": el(g, "path", { ...line, d: left }); break;
      case "right": el(g, "path", { ...line, d: right }); break;
      case "tilt": el(g, "path", { ...line, d: tilt }); break;
      case "tiltLeft": el(g, "path", { ...line, d: left }); el(g, "path", { ...dashed, d: tilt }); break;
      case "tiltRight": el(g, "path", { ...line, d: right }); el(g, "path", { ...dashed, d: tilt }); break;
      case "slideLeft": arrow(-1); break;
      case "slideRight": arrow(1); break;
      case "doorLeft": el(g, "path", { ...line, "stroke-width": unit * 0.34, d: left }); break;
      case "doorRight": el(g, "path", { ...line, "stroke-width": unit * 0.34, d: right }); break;
    }
    const handleSide = { left: r, tiltLeft: r, doorLeft: r, right: x, tiltRight: x, doorRight: x }[opening];
    if (handleSide !== undefined)
      el(g, "rect", { x: handleSide - unit * 0.6, y: cy - unit * 2.2, width: unit * 1.2, height: unit * 4.4, rx: unit * 0.4, fill: DARK });
  }

  function dimension(g, x1, y1, x2, y2, text, unit, offset, scale) {
    const horizontal = y1 === y2;
    const ax1 = horizontal ? x1 : x1 + offset, ay1 = horizontal ? y1 + offset : y1;
    const ax2 = horizontal ? x2 : x2 + offset, ay2 = horizontal ? y2 + offset : y2;
    const stroke = { stroke: "#5b6872", "stroke-width": unit * 0.12 };
    el(g, "line", { ...stroke, x1: ax1, y1: ay1, x2: ax2, y2: ay2 });
    const tick = unit * 1.1;
    [[ax1, ay1], [ax2, ay2]].forEach(([px, py]) => el(g, "line", { ...stroke, x1: px - tick, y1: py + (horizontal ? tick : -tick), x2: px + tick, y2: py + (horizontal ? -tick : tick) }));
    const mx = (ax1 + ax2) / 2, my = (ay1 + ay2) / 2;
    const node = el(g, "text", { x: mx, y: my, "font-size": unit * 2.5 * scale, fill: "#1b252d", "text-anchor": "middle", "font-family": "Manrope, Arial, sans-serif", dy: unit * -0.8 }, text);
    if (!horizontal) node.setAttribute("transform", `rotate(-90 ${mx} ${my})`);
  }

  function start(svg, d, opts) {
    while (svg.firstChild) svg.removeChild(svg.firstChild);
    const unit = Math.max(d.widthMm, d.heightMm) / 100, margin = opts.bare ? unit * 2 : unit * (12.5 + 2.5 * (opts.textScale || 1));
    svg.setAttribute("viewBox", `${-margin} ${-margin} ${d.widthMm + margin * 2} ${d.heightMm + margin * 2}`);
    return unit;
  }

  // Küçük basılan çizimlerde (teklif ve sözleşme ekleri) yazılar textScale ile büyütülür.
  function outerDims(svg, d, unit, opts) {
    const dims = el(svg, "g", { "pointer-events": "none" }), scale = opts.textScale || 1;
    dimension(dims, 0, d.heightMm, d.widthMm, d.heightMm, label(d.widthMm), unit, unit * 9, scale);
    dimension(dims, 0, d.heightMm, 0, 0, label(d.heightMm), unit, unit * -9, scale);
    return dims;
  }

  function renderWindow(svg, d, opts) {
    const f = frameOf(d), w = d.widthMm, h = d.heightMm, unit = start(svg, d, opts);
    const defs = el(svg, "defs");
    const clipId = `wd-inner-${++clipCounter}`; // aynı sayfada birden çok çizim olabilir
    el(el(defs, "clipPath", { id: clipId }), "path", { d: innerPath(d, f) });
    const hairline = { stroke: DARK, "stroke-width": unit * 0.14 };

    el(svg, "path", { d: outerPath(d), fill: d.colorHex, ...hairline });
    const inside = el(svg, "g", { "clip-path": `url(#${clipId})` });
    const bars = [], labels = [];
    const bar = f * 0.7, sash = f * 0.7;

    walk(d.root, 0, 0, w, h, [], (node, x, y, lw, lh, path) => {
      const key = path.join(".");
      const g = el(inside, "g");
      // Bölme dış ölçüdedir; kasa ve kayıt payı görsel olarak içeri alınır.
      const ix = x === 0 ? f : x + bar / 2, iy = y === 0 ? f : y + bar / 2;
      const ir = x + lw >= w - 0.01 ? w - f : x + lw - bar / 2, ib = y + lh >= h - 0.01 ? h - f : y + lh - bar / 2;
      const iw = Math.max(ir - ix, 0), ih = Math.max(ib - iy, 0);
      el(g, "rect", { x: ix, y: iy, width: iw, height: ih, fill: d.colorHex, "data-leaf": key, style: opts.interactive ? "cursor:pointer" : "" });
      const opening = node.opening && node.opening !== "fixed";
      const inset = opening ? sash : 0;
      const gx = ix + inset, gy = iy + inset, gw = iw - 2 * inset, gh = ih - 2 * inset;
      if (opening) el(g, "rect", { x: ix, y: iy, width: iw, height: ih, fill: d.colorHex, ...hairline, "pointer-events": "none" });
      if (gw > 0 && gh > 0) {
        const isPanel = node.fill === "panel";
        el(g, "rect", { x: gx, y: gy, width: gw, height: gh, fill: isPanel ? PANEL : GLASS, ...hairline, "pointer-events": "none" });
        symbol(el(g, "g", { "pointer-events": "none" }), node.opening, gx, gy, gw, gh, unit);
      }
      if (opts.selected === key) el(g, "rect", { x: ix, y: iy, width: iw, height: ih, fill: "rgba(36,99,235,.16)", stroke: "#2463eb", "stroke-width": unit * 0.45, "pointer-events": "none" });
      if (d.root.split) labels.push({ x: ix + iw / 2, y: iy + ih / 2, text: `${label(lw)} × ${label(lh)}`, room: iw, bottom: iy + ih });
    }, (node, x, y, bw, bh, path, index) => bars.push({ node, x, y, bw, bh, path, index }));

    bars.forEach((b) => {
      const vertical = b.node.split === "v";
      if (b.node.sliding) {
        el(inside, "line", { x1: b.x, y1: b.y, x2: b.x, y2: b.y + b.bh, stroke: DARK, "stroke-width": unit * 0.35 });
        // Sürükleme için görünmez tutamak
        el(inside, "rect", { x: b.x - unit, y: b.y, width: unit * 2, height: b.bh, fill: "transparent", "data-bar": b.path.join("."), "data-index": b.index, style: opts.interactive ? "cursor:ew-resize" : "" });
        return;
      }
      el(inside, "rect", {
        x: vertical ? b.x - bar / 2 : b.x, y: vertical ? b.y : b.y - bar / 2, width: vertical ? bar : b.bw, height: vertical ? b.bh : bar,
        fill: d.colorHex, ...hairline, "data-bar": b.path.join("."), "data-index": b.index, style: opts.interactive ? `cursor:${vertical ? "ew-resize" : "ns-resize"}` : ""
      });
    });
    el(svg, "path", { d: innerPath(d, f), fill: "none", ...hairline, "pointer-events": "none" });
    if (opts.bare) return;

    // Her bölmenin ölçüsü üzerinde yazar.
    const texts = el(svg, "g", { "pointer-events": "none" });
    labels.forEach((l) => {
      const size = unit * 2 * (opts.textScale || 1);
      if (l.room < l.text.length * size * 0.56) return; // sığmayan ölçü çizimi kirletmez
      el(texts, "text", { x: l.x, y: Math.min(l.y + size * 2.6, l.bottom - size * 0.5), "font-size": size, fill: "#1b252d", "text-anchor": "middle", "font-family": "Manrope, Arial, sans-serif", "paint-order": "stroke", stroke: "rgba(255,255,255,.85)", "stroke-width": unit * 0.5 }, l.text);
    });
    outerDims(svg, d, unit, opts);
  }

  function renderShutter(svg, d, unit) {
    const w = d.widthMm, h = d.heightMm, box = Math.min(165, h * 0.18), guide = Math.min(30, w * 0.04), lamella = 39;
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    el(svg, "rect", { x: guide, y: box, width: w - 2 * guide, height: h - box, fill: d.colorHex, ...stroke });
    const step = lamella * Math.max(1, Math.ceil((h - box) / (lamella * 40)));
    for (let y = box + step; y < h; y += step) el(svg, "line", { x1: guide, y1: y, x2: w - guide, y2: y, stroke: DARK, "stroke-width": unit * 0.08, opacity: 0.55 });
    [0, w - guide].forEach((x) => el(svg, "rect", { x, y: box, width: guide, height: h - box, fill: d.colorHex, ...stroke }));
    el(svg, "rect", { x: 0, y: 0, width: w, height: box, fill: d.colorHex, ...stroke });
    el(svg, "rect", { x: guide, y: h - unit, width: w - 2 * guide, height: unit, fill: DARK });
    if (d.params?.drive === "motor") el(svg, "text", { x: w / 2, y: box / 2, "font-size": unit * 2.4, "text-anchor": "middle", "dominant-baseline": "middle", fill: DARK, "font-family": "Manrope, Arial, sans-serif" }, "MOTOR");
  }

  function renderRailing(svg, d, unit) {
    const p = d.params || {}, w = d.widthMm, h = d.heightMm;
    const spacing = p.postSpacingMm > 0 ? p.postSpacingMm : 1200;
    const bays = Math.max(1, Math.ceil(w / spacing)), post = Math.min(40, w / (bays * 8)), rail = Math.min(40, h * 0.06);
    const bay = (w - (bays + 1) * post) / bays;
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    for (let i = 0; i < bays; i++) {
      const x = post + i * (bay + post);
      if (p.infill === "bars") {
        const bars = Math.max(1, Math.floor(bay / 110));
        for (let b = 1; b <= bars; b++) { const bx = x + b * bay / (bars + 1); el(svg, "line", { x1: bx, y1: rail, x2: bx, y2: h - rail, ...stroke }); }
      } else if (bay > 0) {
        el(svg, "rect", { x, y: rail, width: bay, height: h - 2 * rail, fill: GLASS, ...stroke });
      }
    }
    for (let i = 0; i <= bays; i++) el(svg, "rect", { x: i * (bay + post), y: 0, width: post, height: h, fill: d.colorHex, ...stroke });
    el(svg, "rect", { x: 0, y: 0, width: w, height: rail, fill: d.colorHex, ...stroke });
    el(svg, "rect", { x: 0, y: h - rail, width: w, height: rail, fill: d.colorHex, ...stroke });
  }

  function renderBalcony(svg, d, unit) {
    const p = d.params || {}, w = d.widthMm, h = d.heightMm, rail = Math.min(40, h * 0.05), count = Math.max(1, p.panelCount || 1);
    const sliding = p.openType !== "folding", panel = w / count, inset = Math.min(30, panel * 0.08);
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    for (let i = 0; i < count; i++) {
      const x = i * panel;
      el(svg, "rect", { x, y: rail, width: panel, height: h - 2 * rail, fill: d.colorHex, ...stroke });
      el(svg, "rect", { x: x + inset, y: rail + inset, width: panel - 2 * inset, height: h - 2 * rail - 2 * inset, fill: GLASS, ...stroke });
      const cx = x + panel / 2, cy = h / 2, a = Math.min(panel * 0.28, unit * 8), head = unit * 1.8;
      if (sliding) {
        const dir = i < count / 2 ? 1 : -1;
        el(svg, "path", { fill: "none", ...stroke, d: `M${cx - dir * a},${cy} L${cx + dir * a},${cy} M${cx + dir * (a - head)},${cy - head} L${cx + dir * a},${cy} L${cx + dir * (a - head)},${cy + head}` });
      } else {
        el(svg, "path", { fill: "none", ...stroke, d: `M${x + inset},${rail + inset} L${x + panel - inset},${cy} L${x + inset},${h - rail - inset}` });
      }
    }
    el(svg, "rect", { x: 0, y: 0, width: w, height: rail, fill: d.colorHex, ...stroke });
    el(svg, "rect", { x: 0, y: h - rail, width: w, height: rail, fill: d.colorHex, ...stroke });
  }

  function render(svg, input, options) {
    const opts = options || {};
    if (!input.widthMm || !input.heightMm || input.widthMm <= 0 || input.heightMm <= 0) return;
    if (input.category && !["aluminum", "pvc"].includes(input.category)) {
      const unit = start(svg, input, opts);
      if (input.category === "shutter") renderShutter(svg, input, unit);
      else if (input.category === "railing") renderRailing(svg, input, unit);
      else renderBalcony(svg, input, unit);
      if (!opts.bare) outerDims(svg, input, unit, opts);
      return;
    }
    // Geçersiz şekil ölçüsü yazılırken çizim bozulmasın; hata kaydederken bildirilir.
    renderWindow(svg, input.shapeParamMm > 0 ? input : { ...input, shape: "rect" }, opts);
  }

  return { render };
})();
