// Proje gönderisi düzenleyicisi: fotoğraf + etiket ("Referans Projelerimiz" / "Devam Eden Projelerimiz") + proje adı + logo, 1080×1350.
// Önizleme ile kaydedilen görsel aynı tuval üzerinde çizilir; kaydederken tuval JPEG olarak forma eklenir.
(function () {
  "use strict";
  var form = document.querySelector("[data-project-editor]");
  if (!form) return;

  // Alt boşluk logonun (altta 64 px boşluk + yaklaşık 84 px yükseklik) üstünde kalır; yazı logoya binmez.
  var W = 1080, H = 1350, MARGIN_X = 84, MARGIN_TOP = 96, MARGIN_BOTTOM = 190;
  var LABEL = window.SocialProjectEditor.label || "Referans Projelerimiz", FONT = '"SmManrope", "Manrope", "Segoe UI", Arial, sans-serif';
  var canvas = form.querySelector("[data-canvas]"), ctx = canvas.getContext("2d");
  var field = function (name) { return form.querySelector("[data-" + name + "]"); };
  var nameInput = field("name"), scaleInput = field("scale"), photoInput = field("photo-input"), imageInput = field("image");
  var textX = field("text-x"), textY = field("text-y"), focusX = field("focus-x"), focusY = field("focus-y"), empty = form.querySelector("[data-empty]");
  var photo = null, logo = null, block = null, drag = null;

  var num = function (input) { var v = parseFloat(input.value); return isFinite(v) ? v : 0; };
  var set = function (input, v) { input.value = String(Math.round(Math.min(1, Math.max(0, v)) * 10000) / 10000); };
  var load = function (src) {
    return new Promise(function (resolve, reject) { var img = new Image(); img.onload = function () { resolve(img); }; img.onerror = reject; img.src = src; });
  };

  function wrap(text, width) {
    var words = text.split(/\s+/).filter(Boolean), lines = [], line = "";
    words.forEach(function (word) {
      var next = line ? line + " " + word : word;
      if (line && ctx.measureText(next).width > width) { lines.push(line); line = word; } else line = next;
    });
    if (line) lines.push(line);
    return lines.length ? lines : [""];
  }

  // Yazı bloğunun ölçüsü ve yeri; TextX/TextY kenar boşlukları içindeki serbest alana göre 0-1 arasıdır.
  function layout() {
    var s = num(scaleInput) || 1, labelSize = 30 * s, nameSize = 72 * s, gap = 14 * s, lineHeight = nameSize * 1.08;
    var maxWidth = W - 2 * MARGIN_X;
    ctx.font = "800 " + nameSize + "px " + FONT;
    var lines = wrap(nameInput.value.trim() || "Proje adı", maxWidth);
    var width = Math.max.apply(null, lines.map(function (l) { return ctx.measureText(l).width; }));
    ctx.font = "600 " + labelSize + "px " + FONT;
    width = Math.min(maxWidth, Math.max(width, ctx.measureText(LABEL).width));
    var height = labelSize * 1.2 + gap + lines.length * lineHeight;
    var freeX = Math.max(0, W - 2 * MARGIN_X - width), freeY = Math.max(0, H - MARGIN_TOP - MARGIN_BOTTOM - height);
    var x = MARGIN_X + num(textX) * freeX, y = MARGIN_TOP + num(textY) * freeY;
    return { x: x, y: y, width: width, height: height, freeX: freeX, freeY: freeY, lines: lines, labelSize: labelSize, nameSize: nameSize, gap: gap, lineHeight: lineHeight, right: num(textX) > 0.6 };
  }

  function photoRect() {
    var scale = Math.max(W / photo.naturalWidth, H / photo.naturalHeight), dw = photo.naturalWidth * scale, dh = photo.naturalHeight * scale;
    return { x: (W - dw) * num(focusX), y: (H - dh) * num(focusY), w: dw, h: dh, overflowX: W - dw, overflowY: H - dh };
  }

  function draw() {
    ctx.clearRect(0, 0, W, H);
    ctx.fillStyle = "#0d1115";
    ctx.fillRect(0, 0, W, H);
    empty.hidden = !!photo;
    if (photo) { var r = photoRect(); ctx.drawImage(photo, r.x, r.y, r.w, r.h); }
    block = layout();

    // Yazının bulunduğu taraf koyulaştırılır; logo için alt kenar her zaman hafifçe koyulaşır.
    var center = block.y + block.height / 2, shade;
    if (center < H / 2) {
      shade = ctx.createLinearGradient(0, 0, 0, Math.min(H, block.y + block.height + 320));
      shade.addColorStop(0, "rgba(8,11,14,.78)"); shade.addColorStop(0.6, "rgba(8,11,14,.4)"); shade.addColorStop(1, "rgba(8,11,14,0)");
    } else {
      shade = ctx.createLinearGradient(0, H, 0, Math.max(0, block.y - 320));
      shade.addColorStop(0, "rgba(8,11,14,.82)"); shade.addColorStop(0.6, "rgba(8,11,14,.42)"); shade.addColorStop(1, "rgba(8,11,14,0)");
    }
    ctx.fillStyle = shade; ctx.fillRect(0, 0, W, H);
    var bottom = ctx.createLinearGradient(0, H, 0, H * 0.74);
    bottom.addColorStop(0, "rgba(8,11,14,.5)"); bottom.addColorStop(1, "rgba(8,11,14,0)");
    ctx.fillStyle = bottom; ctx.fillRect(0, 0, W, H);

    ctx.save();
    ctx.shadowColor = "rgba(0,0,0,.35)"; ctx.shadowBlur = 24; ctx.textBaseline = "top";
    ctx.textAlign = block.right ? "right" : "left";
    var tx = block.right ? block.x + block.width : block.x;
    ctx.font = "600 " + block.labelSize + "px " + FONT; ctx.fillStyle = "#9cc8e2";
    ctx.fillText(LABEL, tx, block.y);
    ctx.font = "800 " + block.nameSize + "px " + FONT; ctx.fillStyle = "#fff";
    block.lines.forEach(function (line, i) { ctx.fillText(line, tx, block.y + block.labelSize * 1.2 + block.gap + i * block.lineHeight); });
    ctx.restore();

    if (logo) {
      var lw = 270, lh = lw * logo.naturalHeight / logo.naturalWidth;
      ctx.save(); ctx.shadowColor = "rgba(0,0,0,.45)"; ctx.shadowBlur = 14; ctx.shadowOffsetY = 2;
      ctx.drawImage(logo, (W - lw) / 2, H - 64 - lh, lw, lh);
      ctx.restore();
    }
  }

  function point(event) {
    var rect = canvas.getBoundingClientRect();
    return { x: (event.clientX - rect.left) * W / rect.width, y: (event.clientY - rect.top) * H / rect.height };
  }
  var onText = function (p) { return block && p.x >= block.x - 20 && p.x <= block.x + block.width + 20 && p.y >= block.y - 20 && p.y <= block.y + block.height + 20; };

  canvas.addEventListener("pointerdown", function (event) {
    var p = point(event);
    if (onText(p)) drag = { mode: "text", dx: p.x - block.x, dy: p.y - block.y };
    else if (photo) drag = { mode: "photo", start: p, fx: num(focusX), fy: num(focusY), rect: photoRect() };
    else return;
    canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  canvas.addEventListener("pointermove", function (event) {
    var p = point(event);
    if (!drag) { canvas.classList.toggle("is-text", onText(p)); return; }
    if (drag.mode === "text") {
      if (block.freeX > 0) set(textX, (p.x - drag.dx - MARGIN_X) / block.freeX);
      if (block.freeY > 0) set(textY, (p.y - drag.dy - MARGIN_TOP) / block.freeY);
    } else {
      // Fotoğraf imleçle birlikte kayar; taşan yön yoksa o eksende hareket olmaz.
      if (drag.rect.overflowX < 0) set(focusX, drag.fx + (p.x - drag.start.x) / drag.rect.overflowX);
      if (drag.rect.overflowY < 0) set(focusY, drag.fy + (p.y - drag.start.y) / drag.rect.overflowY);
    }
    draw();
  });
  var stop = function () { drag = null; };
  canvas.addEventListener("pointerup", stop);
  canvas.addEventListener("pointercancel", stop);

  form.querySelectorAll("[data-preset]").forEach(function (button) {
    button.addEventListener("click", function () {
      var xy = button.dataset.preset.split(",");
      set(textX, +xy[0]); set(textY, +xy[1]); draw();
    });
  });
  nameInput.addEventListener("input", draw);
  scaleInput.addEventListener("input", draw);
  photoInput.addEventListener("change", function () {
    var file = photoInput.files && photoInput.files[0];
    if (!file) return;
    var url = URL.createObjectURL(file);
    load(url).then(function (img) { photo = img; set(focusX, 0.5); set(focusY, 0.5); draw(); })
      .catch(function () { alert("Fotoğraf açılamadı. JPG, PNG veya WEBP seçin."); photoInput.value = ""; });
  });

  form.addEventListener("submit", function (event) {
    event.preventDefault();
    if (!photo) { alert("Önce proje fotoğrafını seçin."); return; }
    var submit = form.querySelector("[data-submit]");
    submit.disabled = true;
    draw();
    canvas.toBlob(function (blob) {
      if (!blob) { submit.disabled = false; alert("Görsel üretilemedi."); return; }
      var transfer = new DataTransfer();
      transfer.items.add(new File([blob], "gonderi.jpg", { type: "image/jpeg" }));
      imageInput.files = transfer.files;
      form.submit();
    }, "image/jpeg", 0.92);
  });

  var ready = [
    document.fonts.load("800 72px SmManrope"), document.fonts.load("600 30px SmManrope"),
    load(window.SocialProjectEditor.logo).then(function (img) { logo = img; }).catch(function () {}),
  ];
  if (window.SocialProjectEditor.photo) ready.push(load(window.SocialProjectEditor.photo).then(function (img) { photo = img; }));
  Promise.allSettled(ready).then(draw);
  draw();
})();
