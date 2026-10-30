// Pencere çizimini SVG olarak üretir. Editör ve imalat sayfası aynı çizimi kullanır.
window.WindowDrawing = (() => {
  const NS = "http://www.w3.org/2000/svg";
  let clipCounter = 0;

  function el(parent, name, attrs, text) {
    const node = document.createElementNS(NS, name);
    Object.entries(attrs || {}).forEach(([key, value]) => node.setAttribute(key, value));
    if (text !== undefined) node.textContent = text;
    if (parent) parent.appendChild(node);
    return node;
  }

  function archRadius(d) { return (d.widthMm * d.widthMm / 4 + d.shapeParamMm * d.shapeParamMm) / (2 * d.shapeParamMm); }

  // Sunucudaki WindowCalculator.TopAt ile aynı geometri.
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
      const r = archRadius(d), ri = r - f, dx = w / 2 - f;
      const y = r - Math.sqrt(Math.max(0, ri * ri - dx * dx));
      return `M${f},${h - f} L${w - f},${h - f} L${w - f},${y} A${ri},${ri} 0 0 0 ${f},${y} Z`;
    }
    if (d.shape === "slopeL" || d.shape === "slopeR")
      return `M${f},${h - f} L${w - f},${h - f} L${w - f},${topAt(d, w - f) + f} L${f},${topAt(d, f) + f} Z`;
    return `M${f},${f} L${w - f},${f} L${w - f},${h - f} L${f},${h - f} Z`;
  }

  // Ağacı gezer; her yaprak ve kayıt için geri çağırım yapar.
  function walk(node, x, y, w, h, sys, path, onLeaf, onBar) {
    if (!node.split) { onLeaf(node, x, y, w, h, path); return; }
    const bar = node.sliding ? 0 : sys.mullion;
    let offset = 0;
    node.children.forEach((child, i) => {
      const size = node.sizes[i], next = path.concat(i);
      if (node.split === "v") {
        walk(child, x + offset, y, size, h, sys, next, onLeaf, onBar);
        if (i < node.children.length - 1) onBar(node, x + offset + size, y, bar, h, path, i);
      } else {
        walk(child, x, y + offset, w, size, sys, next, onLeaf, onBar);
        if (i < node.children.length - 1) onBar(node, x, y + offset + size, w, bar, path, i);
      }
      offset += size + bar;
    });
  }

  function symbol(g, opening, x, y, w, h, unit) {
    const line = { fill: "none", stroke: "#26313a", "stroke-width": unit * 0.22 };
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
      el(g, "rect", { x: handleSide - unit * 0.6, y: cy - unit * 2.2, width: unit * 1.2, height: unit * 4.4, rx: unit * 0.4, fill: "#26313a" });
  }

  function dimension(g, x1, y1, x2, y2, label, unit, offset) {
    const horizontal = y1 === y2;
    const ax1 = horizontal ? x1 : x1 + offset, ay1 = horizontal ? y1 + offset : y1;
    const ax2 = horizontal ? x2 : x2 + offset, ay2 = horizontal ? y2 + offset : y2;
    const stroke = { stroke: "#5b6872", "stroke-width": unit * 0.12 };
    el(g, "line", { ...stroke, x1: ax1, y1: ay1, x2: ax2, y2: ay2 });
    const tick = unit * 1.1;
    [[ax1, ay1], [ax2, ay2]].forEach(([px, py]) => el(g, "line", { ...stroke, x1: px - tick, y1: py + (horizontal ? tick : -tick), x2: px + tick, y2: py + (horizontal ? -tick : tick) }));
    const mx = (ax1 + ax2) / 2, my = (ay1 + ay2) / 2;
    const text = el(g, "text", { x: mx, y: my, "font-size": unit * 2.5, fill: "#1b252d", "text-anchor": "middle", "font-family": "Inter, Arial, sans-serif", dy: unit * -0.8 }, label);
    if (!horizontal) text.setAttribute("transform", `rotate(-90 ${mx} ${my})`);
  }

  // Ölçü yazıları kullanıcının seçtiği birimde gösterilir.
  function round(value) { return window.Units ? Units.format(value) : Math.round(value * 10) / 10; }

  // design: { widthMm, heightMm, shape, shapeParamMm, colorHex, root, sys: { frame, sash, mullion, overlap } }
  function render(svg, design, options) {
    const opts = options || {};
    if (design.category && !["aluminum", "pvc"].includes(design.category)) { renderProduct(svg, design, opts); return; }
    const sys = design.sys, f = sys.frame, w = design.widthMm, h = design.heightMm;
    // Geçersiz şekil ölçüsü yazılırken çizim bozulmasın; hata mesajını sunucu verir.
    const d = design.shapeParamMm > 0 ? design : { ...design, shape: "rect" };
    while (svg.firstChild) svg.removeChild(svg.firstChild);
    const unit = Math.max(w, h) / 100, margin = opts.bare ? unit * 2 : unit * 15;
    svg.setAttribute("viewBox", `${-margin} ${-margin} ${w + margin * 2} ${h + margin * 2}`);

    const defs = el(svg, "defs");
    const clipId = `wd-inner-${++clipCounter}`; // aynı sayfada birden çok çizim olabilir
    const clip = el(defs, "clipPath", { id: clipId });
    el(clip, "path", { d: innerPath(d, f) });
    const hairline = unit * 0.14, frameStroke = { stroke: "#26313a", "stroke-width": hairline };

    el(svg, "path", { d: outerPath(d), fill: d.colorHex, ...frameStroke });
    const inside = el(svg, "g", { "clip-path": `url(#${clipId})` });
    const bars = [];
    walk(d.root, f, f, w - 2 * f, h - 2 * f, sys, [], (node, x, y, lw, lh, path) => {
      const g = el(inside, "g");
      const pathKey = path.join(".");
      const isOpening = node.opening && node.opening !== "fixed";
      const fx = isOpening ? x - sys.overlap + sys.sash : x, fy = isOpening ? y - sys.overlap + sys.sash : y;
      const fw = isOpening ? lw + 2 * sys.overlap - 2 * sys.sash : lw, fh = isOpening ? lh + 2 * sys.overlap - 2 * sys.sash : lh;
      const hit = el(g, "rect", { x, y, width: lw, height: lh, fill: d.colorHex, "data-leaf": pathKey, style: opts.interactive ? "cursor:pointer" : "" });
      if (isOpening) el(g, "rect", { x: x - sys.overlap, y: y - sys.overlap, width: lw + 2 * sys.overlap, height: lh + 2 * sys.overlap, fill: d.colorHex, ...frameStroke, "pointer-events": "none" });
      if (fw > 0 && fh > 0) {
        const isPanel = node.fill === "panel";
        el(g, "rect", { x: fx, y: fy, width: fw, height: fh, fill: isPanel ? "#d4d8db" : "#cfe4f0", ...frameStroke, "pointer-events": "none" });
        if (!isPanel) el(g, "path", { d: `M${fx + fw * 0.12},${fy + fh * 0.3} L${fx + fw * 0.3},${fy + fh * 0.1} M${fx + fw * 0.12},${fy + fh * 0.42} L${fx + fw * 0.42},${fy + fh * 0.1}`, stroke: "#fff", "stroke-width": unit * 0.3, opacity: 0.85, "pointer-events": "none" });
        const symbols = el(g, "g", { "pointer-events": "none" });
        symbol(symbols, node.opening, fx, fy, fw, fh, unit);
      }
      if (opts.selected === pathKey) el(g, "rect", { x, y, width: lw, height: lh, fill: "rgba(36,99,235,.16)", stroke: "#2463eb", "stroke-width": unit * 0.45, "pointer-events": "none" });
      hit.dataset.leaf = pathKey;
    }, (node, x, y, bw, bh, path, index) => bars.push({ node, x, y, bw, bh, path, index }));

    bars.forEach((b) => {
      if (b.node.sliding) {
        el(inside, "line", { x1: b.x, y1: b.y, x2: b.x, y2: b.y + b.bh, stroke: "#26313a", "stroke-width": unit * 0.35 });
        return;
      }
      el(inside, "rect", {
        x: b.x, y: b.y, width: b.bw, height: b.bh, fill: d.colorHex, ...frameStroke,
        "data-bar": b.path.join("."), "data-index": b.index, style: opts.interactive ? `cursor:${b.node.split === "v" ? "ew-resize" : "ns-resize"}` : ""
      });
    });
    el(svg, "path", { d: innerPath(d, f), fill: "none", ...frameStroke, "pointer-events": "none" });

    if (opts.bare) return;
    const dims = el(svg, "g", { "pointer-events": "none" });
    dimension(dims, 0, h, w, h, `${round(w)}`, unit, unit * 9);
    dimension(dims, 0, h, 0, 0, `${round(h)}`, unit, unit * -9);
    const root = d.root;
    if (root.split === "v") {
      let x = f;
      root.sizes.forEach((size) => { dimension(dims, x, 0, x + size, 0, `${round(size)}`, unit, unit * -5); x += size + (root.sliding ? 0 : sys.mullion); });
    } else if (root.split === "h") {
      let y = f;
      root.sizes.forEach((size) => { dimension(dims, w, y + size, w, y, `${round(size)}`, unit, unit * 7); y += size + sys.mullion; });
    }
  }

  const DARK = "#26313a", GLASS = "#cfe4f0";

  function startProduct(svg, d, opts) {
    while (svg.firstChild) svg.removeChild(svg.firstChild);
    const unit = Math.max(d.widthMm, d.heightMm) / 100, margin = opts.bare ? unit * 2 : unit * 15;
    svg.setAttribute("viewBox", `${-margin} ${-margin} ${d.widthMm + margin * 2} ${d.heightMm + margin * 2}`);
    return unit;
  }

  function outerDims(svg, d, unit, opts) {
    if (opts.bare) return;
    const dims = el(svg, "g", { "pointer-events": "none" });
    dimension(dims, 0, d.heightMm, d.widthMm, d.heightMm, `${round(d.widthMm)}`, unit, unit * 9);
    dimension(dims, 0, d.heightMm, 0, 0, `${round(d.heightMm)}`, unit, unit * -9);
  }

  function renderShutter(svg, d, unit) {
    const s = d.settings, w = d.widthMm, h = d.heightMm, box = Math.min(s.boxHeightMm, h), guide = Math.max(s.guideDeductMm, unit);
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    el(svg, "rect", { x: guide, y: box, width: w - 2 * guide, height: h - box, fill: d.colorHex, ...stroke });
    const step = s.lamellaHeightMm * Math.max(1, Math.ceil((h - box) / (s.lamellaHeightMm * 40)));
    for (let y = box + step; y < h; y += step) el(svg, "line", { x1: guide, y1: y, x2: w - guide, y2: y, stroke: DARK, "stroke-width": unit * 0.08, opacity: 0.55 });
    [0, w - guide].forEach((x) => el(svg, "rect", { x, y: box, width: guide, height: h - box, fill: d.colorHex, ...stroke }));
    el(svg, "rect", { x: 0, y: 0, width: w, height: box, fill: d.colorHex, ...stroke });
    el(svg, "rect", { x: guide, y: h - unit, width: w - 2 * guide, height: unit, fill: DARK });
    if (d.params?.drive === "motor") el(svg, "text", { x: w / 2, y: box / 2, "font-size": unit * 2.4, "text-anchor": "middle", "dominant-baseline": "middle", fill: DARK, "font-family": "Inter, Arial, sans-serif" }, "MOTOR");
  }

  function renderRailing(svg, d, unit) {
    const s = d.settings, p = d.params || {}, w = d.widthMm, h = d.heightMm;
    const spacing = p.postSpacingMm > 0 ? p.postSpacingMm : s.postSpacingMm;
    const bays = Math.max(1, Math.ceil(w / spacing)), post = s.postWidthMm, rail = s.railHeightMm;
    const bay = (w - (bays + 1) * post) / bays;
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    for (let i = 0; i < bays; i++) {
      const x = post + i * (bay + post);
      if (p.infill === "bars") {
        const bars = Math.max(1, Math.floor(bay / s.barSpacingMm));
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
    const s = d.settings, p = d.params || {}, w = d.widthMm, h = d.heightMm, rail = s.railHeightMm, count = Math.max(1, p.panelCount || 1);
    const sliding = p.openType !== "folding", overlap = sliding ? s.panelOverlapMm : 0, panel = (w + overlap * (count - 1)) / count;
    const stroke = { stroke: DARK, "stroke-width": unit * 0.14 };
    const inset = d.sys.sash > panel / 3 ? panel / 10 : d.sys.sash;
    for (let i = 0; i < count; i++) {
      const x = i * (panel - overlap);
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

  function renderProduct(svg, d, opts) {
    const unit = startProduct(svg, d, opts);
    if (d.category === "shutter") renderShutter(svg, d, unit);
    else if (d.category === "railing") renderRailing(svg, d, unit);
    else renderBalcony(svg, d, unit);
    outerDims(svg, d, unit, opts);
  }

  return { render };
})();
