// Poz listesini Ercom'daki gibi tek ölçekle çizer: büyük poz büyük, küçük poz küçük görünür.
// Ölçek basılacak sayfa genişliğine göre seçilir (data-width-mm); ekranda da aynı fiziksel boyuttadır.
// Görünüm seçenekleri (ara/dış ölçü, cam ölçüsü, açıklama, siyah-beyaz, ölçek) [data-poz-option] alanlarından okunur ve tarayıcıda hatırlanır.
(() => {
  const KEY = "poz-list-options";
  const defaults = { inner: true, outer: true, glass: true, lines: true, mono: false, zoom: 1 };
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem(KEY) || "{}") || {}; } catch { saved = {}; }
  const controls = [...document.querySelectorAll("[data-poz-option]")];
  const options = { ...defaults, ...(controls.length ? saved : {}) };

  controls.forEach((input) => {
    const name = input.dataset.pozOption;
    if (input.type === "checkbox") input.checked = !!options[name];
    else input.value = options[name];
    input.addEventListener("input", () => {
      options[name] = input.type === "checkbox" ? input.checked : Number(input.value);
      try { localStorage.setItem(KEY, JSON.stringify(options)); } catch { /* yalnızca bu oturum */ }
      draw();
    });
  });

  function draw() {
    document.querySelectorAll("[data-poz-list]").forEach((list) => {
      const svgs = [...list.querySelectorAll("svg[data-poz]")];
      const drawings = svgs.map((svg) => JSON.parse(svg.dataset.poz));
      if (!drawings.length) return;
      const page = Number(list.dataset.widthMm) || 190;
      const maxW = Math.max(...drawings.map((d) => d.widthMm)), maxH = Math.max(...drawings.map((d) => d.heightMm));
      // En yüksek poz ~46 mm (Ercom çıktısına yakın), en geniş poz sayfa genişliğine (ölçü payı düşülerek) sığar; 1/15'ten büyük çizilmez.
      const scale = Math.min(46 / maxH, (page - 32) / maxW, 1 / 15) * (options.zoom || 1);
      const fitted = Math.min(scale, (page - 32) / maxW);
      list.toggleAttribute("data-hide-lines", !options.lines);
      svgs.forEach((svg, i) => WindowDrawing.render(svg, drawings[i], {
        scale: fitted, mono: options.mono, show: { inner: options.inner, outer: options.outer, glass: options.glass }
      }));
    });
  }

  draw();
})();
