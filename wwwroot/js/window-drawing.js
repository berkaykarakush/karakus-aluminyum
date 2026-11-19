// Ölçülü poz çizimi (Ercom tarzı). Editör, çizim listesi, teklif ve sözleşme ekleri aynı çizimi kullanır.
// Kasa, kayıt ve kanat profil genişlikleriyle çizilir; cam ölçüleri imalat hesabıyla (WindowCalculator) aynı kuralla bulunur.
// Ara ölçüler siyah, toplam ölçüler kırmızı oklu yazılır. opts.scale verilirse çizim kâğıtta sabit ölçekle basılır.
window.WindowDrawing = (() => {
  const NS = "http://www.w3.org/2000/svg";
  const INK = "#1f2a33", RED = "#d3202a", GLASS = "#c8f1fb", PANEL = "#d4d8db", HINGE = "#e3b505";
  // Sistemsiz çizimde varsayılan profil sistemi değerleri (ProfileSystem varsayılanları).
  const DEFAULT_PROFILE = { frame: 70, sash: 80, mullion: 80, overlap: 8, clearance: 5 };
  let clipCounter = 0;

  function el(parent, name, attrs, text) {
    const node = document.createElementNS(NS, name);
    Object.entries(attrs || {}).forEach(([key, value]) => node.setAttribute(key, value));
    if (text !== undefined) node.textContent = text;
    if (parent) parent.appendChild(node);
    return node;
  }

  // Çizimde tüm ölçüler tam milimetre yazılır (Ercom gibi: "498x1283", "1200").
  const label = (mm) => String(Math.round(mm));
  const archRadius = (d) => (d.widthMm * d.widthMm / 4 + d.shapeParamMm * d.shapeParamMm) / (2 * d.shapeParamMm);
  const FONT = "Arial, Helvetica, sans-serif";

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

  // WindowCalculator.Walk ile aynı: bölmeler dış ölçüde oranlanır, kayıt payı sınırın iki yanından düşülür.
  // visit.leaf(node, x, y, genişlik, yükseklik, yol): kasa ve kayıt payı düşülmüş net bölme
  // visit.bar(node, sınır, karşı eksende başlangıç, uzunluk, yol, sıra, kayıt genişliği)
  function walk(node, x, y, w, h, ox, oy, ow, oh, path, profile, visit) {
    if (!node.split) { visit.leaf(node, x, y, w, h, path); return; }
    const vertical = node.split === "v", bar = node.sliding ? 0 : profile.mullion;
    const sum = node.sizes.reduce((a, b) => a + b, 0) || 1;
    const [start, span] = vertical ? [ox, ow] : [oy, oh];
    const [clearStart, clearEnd] = vertical ? [x, x + w] : [y, y + h];
    let used = 0;
    node.children.forEach((child, i) => {
      const last = i === node.children.length - 1;
      const partStart = start + span * used / sum;
      used += node.sizes[i];
      const partEnd = start + span * used / sum;
      const from = i === 0 ? clearStart : partStart + bar / 2, to = last ? clearEnd : partEnd - bar / 2;
      const next = path.concat(i);
      if (vertical) walk(child, from, y, to - from, h, partStart, oy, partEnd - partStart, oh, next, profile, visit);
      else walk(child, x, from, w, to - from, ox, partStart, ow, partEnd - partStart, next, profile, visit);
      if (!last) visit.bar(node, partEnd, vertical ? y : x, vertical ? h : w, path, i, bar);
    });
  }

  // Ölçü zinciri için bölme sınırları (dış ölçü üzerinde).
  function boundaries(node, ox, oy, ow, oh, xs, ys) {
    if (!node.split) return;
    const vertical = node.split === "v", sum = node.sizes.reduce((a, b) => a + b, 0) || 1;
    let used = 0;
    node.children.forEach((child, i) => {
      const s = (vertical ? ow : oh) * used / sum;
      used += node.sizes[i];
      const e = (vertical ? ow : oh) * used / sum;
      if (vertical) { boundaries(child, ox + s, oy, e - s, oh, xs, ys); if (i < node.children.length - 1) xs.push(ox + e); }
      else { boundaries(child, ox, oy + s, ow, e - s, xs, ys); if (i < node.children.length - 1) ys.push(oy + e); }
    });
  }

  const uniq = (values) => values.sort((a, b) => a - b).filter((v, i, all) => i === 0 || v - all[i - 1] > 1);

  function arrowHead(g, x, y, dx, dy, size, color) {
    const nx = -dy, ny = dx;
    el(g, "path", { d: `M${x},${y} L${x - dx * size + nx * size * 0.35},${y - dy * size + ny * size * 0.35} L${x - dx * size - nx * size * 0.35},${y - dy * size - ny * size * 0.35} Z`, fill: color });
  }

  // Bir ölçü zinciri: points eksen üzerindeki konumlar, along = "x" (alt) veya "y" (sağ/sol), at = çizginin karşı eksendeki konumu.
  function chain(g, points, along, at, k, total, color) {
    const horizontal = along === "x", font = (total ? 2.9 : 2.5) * k, stroke = { stroke: color, "stroke-width": 0.18 * k };
    const P = (a, b) => (horizontal ? [a, b] : [b, a]);
    const [x1, y1] = P(points[0], at), [x2, y2] = P(points[points.length - 1], at);
    el(g, "line", { ...stroke, x1, y1, x2, y2 });
    points.forEach((p, i) => {
      if (total) {
        const [px, py] = P(p, at), dir = i === 0 ? -1 : 1;
        if (i === 0 || i === points.length - 1) arrowHead(g, px, py, horizontal ? dir : 0, horizontal ? 0 : dir, 1.6 * k, color);
      } else {
        const tick = 1.2 * k, [a1, b1] = P(p, at - tick), [a2, b2] = P(p, at + tick);
        el(g, "line", { ...stroke, x1: a1, y1: b1, x2: a2, y2: b2 });
      }
    });
    for (let i = 0; i < points.length - 1; i++) {
      const mid = (points[i] + points[i + 1]) / 2, text = label(points[i + 1] - points[i]);
      if (Math.abs(points[i + 1] - points[i]) < text.length * font * 0.5) continue; // sığmayan ara ölçü yazılmaz
      const [tx, ty] = P(mid, at);
      const node = el(g, "text", {
        x: tx, y: ty, "font-size": font, "font-family": FONT, "font-weight": total ? 700 : 400, fill: color, "text-anchor": "middle", "dominant-baseline": "central",
        "paint-order": "stroke", stroke: "#fff", "stroke-width": 1.4 * k, "stroke-linejoin": "round"
      }, text);
      if (!horizontal) node.setAttribute("transform", `rotate(-90 ${tx} ${ty})`);
    }
  }

  function dimensions(svg, d, k, opts, xs, ys) {
    const g = el(svg, "g", { "pointer-events": "none" }), w = d.widthMm, h = d.heightMm, show = opts.show;
    const color = opts.mono ? INK : RED;
    let below = h + 5 * k, right = w + 5 * k;
    if (show.inner && xs.length) { chain(g, [0, ...xs, w], "x", below, k, false, INK); below += 6 * k; }
    if (show.inner && ys.length) { chain(g, [0, ...ys, h], "y", right, k, false, INK); right += 6 * k; }
    if (show.outer) { chain(g, [0, w], "x", below, k, true, color); chain(g, [0, h], "y", right, k, true, color); }
    // Eğimli üstte alçak kenarın ölçüsü yan tarafta ayrıca yazılır.
    if (show.inner && d.shape === "slopeL" && d.shapeParamMm > 0) chain(g, [h - d.shapeParamMm, h], "y", -5 * k, k, false, INK);
    if (show.inner && d.shape === "slopeR" && d.shapeParamMm > 0) chain(g, [h - d.shapeParamMm, h], "y", w + 5 * k, k, false, INK);
  }

  // Açılım sembolü kanadın camı üzerine çizilir: çizgiler menteşe tarafında birleşir.
  function symbol(g, opening, x, y, w, h, k, color) {
    const line = { fill: "none", stroke: color, "stroke-width": 0.22 * k, "stroke-linejoin": "round" };
    const dashed = { ...line, "stroke-dasharray": `${1.4 * k} ${1 * k}` };
    const cx = x + w / 2, cy = y + h / 2, r = x + w, b = y + h;
    const left = `M${r},${y} L${x},${cy} L${r},${b}`, right = `M${x},${y} L${r},${cy} L${x},${b}`, tilt = `M${x},${y} L${cx},${b} L${r},${y}`;
    const arrow = (dir) => {
      const span = Math.min(w * 0.22, 9 * k), head = 1.6 * k;
      el(g, "line", { ...line, x1: cx - dir * span, y1: cy, x2: cx + dir * span, y2: cy });
      arrowHead(g, cx + dir * span, cy, dir, 0, head, color);
    };
    switch (opening) {
      case "left": el(g, "path", { ...line, d: left }); break;
      case "right": el(g, "path", { ...line, d: right }); break;
      case "tilt": el(g, "path", { ...line, d: tilt }); break;
      case "tiltLeft": el(g, "path", { ...line, d: left }); el(g, "path", { ...dashed, d: tilt }); break;
      case "tiltRight": el(g, "path", { ...line, d: right }); el(g, "path", { ...dashed, d: tilt }); break;
      case "slideLeft": arrow(-1); break;
      case "slideRight": arrow(1); break;
      case "doorLeft": el(g, "path", { ...line, "stroke-width": 0.3 * k, d: left }); break;
      case "doorRight": el(g, "path", { ...line, "stroke-width": 0.3 * k, d: right }); break;
    }
  }

  // Kol ve menteşe işaretleri kanat profilinin üzerine konur.
  function hardware(g, opening, sx, sy, sw, sh, sash, k, mono) {
    const hingeLeft = ["left", "tiltLeft", "doorLeft"].includes(opening), hingeRight = ["right", "tiltRight", "doorRight"].includes(opening);
    const mid = sash / 2, len = Math.min(sh * 0.12, 4 * k), thick = Math.min(sash * 0.45, 1.2 * k);
    if (hingeLeft || hingeRight) {
      const hx = hingeLeft ? sx + mid : sx + sw - mid;
      [sy + sash + len, sy + sh - sash - 2 * len].forEach((hy) => el(g, "rect", { x: hx - thick / 2, y: hy, width: thick, height: len, fill: mono ? "#fff" : HINGE, stroke: INK, "stroke-width": 0.1 * k }));
      const handleX = hingeLeft ? sx + sw - mid : sx + mid, handleY = sy + sh / 2;
      el(g, "rect", { x: handleX - thick * 0.6, y: handleY - len * 0.7, width: thick * 1.2, height: len * 1.4, rx: thick * 0.3, fill: mono ? "#fff" : "#9bc53d", stroke: INK, "stroke-width": 0.12 * k });
    } else if (opening === "tilt") {
      const handleX = sx + sw / 2, handleY = sy + mid;
      el(g, "rect", { x: handleX - len * 0.7, y: handleY - thick * 0.6, width: len * 1.4, height: thick * 1.2, rx: thick * 0.3, fill: mono ? "#fff" : "#9bc53d", stroke: INK, "stroke-width": 0.12 * k });
    }
  }

  // Cam ölçüsü ve cam tipi camın alt kısmında yazar (Ercom'daki "571x1221 / 4+16+4").
  function glassLabel(g, gx, gy, gw, gh, text, spec, k) {
    for (const size of [2.3 * k, 1.7 * k]) {
      const widest = Math.max(text.length, spec ? spec.length : 0) * size * 0.56, lines = spec ? 2 : 1;
      if (widest > gw * 0.94 || lines * size * 1.25 > gh * 0.8) continue;
      const baseY = gy + gh - size * (lines === 2 ? 1.9 : 0.8);
      const attrs = { "font-size": size, "font-family": FONT, fill: INK, "text-anchor": "middle", "pointer-events": "none" };
      el(g, "text", { ...attrs, x: gx + gw / 2, y: baseY }, text);
      if (spec) el(g, "text", { ...attrs, x: gx + gw / 2, y: baseY + size * 1.15 }, spec);
      return;
    }
  }

  function setup(svg, d, opts, extra) {
    while (svg.firstChild) svg.removeChild(svg.firstChild);
    const w = d.widthMm, h = d.heightMm;
    // k: kâğıttaki 1 mm'nin çizimdeki karşılığı. Ölçek verilmezse (editör tuvali) çizim ~110 mm düşünülür.
    const k = opts.scale ? 1 / opts.scale : Math.max(w, h) / 110;
    const m = opts.bare ? { l: k, t: k, r: k, b: k } : { l: (extra.left ? 10 : 2) * k, t: 2 * k, r: (8 + 6 * extra.rights) * k, b: (8 + 6 * extra.belows) * k };
    const vbW = w + m.l + m.r, vbH = h + m.t + m.b;
    svg.setAttribute("viewBox", `${-m.l} ${-m.t} ${vbW} ${vbH}`);
    if (opts.scale) { svg.setAttribute("width", `${vbW * opts.scale}mm`); svg.setAttribute("height", `${vbH * opts.scale}mm`); }
    else { svg.removeAttribute("width"); svg.removeAttribute("height"); }
    return k;
  }

  function renderWindow(svg, d, opts) {
    const w = d.widthMm, h = d.heightMm, show = opts.show, mono = opts.mono;
    const base = { ...DEFAULT_PROFILE, ...(d.profile || {}) };
    // Çok küçük çizimde kasa görsel olarak sınırlanır; ölçü ve cam hesabı gerçek değerle yapılır.
    const f = Math.min(base.frame, Math.min(w, h) * 0.2);
    const profile = { ...base, frame: f };
    const rawX = [], rawY = [];
    if (d.root) boundaries(d.root, 0, 0, w, h, rawX, rawY);
    if (d.shape === "arch" && d.shapeParamMm > 0) rawY.push(d.shapeParamMm);
    const xs = uniq(rawX), ys = uniq(rawY);
    const showInner = show.inner && !opts.bare;
    const k = setup(svg, d, opts, { rights: showInner && ys.length ? 1 : 0, belows: showInner && xs.length ? 1 : 0, left: showInner && d.shape === "slopeL" });
    const color = mono ? "#ffffff" : d.colorHex, glassFill = mono ? "#ffffff" : GLASS, symbolColor = mono ? INK : RED;
    const hair = { stroke: INK, "stroke-width": 0.16 * k };

    const defs = el(svg, "defs"), clipId = `wd-inner-${++clipCounter}`; // aynı sayfada birden çok çizim olabilir
    el(el(defs, "clipPath", { id: clipId }), "path", { d: innerPath(d, f) });
    el(svg, "path", { d: outerPath(d), fill: color, ...hair });
    if (d.shape === "rect") [[0, 0, f, f], [w, 0, w - f, f], [0, h, f, h - f], [w, h, w - f, h - f]].forEach(([x1, y1, x2, y2]) => el(svg, "line", { x1, y1, x2, y2, ...hair }));
    const inside = el(svg, "g", { "clip-path": `url(#${clipId})` });
    const bars = el(inside, "g"), leaves = el(inside, "g"), hits = el(svg, "g");
    if (!d.root) return;

    walk(d.root, f, f, w - 2 * f, h - 2 * f, 0, 0, w, h, [], profile, {
      bar(node, center, from, length, path, index, bar) {
        const vertical = node.split === "v", key = path.join(".");
        if (bar > 0) el(bars, "rect", vertical ? { x: center - bar / 2, y: from, width: bar, height: length, fill: color, ...hair } : { x: from, y: center - bar / 2, width: length, height: bar, fill: color, ...hair });
        // Sürükleme tutamağı: sürmede kayıt yoktur, yine de bölme ölçüsü çekilerek değiştirilebilir.
        if (opts.interactive) {
          const grip = Math.max(bar, 3 * k);
          const handle = el(hits, "rect", vertical ? { x: center - grip / 2, y: from, width: grip, height: length } : { x: from, y: center - grip / 2, width: length, height: grip });
          handle.setAttribute("fill", "transparent");
          handle.dataset.bar = key; handle.dataset.index = index; handle.style.cursor = vertical ? "ew-resize" : "ns-resize";
        }
      },
      leaf(node, x, y, lw, lh, path) {
        const key = path.join("."), g = el(leaves, "g");
        const opening = node.opening && node.opening !== "fixed";
        // Kemer veya eğim bölgesindeki sabit cam şekle göre kısalır (imalat hesabındaki gibi).
        const boundaryTop = Math.min(topAt(d, x), topAt(d, x + lw), topAt(d, x + lw / 2)) + f;
        const clipped = topAt(d, x) + f > y + 0.01 || topAt(d, x + lw) + f > y + 0.01;
        let gx = x, gy = y, gw = lw, gh = lh;
        let glassW = lw - 2 * profile.clearance, glassH = (clipped ? y + lh - Math.max(y, boundaryTop) : lh) - 2 * profile.clearance;
        if (opening) {
          const sx = x - profile.overlap, sy = y - profile.overlap, sw = lw + 2 * profile.overlap, sh = lh + 2 * profile.overlap, s = Math.min(profile.sash, sw * 0.3, sh * 0.3);
          el(g, "rect", { x: sx, y: sy, width: sw, height: sh, fill: color, ...hair });
          [[sx, sy, sx + s, sy + s], [sx + sw, sy, sx + sw - s, sy + s], [sx, sy + sh, sx + s, sy + sh - s], [sx + sw, sy + sh, sx + sw - s, sy + sh - s]].forEach(([x1, y1, x2, y2]) => el(g, "line", { x1, y1, x2, y2, ...hair }));
          gx = sx + s; gy = sy + s; gw = sw - 2 * s; gh = sh - 2 * s;
          glassW = sw - 2 * profile.sash - 2 * profile.clearance;
          glassH = sh - 2 * profile.sash - 2 * profile.clearance;
          hardware(g, node.opening, sx, sy, sw, sh, s, k, mono);
        }
        if (gw > 0 && gh > 0) {
          const panel = node.fill === "panel";
          el(g, "rect", { x: gx, y: gy, width: gw, height: gh, fill: panel ? (mono ? "#fff" : PANEL) : glassFill, ...hair });
          symbol(el(g, "g"), node.opening, gx, gy, gw, gh, k, symbolColor);
          const top = clipped ? Math.max(gy, boundaryTop) : gy;
          if (show.glass && !opts.bare && glassW > 0 && glassH > 0) glassLabel(g, gx, top, gw, gy + gh - top, `${label(glassW)}x${label(glassH)}`, panel ? "Panel" : d.glass, k);
        }
        if (opts.interactive) {
          const hit = el(hits, "rect", { x, y, width: lw, height: lh, fill: "transparent", style: "cursor:pointer" });
          hit.dataset.leaf = key;
          if (opts.selected === key) el(hits, "rect", { x, y, width: lw, height: lh, fill: "rgba(36,99,235,.14)", stroke: "#2463eb", "stroke-width": 0.6 * k, "pointer-events": "none" });
        }
      }
    });
    // Tutamaklar seçim kutularının üstünde kalsın.
    hits.querySelectorAll("[data-bar]").forEach((bar) => hits.appendChild(bar));
    el(svg, "path", { d: innerPath(d, f), fill: "none", ...hair, "pointer-events": "none" });
    if (!opts.bare) dimensions(svg, d, k, opts, xs, ys);
  }

  function renderShutter(svg, d, k, mono) {
    const w = d.widthMm, h = d.heightMm, box = Math.min(165, h * 0.18), guide = Math.min(30, w * 0.04), lamella = 39;
    const color = mono ? "#fff" : d.colorHex, stroke = { stroke: INK, "stroke-width": 0.16 * k };
    el(svg, "rect", { x: guide, y: box, width: w - 2 * guide, height: h - box, fill: color, ...stroke });
    const step = lamella * Math.max(1, Math.ceil((h - box) / (lamella * 40)));
    const lamellaLine = !mono && isDark(d.colorHex) ? "#c9d0d6" : INK; // koyu panjurda lamel çizgisi açık renk
    for (let y = box + step; y < h; y += step) el(svg, "line", { x1: guide, y1: y, x2: w - guide, y2: y, stroke: lamellaLine, "stroke-width": 0.08 * k, opacity: 0.6 });
    [0, w - guide].forEach((x) => el(svg, "rect", { x, y: box, width: guide, height: h - box, fill: color, ...stroke }));
    el(svg, "rect", { x: 0, y: 0, width: w, height: box, fill: color, ...stroke });
    el(svg, "rect", { x: guide, y: h - 0.6 * k, width: w - 2 * guide, height: 0.6 * k, fill: INK });
    if (d.params?.drive === "motor") el(svg, "text", { x: w / 2, y: box / 2, "font-size": Math.min(2.4 * k, box * 0.6), "text-anchor": "middle", "dominant-baseline": "central", fill: mono || isDark(d.colorHex) ? (mono ? INK : "#fff") : INK, "font-family": FONT }, "MOTOR");
  }

  function renderRailing(svg, d, k, mono) {
    const p = d.params || {}, w = d.widthMm, h = d.heightMm;
    const spacing = p.postSpacingMm > 0 ? p.postSpacingMm : 1200;
    const bays = Math.max(1, Math.ceil(w / spacing)), post = Math.min(40, w / (bays * 8)), rail = Math.min(40, h * 0.06);
    const bay = (w - (bays + 1) * post) / bays, color = mono ? "#fff" : d.colorHex;
    const stroke = { stroke: INK, "stroke-width": 0.16 * k };
    for (let i = 0; i < bays; i++) {
      const x = post + i * (bay + post);
      if (p.infill === "bars") {
        const count = Math.max(1, Math.floor(bay / 110));
        for (let b = 1; b <= count; b++) { const bx = x + b * bay / (count + 1); el(svg, "line", { x1: bx, y1: rail, x2: bx, y2: h - rail, ...stroke }); }
      } else if (bay > 0) {
        el(svg, "rect", { x, y: rail, width: bay, height: h - 2 * rail, fill: mono ? "#fff" : GLASS, ...stroke });
      }
    }
    for (let i = 0; i <= bays; i++) el(svg, "rect", { x: i * (bay + post), y: 0, width: post, height: h, fill: color, ...stroke });
    el(svg, "rect", { x: 0, y: 0, width: w, height: rail, fill: color, ...stroke });
    el(svg, "rect", { x: 0, y: h - rail, width: w, height: rail, fill: color, ...stroke });
    return bays > 1 ? Array.from({ length: bays - 1 }, (_, i) => (i + 1) * (bay + post) + post / 2) : [];
  }

  function renderBalcony(svg, d, k, mono) {
    const p = d.params || {}, w = d.widthMm, h = d.heightMm, rail = Math.min(40, h * 0.05), count = Math.max(1, p.panelCount || 1);
    const sliding = p.openType !== "folding", panel = w / count, inset = Math.min(30, panel * 0.08), color = mono ? "#fff" : d.colorHex;
    const stroke = { stroke: INK, "stroke-width": 0.16 * k }, symbolColor = mono ? INK : RED;
    for (let i = 0; i < count; i++) {
      const x = i * panel;
      el(svg, "rect", { x, y: rail, width: panel, height: h - 2 * rail, fill: color, ...stroke });
      el(svg, "rect", { x: x + inset, y: rail + inset, width: panel - 2 * inset, height: h - 2 * rail - 2 * inset, fill: mono ? "#fff" : GLASS, ...stroke });
      const cx = x + panel / 2, cy = h / 2, a = Math.min(panel * 0.28, 6 * k);
      if (sliding) {
        const dir = i < count / 2 ? 1 : -1;
        el(svg, "line", { x1: cx - dir * a, y1: cy, x2: cx + dir * a, y2: cy, stroke: symbolColor, "stroke-width": 0.22 * k });
        arrowHead(svg, cx + dir * a, cy, dir, 0, 1.6 * k, symbolColor);
      } else {
        el(svg, "path", { fill: "none", stroke: symbolColor, "stroke-width": 0.22 * k, d: `M${x + inset},${rail + inset} L${x + panel - inset},${cy} L${x + inset},${h - rail - inset}` });
      }
    }
    el(svg, "rect", { x: 0, y: 0, width: w, height: rail, fill: color, ...stroke });
    el(svg, "rect", { x: 0, y: h - rail, width: w, height: rail, fill: color, ...stroke });
    return count > 1 ? Array.from({ length: count - 1 }, (_, i) => (i + 1) * panel) : [];
  }

  function isDark(hex) {
    const n = parseInt((hex || "#ffffff").slice(1), 16);
    return ((n >> 16) * 0.299 + ((n >> 8) & 255) * 0.587 + (n & 255) * 0.114) < 110;
  }

  // opts: interactive, selected, bare (ölçüsüz küçük önizleme), scale (kâğıt mm / gerçek mm), mono (siyah-beyaz),
  // show: { inner: ara ölçüler, outer: dış ölçüler, glass: cam ölçüleri }
  function render(svg, input, options) {
    const opts = { ...(options || {}) };
    opts.show = { inner: true, outer: true, glass: true, ...(opts.show || {}) };
    if (!input.widthMm || !input.heightMm || input.widthMm <= 0 || input.heightMm <= 0) return;
    if (input.category && !["aluminum", "pvc"].includes(input.category)) {
      // Panjur, korkuluk ve cam balkon: bölme ölçüleri (dikme / panel) ara ölçü olarak yazılır.
      const parts = input.category === "railing" || input.category === "balcony";
      const k = setup(svg, input, opts, { rights: 0, belows: parts && opts.show.inner && !opts.bare ? 1 : 0, left: false });
      let xs = [];
      if (input.category === "shutter") renderShutter(svg, input, k, opts.mono);
      else if (input.category === "railing") xs = renderRailing(svg, input, k, opts.mono);
      else xs = renderBalcony(svg, input, k, opts.mono);
      if (!opts.bare) dimensions(svg, input, k, opts, xs, []);
      return;
    }
    // Geçersiz şekil ölçüsü yazılırken çizim bozulmasın; hata kaydederken bildirilir.
    renderWindow(svg, input.shapeParamMm > 0 ? input : { ...input, shape: "rect" }, opts);
  }

  // Hazır tipler. Editör ve toplu ekleme aynı tanımları kullanır.
  let idCounter = 0;
  const newId = () => `t${Date.now().toString(36)}${++idCounter}`;
  const leaf = (opening) => ({ id: newId(), opening: opening || "fixed", fill: "glass" });
  const row = (direction, children, sliding, sizes) => ({ id: newId(), split: direction, sliding: !!sliding, sizes: sizes || children.map(() => 1), children });
  const templates = {
    fixed: () => leaf(),
    single: () => leaf("tiltRight"),
    double: () => row("v", [leaf("tiltLeft"), leaf("tiltRight")]),
    triple: () => row("v", [leaf("tiltLeft"), leaf(), leaf("tiltRight")]),
    sliding: () => row("v", [leaf("slideRight"), leaf("slideLeft")], true),
    transom: () => row("h", [leaf("tilt"), row("v", [leaf(), leaf("tiltRight")])], false, [1, 3]),
    door: () => row("h", [leaf(), leaf("doorLeft")], false, [1, 4])
  };

  // Bölmeler orantılı olarak verilen dış ölçüye oturur; toplam her zaman dış ölçüye eşittir.
  function fit(node, w, h) {
    if (!node.split) return;
    const axis = node.split === "v" ? w : h;
    const sum = node.sizes.reduce((a, b) => a + b, 0) || 1;
    let used = 0;
    node.sizes = node.sizes.map((size, i) => {
      if (i === node.sizes.length - 1) return Math.round((axis - used) * 100) / 100;
      const scaled = Math.round(size * axis / sum);
      used += scaled;
      return scaled;
    });
    node.children.forEach((child, i) => fit(child, node.split === "v" ? node.sizes[i] : w, node.split === "v" ? h : node.sizes[i]));
  }

  return { render, templates, fit };
})();
