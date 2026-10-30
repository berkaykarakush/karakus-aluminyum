// Ölçü birimi: alanlar kullanıcıya seçtiği birimde görünür, forma ve hesaplara hep milimetre gider.
// Gerçek alan gizlenir, yanına birimli bir kopya eklenir; böylece mevcut kodlar değişmeden milimetre okur.
window.Units = (() => {
  const FACTORS = { mm: 1, cm: 10, m: 1000 };
  const STEPS = { mm: "1", cm: "0.1", m: "0.001" };
  const INTEGER_FIELDS = new Set(["WidthMm", "HeightMm", "ShapeParamMm"]);
  const valueDescriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value");

  const normalize = (code) => (FACTORS[code] ? code : "cm");
  const cookie = () => (document.cookie.match(/(?:^|;\s*)length_unit=([^;]+)/) || [])[1];
  let current = normalize(cookie());

  const store = {
    get(key) { try { return localStorage.getItem(`unit.${key}`); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(`unit.${key}`, value); } catch { /* tarayıcı depolamayı kapatmış olabilir */ } },
    clear() {
      try { Object.keys(localStorage).filter((k) => k.startsWith("unit.")).forEach((k) => localStorage.removeItem(k)); } catch { /* yok say */ }
    }
  };

  const round = (value, code) => Math.round(value * { mm: 10, cm: 100, m: 1000 }[code]) / { mm: 10, cm: 100, m: 1000 }[code];
  const toDisplay = (mm, code = current) => round(Number(mm) / FACTORS[code], code);
  const toMm = (value, code = current) => Number(value) * FACTORS[code];
  const format = (mm, code = current) => toDisplay(mm, code).toLocaleString("tr-TR", { useGrouping: false, maximumFractionDigits: 3 });

  function enhance(real) {
    if (real.dataset.unitDone || real.type === "hidden") return;
    real.dataset.unitDone = "1";
    const key = real.dataset.mm || real.name;
    let code = normalize(store.get(key) || current);
    let fromProxy = false;

    const proxy = document.createElement("input");
    proxy.type = "number";
    proxy.step = "any";
    proxy.required = real.required;
    proxy.placeholder = real.placeholder;
    if (real.getAttribute("aria-label")) proxy.setAttribute("aria-label", real.getAttribute("aria-label"));
    // Kopya, gerçek alanın id'sini alamaz: form.elements[ad] id ile de eşleşir ve kodlar yanlış alanı okur.
    if (real.id) {
      proxy.id = `${real.id}__unit`;
      document.querySelectorAll(`label[for="${real.id}"]`).forEach((label) => { label.htmlFor = proxy.id; });
    }

    // Gizli alandaki doğrulamalar gönderimi sessizce engellemesin; denetim sunucuda yapılır.
    ["min", "max", "required"].forEach((attr) => real.removeAttribute(attr));
    real.step = "any";
    real.classList.add("unit-real");

    const select = document.createElement("select");
    select.className = "unit-select";
    select.setAttribute("aria-label", "Birim");
    Object.keys(FACTORS).forEach((unit) => select.add(new Option(unit, unit, false, unit === code)));

    const show = () => {
      const raw = valueDescriptor.get.call(real);
      proxy.value = raw === "" ? "" : toDisplay(raw, code);
      proxy.step = STEPS[code];
    };

    // Kodun gerçek alana yazdığı değer de görünen kopyaya yansısın.
    Object.defineProperty(real, "value", {
      configurable: true,
      get: () => valueDescriptor.get.call(real),
      set: (next) => { valueDescriptor.set.call(real, next); if (!fromProxy) show(); }
    });

    const push = (eventName) => {
      fromProxy = true;
      if (proxy.value === "") real.value = "";
      else {
        const mm = toMm(proxy.value, code);
        real.value = INTEGER_FIELDS.has(real.name) ? Math.round(mm) : Math.round(mm * 10) / 10;
      }
      fromProxy = false;
      real.dispatchEvent(new Event(eventName, { bubbles: true }));
    };
    proxy.addEventListener("input", (event) => { event.stopPropagation(); push("input"); });
    proxy.addEventListener("change", (event) => { event.stopPropagation(); push("change"); });
    select.addEventListener("change", (event) => {
      event.stopPropagation();
      code = select.value;
      store.set(key, code);
      show();
    });
    select.addEventListener("input", (event) => event.stopPropagation());
    document.addEventListener("units:change", () => {
      code = current;
      select.value = code;
      show();
    });

    const wrap = document.createElement("span");
    wrap.className = "unit-field";
    real.before(wrap);
    wrap.append(proxy, select, real);
    show();
  }

  const scan = (root) => {
    if (!root.querySelectorAll) return;
    root.querySelectorAll('input[name$="Mm"], input[data-mm]').forEach(enhance);
  };

  function setGlobal(code) {
    current = normalize(code);
    document.cookie = `length_unit=${current}; path=/; max-age=31536000; samesite=lax`;
    store.clear();
    // Ölçü girilen sayfada yazılanlar kaybolmasın; diğer sayfalar sunucudan yeni birimle gelir.
    if (document.querySelector(".unit-field")) document.dispatchEvent(new CustomEvent("units:change"));
    else location.reload();
  }

  document.addEventListener("DOMContentLoaded", () => {
    scan(document);
    new MutationObserver((changes) => changes.forEach((change) => change.addedNodes.forEach(scan)))
      .observe(document.body, { childList: true, subtree: true });
    const global = document.getElementById("unit-global");
    if (global) {
      global.value = current;
      global.addEventListener("change", () => setGlobal(global.value));
    }
  });

  return { get code() { return current; }, format, toMm, toDisplay };
})();
