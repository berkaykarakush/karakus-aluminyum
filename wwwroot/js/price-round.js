// Fiyat yuvarlama: paneldeki her fiyat alanının yanına "Yuvarla" düğmesi eklenir.
// Menüden adım (tam sayı, 0,50, 10, 50, 100, 500, 1.000) ve yön (en yakın / yukarı / aşağı) seçilir;
// 123,45632 → 123 veya 123,50 gibi. Seçilen yön tarayıcıda hatırlanır.
// Kapsam: step="0.01" olan ve max taşımayan sayı alanları (yüzde alanları max taşır) ile data-round işaretli alanlar.
// Bir alanı dışarıda bırakmak için data-no-round eklenir.
window.PriceRound = (() => {
  const STEPS = [
    { step: 1, label: "Tam sayı" },
    { step: 0.5, label: "0,50'lik" },
    { step: 10, label: "10'luk" },
    { step: 50, label: "50'lik" },
    { step: 100, label: "100'lük" },
    { step: 500, label: "500'lük" },
    { step: 1000, label: "1.000'lik" },
  ];
  const DIRECTIONS = [["nearest", "En yakın"], ["up", "Yukarı"], ["down", "Aşağı"]];
  const KEY = "price_round_direction";
  const SELECTOR = 'input[type=number][step="0.01"]:not([max]):not([data-no-round]), input[data-round]:not([data-no-round])';

  let direction = "nearest";
  try { direction = localStorage.getItem(KEY) || "nearest"; } catch { /* tarayıcı depolaması kapalı olabilir */ }

  function round(value, step, dir = direction) {
    const v = Number(value);
    if (!Number.isFinite(v)) return value;
    const q = v / step;
    const r = dir === "up" ? Math.ceil(q - 1e-9) : dir === "down" ? Math.floor(q + 1e-9) : Math.round(q);
    return Number((r * step).toFixed(2));
  }
  const format = (v) => Number(v).toLocaleString("tr-TR", { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2 });

  let menu = null;
  function close() {
    if (!menu) return;
    menu.anchor.setAttribute("aria-expanded", "false");
    menu.remove();
    menu = null;
    document.removeEventListener("pointerdown", outside, true);
    document.removeEventListener("keydown", onKey, true);
  }
  const outside = (e) => { if (menu && !menu.contains(e.target) && !menu.anchor.contains(e.target)) close(); };
  const onKey = (e) => { if (e.key === "Escape") { const anchor = menu?.anchor; close(); anchor?.focus(); } };

  // anchor: menünün altında açılacağı düğme; getValue: önizleme için güncel değer (toplu yuvarlamada yok);
  // onPick(fn): fn(değer) seçilen adım ve yönle yuvarlanmış değeri döndürür.
  function open(anchor, { getValue, onPick, title }) {
    if (menu?.anchor === anchor) { close(); return; }
    close();
    menu = document.createElement("div");
    menu.className = "price-round-menu";
    menu.setAttribute("role", "menu");
    menu.anchor = anchor;
    anchor.setAttribute("aria-expanded", "true");

    const render = () => {
      menu.textContent = "";
      if (title) menu.append(Object.assign(document.createElement("p"), { className: "price-round-menu__title", textContent: title }));
      const dirs = document.createElement("div");
      dirs.className = "price-round-menu__dirs";
      DIRECTIONS.forEach(([value, label]) => {
        const b = Object.assign(document.createElement("button"), { type: "button", textContent: label });
        b.setAttribute("aria-pressed", String(direction === value));
        b.addEventListener("click", () => { direction = value; try { localStorage.setItem(KEY, value); } catch { } render(); });
        dirs.append(b);
      });
      menu.append(dirs);
      const current = getValue ? Number(getValue()) : NaN;
      STEPS.forEach(({ step, label }) => {
        const b = Object.assign(document.createElement("button"), { type: "button", className: "price-round-menu__item" });
        b.setAttribute("role", "menuitem");
        b.append(Object.assign(document.createElement("span"), { textContent: label }));
        if (Number.isFinite(current) && getValue() !== "") b.append(Object.assign(document.createElement("strong"), { textContent: format(round(current, step)) }));
        b.addEventListener("click", () => { const dir = direction; onPick((v) => round(v, step, dir)); close(); });
        menu.append(b);
      });
    };
    render();
    document.body.append(menu);
    const rect = anchor.getBoundingClientRect();
    const width = menu.offsetWidth;
    menu.style.top = `${rect.bottom + window.scrollY + 4}px`;
    menu.style.left = `${Math.max(8, Math.min(rect.right + window.scrollX - width, window.scrollX + document.documentElement.clientWidth - width - 8))}px`;
    menu.querySelector(".price-round-menu__item")?.focus();
    document.addEventListener("pointerdown", outside, true);
    document.addEventListener("keydown", onKey, true);
  }

  // Tek alan için düğme. Değer değişince alanın kendi input/change dinleyicileri çalışır (toplamlar güncellenir).
  function enhance(input) {
    if (input.dataset.roundDone || input.type === "hidden" || input.closest(".price-round")) return;
    input.dataset.roundDone = "1";
    const wrap = document.createElement("span");
    wrap.className = "price-round";
    const button = Object.assign(document.createElement("button"), { type: "button", className: "price-round__btn", textContent: "Yuvarla", title: "Fiyatı yuvarla" });
    button.setAttribute("aria-haspopup", "menu");
    button.setAttribute("aria-label", `${input.getAttribute("aria-label") || "Fiyatı"} yuvarla`);
    button.addEventListener("click", () => open(button, {
      getValue: () => input.value,
      onPick: (fn) => {
        if (input.value === "") return;
        input.value = String(fn(input.value));
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
      },
    }));
    input.before(wrap);
    wrap.append(input, button);
  }

  // Toplu yuvarlama düğmesi (teklif, sözleşme, fatura kalemleri). onPick(fn) tüm fiyatlara uygulanır.
  function bulkButton(text, onPick) {
    const button = Object.assign(document.createElement("button"), { type: "button", className: "doc-btn", textContent: text });
    button.setAttribute("aria-haspopup", "menu");
    button.addEventListener("click", () => open(button, { onPick, title: "Tüm birim fiyatlar yuvarlanır" }));
    return button;
  }

  const scan = (root) => {
    if (!root.querySelectorAll) return;
    if (root.matches?.(SELECTOR)) enhance(root);
    root.querySelectorAll(SELECTOR).forEach(enhance);
  };
  document.addEventListener("DOMContentLoaded", () => {
    scan(document);
    new MutationObserver((changes) => changes.forEach((change) => change.addedNodes.forEach(scan)))
      .observe(document.body, { childList: true, subtree: true });
  });

  return { round, open, bulkButton, enhance };
})();
